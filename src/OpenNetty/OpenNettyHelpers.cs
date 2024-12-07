/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Concurrent;

namespace OpenNetty;

/// <summary>
/// Exposes common helpers used by the OpenNetty assemblies.
/// </summary>
internal static class OpenNettyHelpers
{
    /// <summary>
    /// Converts an async-observable sequence to an async-enumerable sequence.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <param name="source">Async-observable sequence to convert to an async-enumerable sequence.</param>
    /// <returns>The async-enumerable sequence whose elements are pulled from the given async-observable sequence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public static IAsyncEnumerable<TSource> ToAsyncEnumerable<TSource>(this IAsyncObservable<TSource> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new AsyncObservableAsyncEnumerable<TSource>(source);
    }

    // REVIEW: The base class below was introduced to avoid the overhead of storing a field of type TSource if the
    //         value of the iterator can trivially be inferred from another field (e.g. in Repeat). It is also used
    //         by the Defer operator in System.Interactive.Async. For some operators such as Where, Skip, Take, and
    //         Concat, it could be used to retrieve the value from the underlying enumerator. However, performance
    //         of this approach is a bit worse in some cases, so we don't go ahead with it for now. One decision to
    //         make is whether it's okay for Current to throw an exception when MoveNextAsync returns false, e.g.
    //         by omitting a null check for an enumerator field.

    internal abstract partial class AsyncIteratorBase<TSource> : IAsyncEnumerable<TSource>, IAsyncEnumerator<TSource>
    {
        private readonly int _threadId;

        protected AsyncIteratorState _state = AsyncIteratorState.New;
        protected CancellationToken _cancellationToken;

        protected AsyncIteratorBase()
        {
            _threadId = Environment.CurrentManagedThreadId;
        }

        public IAsyncEnumerator<TSource> GetAsyncEnumerator(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); // NB: [LDM-2018-11-28] Equivalent to async iterator behavior.

            var enumerator = _state == AsyncIteratorState.New && _threadId == Environment.CurrentManagedThreadId
                ? this
                : Clone();

            enumerator._state = AsyncIteratorState.Allocated;
            enumerator._cancellationToken = cancellationToken;

            // REVIEW: If the final interface contains a CancellationToken here, should we check for a cancellation request
            //         either here or in the first call to MoveNextAsync?

            return enumerator;
        }

        public virtual ValueTask DisposeAsync()
        {
            _state = AsyncIteratorState.Disposed;

            return default;
        }

        public abstract TSource Current { get; }

        public async ValueTask<bool> MoveNextAsync()
        {
            // Note: MoveNext *must* be implemented as an async method to ensure
            // that any exceptions thrown from the MoveNextCore call are handled 
            // by the try/catch, whether they're sync or async

            if (_state == AsyncIteratorState.Disposed)
            {
                return false;
            }

            try
            {
                return await MoveNextCore().ConfigureAwait(false);
            }
            catch
            {
                await DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public abstract AsyncIteratorBase<TSource> Clone();

        protected abstract ValueTask<bool> MoveNextCore();
    }

    internal abstract class AsyncIterator<TSource> : AsyncIteratorBase<TSource>
    {
        protected TSource _current = default!;

        public override TSource Current => _current;

        public override ValueTask DisposeAsync()
        {
            _current = default!;

            return base.DisposeAsync();
        }
    }

    internal enum AsyncIteratorState
    {
        New = 0,
        Allocated = 1,
        Iterating = 2,
        Disposed = -1,
    }

    private sealed class AsyncObservableAsyncEnumerable<TSource> : AsyncIterator<TSource>, IAsyncObserver<TSource>
    {
        private readonly IAsyncObservable<TSource> _source;

        private ConcurrentQueue<TSource>? _values = new();
        private Exception? _error;
        private bool _completed;
        private TaskCompletionSource<bool>? _signal;
        private IAsyncDisposable? _subscription;
        private CancellationTokenRegistration _ctr;

        public AsyncObservableAsyncEnumerable(IAsyncObservable<TSource> source) => _source = source;

        public override AsyncIteratorBase<TSource> Clone() => new AsyncObservableAsyncEnumerable<TSource>(_source);

        protected override async ValueTask<bool> MoveNextCore()
        {
            //
            // REVIEW: How often should we check? At the very least, we want to prevent
            //         subscribing if cancellation is requested. A case may be made to
            //         check for each iteration, namely because this operator is a bridge
            //         with another interface. However, we also wire up cancellation to
            //         the observable subscription, so there's redundancy here.
            //
            _cancellationToken.ThrowIfCancellationRequested();

            switch (_state)
            {
                case AsyncIteratorState.Allocated:
                    //
                    // NB: Breaking change to align with lazy nature of async iterators.
                    //
                    //     In previous implementations, the Subscribe call happened during
                    //     the call to GetAsyncEnumerator.
                    //
                    // REVIEW: Confirm this design point. This implementation is compatible
                    //         with an async iterator using "yield return", e.g. subscribing
                    //         to the observable sequence and yielding values out of a local
                    //         queue filled by observer callbacks. However, it departs from
                    //         the dual treatment of Subscribe/GetEnumerator.
                    //

                    _subscription = await _source.SubscribeAsync(this);
                    _ctr = _cancellationToken.Register(async () => await OnCanceledAsync());
                    _state = AsyncIteratorState.Iterating;
                    goto case AsyncIteratorState.Iterating;

                case AsyncIteratorState.Iterating:
                    while (true)
                    {
                        var completed = Volatile.Read(ref _completed);

                        if (_values!.TryDequeue(out _current!))
                        {
                            return true;
                        }
                        else if (completed)
                        {
                            var error = _error;

                            if (error != null)
                            {
                                throw error;
                            }

                            return false;
                        }

                        await Resume().ConfigureAwait(false);
                        Volatile.Write(ref _signal, null);
                    }
            }

            await DisposeAsync().ConfigureAwait(false);
            return false;
        }

        public async ValueTask OnCompletedAsync()
        {
            Volatile.Write(ref _completed, true);

            await DisposeSubscriptionAsync();
            OnNotification();
        }

        public async ValueTask OnErrorAsync(Exception error)
        {
            _error = error;
            Volatile.Write(ref _completed, true);

            await DisposeSubscriptionAsync();
            OnNotification();
        }

        public ValueTask OnNextAsync(TSource value)
        {
            _values?.Enqueue(value);

            OnNotification();

            return default;
        }

        private void OnNotification()
        {
            while (true)
            {
                var signal = Volatile.Read(ref _signal);

                if (signal == TaskExt.True)
                {
                    return;
                }

                if (signal != null)
                {
                    signal.TrySetResult(true);
                    return;
                }

                if (Interlocked.CompareExchange(ref _signal, TaskExt.True, null) == null)
                {
                    return;
                }
            }
        }

        public override async ValueTask DisposeAsync()
        {
            await _ctr.DisposeAsync();
            await DisposeSubscriptionAsync();

            _values = null;
            _error = null;
        }

        private ValueTask DisposeSubscriptionAsync() => Interlocked.Exchange(ref _subscription, null)?.DisposeAsync() ?? default;

        private async ValueTask OnCanceledAsync()
        {
            var cancelledTcs = default(TaskCompletionSource<bool>);

            await DisposeAsync();

            while (true)
            {
                var signal = Volatile.Read(ref _signal);

                if (signal != null)
                {
                    if (signal.TrySetCanceled(_cancellationToken))
                        return;
                }

                if (cancelledTcs == null)
                {
                    cancelledTcs = new TaskCompletionSource<bool>();
                    cancelledTcs.TrySetCanceled(_cancellationToken);
                }

                if (Interlocked.CompareExchange(ref _signal, cancelledTcs, signal) == signal)
                    return;
            }
        }

        private Task Resume()
        {
            TaskCompletionSource<bool>? newSignal = null;

            while (true)
            {
                var signal = Volatile.Read(ref _signal);

                if (signal != null)
                {
                    return signal.Task;
                }

                newSignal ??= new TaskCompletionSource<bool>();

                if (Interlocked.CompareExchange(ref _signal, newSignal, null) == null)
                {
                    return newSignal.Task;
                }
            }
        }
    }

    internal static class TaskExt
    {
        public static readonly TaskCompletionSource<bool> True;

        static TaskExt()
        {
            True = new TaskCompletionSource<bool>();
            True.SetResult(true);
        }
    }
}
