/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using NSubstitute;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyHelpersTests
{
    private const int TestTimeout = 10_000;

    [Fact]
    public void ToAsyncEnumerable_NullSource_ThrowsArgumentNullException()
    {
        IAsyncObservable<int> source = null!;

        Assert.Throws<ArgumentNullException>("source", () => source.ToAsyncEnumerable());
    }

    [Fact(Timeout = TestTimeout)]
    public async Task ToAsyncEnumerable_DoesNotSubscribeBeforeEnumeration()
    {
        var fixture = new ObservableFixture<int>();

        var enumerable = fixture.Source.ToAsyncEnumerable();
        await using var enumerator = enumerable.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        await fixture.Source.DidNotReceive().SubscribeAsync(Arg.Any<IAsyncObserver<int>>());
    }

    [Fact(Timeout = TestTimeout)]
    public async Task ToAsyncEnumerable_EmittedValues_AreReturnedInOrderUntilCompletion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = new ObservableFixture<int>();
        var consumer = ToListAsync(fixture.Source.ToAsyncEnumerable(), cancellationToken);
        var observer = await fixture.Subscribed.Task;

        await observer.OnNextAsync(1);
        await observer.OnNextAsync(2);
        await observer.OnNextAsync(3);
        await observer.OnCompletedAsync();

        Assert.Equal([1, 2, 3], await consumer);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task ToAsyncEnumerable_ValuesEmittedBeforeBeingConsumed_AreBuffered()
    {
        var fixture = new ObservableFixture<string>();
        await using var enumerator = fixture.Source.ToAsyncEnumerable().GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var firstMove = enumerator.MoveNextAsync().AsTask();
        var observer = await fixture.Subscribed.Task;

        await observer.OnNextAsync("a");
        await observer.OnNextAsync("b");
        await observer.OnCompletedAsync();

        Assert.True(await firstMove);
        Assert.Equal("a", enumerator.Current);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("b", enumerator.Current);
        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact(Timeout = TestTimeout)]
    public async Task ToAsyncEnumerable_CompletionWithoutValues_ReturnsEmptySequence()
    {
        var fixture = new ObservableFixture<int>();
        var consumer = ToListAsync(fixture.Source.ToAsyncEnumerable(), TestContext.Current.CancellationToken);
        var observer = await fixture.Subscribed.Task;

        await observer.OnCompletedAsync();

        Assert.Empty(await consumer);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task ToAsyncEnumerable_Completion_DisposesTheSubscription()
    {
        var fixture = new ObservableFixture<int>();
        var consumer = ToListAsync(fixture.Source.ToAsyncEnumerable(), TestContext.Current.CancellationToken);
        var observer = await fixture.Subscribed.Task;

        await observer.OnCompletedAsync();
        await consumer;

        await fixture.Subscription.Received(1).DisposeAsync();
    }

    [Fact(Timeout = TestTimeout)]
    public async Task ToAsyncEnumerable_Error_IsThrownAfterTheBufferedValuesWereReturned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = new ObservableFixture<int>();
        var values = new List<int>();
        var error = new InvalidOperationException("Failure.");
        var consumer = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var value in fixture.Source.ToAsyncEnumerable().WithCancellation(cancellationToken))
            {
                values.Add(value);
            }
        });
        var observer = await fixture.Subscribed.Task;

        await observer.OnNextAsync(1);
        await observer.OnNextAsync(2);
        await observer.OnErrorAsync(error);

        Assert.Same(error, await consumer);
        Assert.Equal([1, 2], values);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task ToAsyncEnumerable_Error_DisposesTheSubscription()
    {
        var fixture = new ObservableFixture<int>();
        var consumer = Assert.ThrowsAsync<InvalidOperationException>(
            () => ToListAsync(fixture.Source.ToAsyncEnumerable(), TestContext.Current.CancellationToken));
        var observer = await fixture.Subscribed.Task;

        await observer.OnErrorAsync(new InvalidOperationException());
        await consumer;

        await fixture.Subscription.Received(1).DisposeAsync();
    }

    [Fact]
    public void ToAsyncEnumerable_CanceledToken_ThrowsWhenGettingTheEnumerator()
    {
        var fixture = new ObservableFixture<int>();
        var enumerable = fixture.Source.ToAsyncEnumerable();

        Assert.Throws<OperationCanceledException>(() => enumerable.GetAsyncEnumerator(new CancellationToken(canceled: true)));
    }

    [Fact(Timeout = TestTimeout)]
    public async Task ToAsyncEnumerable_TokenCanceledBeforeTheFirstMove_ThrowsWithoutSubscribing()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var fixture = new ObservableFixture<int>();
        await using var enumerator = fixture.Source.ToAsyncEnumerable().GetAsyncEnumerator(source.Token);

        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
        await fixture.Source.DidNotReceive().SubscribeAsync(Arg.Any<IAsyncObserver<int>>());
    }

    [Fact(Timeout = TestTimeout)]
    public async Task ToAsyncEnumerable_TokenCanceledWhileWaiting_ThrowsOperationCanceledException()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var fixture = new ObservableFixture<int>();
        var consumer = Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ToListAsync(fixture.Source.ToAsyncEnumerable(), source.Token));
        await fixture.Subscribed.Task;

        await source.CancelAsync();

        await consumer;
    }

    [Fact(Timeout = TestTimeout)]
    public async Task ToAsyncEnumerable_TokenCanceledWhileWaiting_DisposesTheSubscription()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var fixture = new ObservableFixture<int>();
        var consumer = Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ToListAsync(fixture.Source.ToAsyncEnumerable(), source.Token));
        await fixture.Subscribed.Task;

        await source.CancelAsync();
        await consumer;

        await fixture.Subscription.Received().DisposeAsync();
    }

    [Fact(Timeout = TestTimeout)]
    public async Task ToAsyncEnumerable_EnumeratedTwice_SubscribesOncePerEnumeration()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var fixture = new ObservableFixture<int>();
        var enumerable = fixture.Source.ToAsyncEnumerable();
        var first = ToListAsync(enumerable, source.Token);
        var second = ToListAsync(enumerable, source.Token);

        await fixture.Source.Received(2).SubscribeAsync(Arg.Any<IAsyncObserver<int>>());

        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task ToAsyncEnumerable_MoveNextAsyncAfterDisposal_ReturnsFalse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = new ObservableFixture<int>();
        var enumerator = fixture.Source.ToAsyncEnumerable().GetAsyncEnumerator(cancellationToken);
        var firstMove = enumerator.MoveNextAsync().AsTask();
        var observer = await fixture.Subscribed.Task;
        await observer.OnNextAsync(1);
        await firstMove;

        await enumerator.DisposeAsync();

        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task TaskExt_True_IsACompletedTaskReturningTrue()
    {
        Assert.True(OpenNettyHelpers.TaskExt.True.Task.IsCompletedSuccessfully);
        Assert.True(await OpenNettyHelpers.TaskExt.True.Task);
    }

    [Fact]
    public async Task AsyncIterator_GetAsyncEnumerator_FirstCallOnTheCreatingThread_ReturnsTheSameInstance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var iterator = new CountingIterator(count: 1);

        var enumerator = iterator.GetAsyncEnumerator(cancellationToken);

        Assert.Same(iterator, enumerator);
        Assert.Equal(OpenNettyHelpers.AsyncIteratorState.Allocated, iterator.State);
        Assert.Equal(cancellationToken, iterator.Token);
        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task AsyncIterator_GetAsyncEnumerator_SecondCall_ReturnsAClone()
    {
        var iterator = new CountingIterator(count: 1);
        await using var first = iterator.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        await using var second = iterator.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.NotSame(first, second);
        Assert.IsType<CountingIterator>(second);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task AsyncIterator_GetAsyncEnumerator_FromAnotherThread_ReturnsAClone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var iterator = new CountingIterator(count: 1);

        // Note: a dedicated thread is used to guarantee the iterator is not accessed by the thread that created it
        // (Task.Run() may run the delegate on the same thread-pool thread once the test method yields).
        var enumerator = await Task.Factory.StartNew(
            () => iterator.GetAsyncEnumerator(cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        Assert.NotSame(iterator, enumerator);
        Assert.Equal(OpenNettyHelpers.AsyncIteratorState.New, iterator.State);
        await enumerator.DisposeAsync();
    }

    [Fact]
    public void AsyncIterator_GetAsyncEnumerator_CanceledToken_ThrowsOperationCanceledException()
    {
        var iterator = new CountingIterator(count: 1);

        Assert.Throws<OperationCanceledException>(() => iterator.GetAsyncEnumerator(new CancellationToken(canceled: true)));
        Assert.Equal(OpenNettyHelpers.AsyncIteratorState.New, iterator.State);
    }

    [Fact]
    public async Task AsyncIterator_MoveNextAsync_ReturnsItemsThenFalse()
    {
        await using var iterator = new CountingIterator(count: 2).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await iterator.MoveNextAsync());
        Assert.Equal(1, iterator.Current);
        Assert.True(await iterator.MoveNextAsync());
        Assert.Equal(2, iterator.Current);
        Assert.False(await iterator.MoveNextAsync());
    }

    [Fact]
    public async Task AsyncIterator_MoveNextAsync_Failure_RethrowsAndDisposesTheIterator()
    {
        var iterator = new CountingIterator(count: 1, failure: new InvalidOperationException("Failure."));
        await using var enumerator = iterator.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await enumerator.MoveNextAsync());

        Assert.Equal("Failure.", exception.Message);
        Assert.Equal(OpenNettyHelpers.AsyncIteratorState.Disposed, iterator.State);
    }

    [Fact]
    public async Task AsyncIterator_MoveNextAsync_AfterDisposal_ReturnsFalse()
    {
        var enumerator = new CountingIterator(count: 5).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await enumerator.MoveNextAsync();
        await enumerator.DisposeAsync();

        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task AsyncIterator_DisposeAsync_ResetsTheCurrentValueAndMarksTheIteratorAsDisposed()
    {
        var iterator = new CountingIterator(count: 5);
        await using var enumerator = iterator.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await enumerator.MoveNextAsync();

        await enumerator.DisposeAsync();

        Assert.Equal(0, enumerator.Current);
        Assert.Equal(OpenNettyHelpers.AsyncIteratorState.Disposed, iterator.State);
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source, CancellationToken cancellationToken)
    {
        var values = new List<T>();

        await foreach (var value in source.WithCancellation(cancellationToken))
        {
            values.Add(value);
        }

        return values;
    }

    private sealed class ObservableFixture<T>
    {
        public ObservableFixture()
        {
            // Note: NSubstitute requires the ValueTask-returning call to be used as the receiver of Returns().
#pragma warning disable CA2012
            Source.SubscribeAsync(Arg.Any<IAsyncObserver<T>>()).Returns(call =>
            {
                Subscribed.TrySetResult(call.Arg<IAsyncObserver<T>>());

                return new ValueTask<IAsyncDisposable>(Subscription);
            });
#pragma warning restore CA2012
        }

        public IAsyncObservable<T> Source { get; } = Substitute.For<IAsyncObservable<T>>();

        public IAsyncDisposable Subscription { get; } = Substitute.For<IAsyncDisposable>();

        public TaskCompletionSource<IAsyncObserver<T>> Subscribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class CountingIterator : OpenNettyHelpers.AsyncIterator<int>
    {
        private readonly int _count;
        private readonly Exception? _failure;
        private int _index;

        public CountingIterator(int count, Exception? failure = null)
        {
            _count = count;
            _failure = failure;
        }

        public OpenNettyHelpers.AsyncIteratorState State => _state;

        public CancellationToken Token => _cancellationToken;

        public override OpenNettyHelpers.AsyncIteratorBase<int> Clone() => new CountingIterator(_count, _failure);

        protected override ValueTask<bool> MoveNextCore()
        {
            if (_failure is not null)
            {
                throw _failure;
            }

            if (_index >= _count)
            {
                return new ValueTask<bool>(false);
            }

            _current = ++_index;

            return new ValueTask<bool>(true);
        }
    }
}
