using System.ComponentModel;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace OpenNetty;

/// <summary>
/// Represents a thread-safe notification pipeline that can be observed and whose delivery order is guaranteed.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class OpenNettyPipeline : IOpenNettyPipeline, IDisposable
{
    private readonly Channel<OpenNettyNotification> _channel = Channel.CreateUnbounded<OpenNettyNotification>(new UnboundedChannelOptions
    {
        AllowSynchronousContinuations = false,
        SingleReader = true,
        SingleWriter = false
    });

    private readonly IConnectableAsyncObservable<OpenNettyNotification> _observable;
    private readonly CancellationTokenRegistration _registration;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyPipeline"/> class.
    /// </summary>
    /// <param name="lifetime">The host application lifetime.</param>
    public OpenNettyPipeline(IHostApplicationLifetime lifetime)
    {
        _observable = AsyncObservable.Create<OpenNettyNotification>(observer =>
        {
            return TaskPoolAsyncScheduler.Default.ScheduleAsync(async cancellationToken =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (!await _channel.Reader.WaitToReadAsync(cancellationToken))
                        {
                            await observer.OnCompletedAsync();
                            return;
                        }

                        while (_channel.Reader.TryRead(out OpenNettyNotification? notification))
                        {
                            await observer.OnNextAsync(notification);
                        }
                    }

                    catch (ChannelClosedException)
                    {
                        await observer.OnCompletedAsync();
                        return;
                    }

                    catch (Exception exception)
                    {
                        await observer.OnErrorAsync(exception);
                    }
                }
            });
        })
        .Retry()
        .Multicast(new ConcurrentSimpleAsyncSubject<OpenNettyNotification>());

        // Marks the channel as completed when the host indicates the application is shutting down.
        _registration = lifetime.ApplicationStopping.Register(static state =>
            ((OpenNettyPipeline) state!)._channel.Writer.TryComplete(), this);
    }

    /// <summary>
    /// Registers a new notification observer.
    /// </summary>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation and whose
    /// result is used by the caller to indicate that the subscription should be aborted and discarded.
    /// </returns>
    public ValueTask<IAsyncDisposable> SubscribeAsync(IAsyncObserver<OpenNettyNotification> observer)
        => _observable.ObserveOn(TaskPoolAsyncScheduler.Default).SubscribeAsync(observer);

    /// <inheritdoc/>
    public ValueTask PublishAsync(OpenNettyNotification notification, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(notification, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<IAsyncDisposable> ConnectAsync() => _observable.ConnectAsync();

    /// <inheritdoc/>
    public void Dispose() => _registration.Dispose();
}
