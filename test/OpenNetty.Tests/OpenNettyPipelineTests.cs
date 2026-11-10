/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyPipelineTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Pipeline_ImplementsExpectedContracts()
    {
        var pipeline = new OpenNettyPipeline(CreateLifetime(new CancellationTokenSource()));

        Assert.IsAssignableFrom<IOpenNettyPipeline>(pipeline);
        Assert.IsAssignableFrom<IAsyncDisposable>(pipeline);
    }

    [Fact]
    public async Task ApplicationStopping_Triggered_CompletesObserversAfterPendingNotifications()
    {
        using var stopping = new CancellationTokenSource();
        var pipeline = new OpenNettyPipeline(CreateLifetime(stopping));
        var observer = new RecordingObserver();
        await using var subscription = await pipeline.SubscribeAsync(observer);
        await pipeline.PublishAsync(CreateNotification(1), TestContext.Current.CancellationToken);
        await pipeline.PublishAsync(CreateNotification(2), TestContext.Current.CancellationToken);

        await stopping.CancelAsync();
        await using var connection = await pipeline.ConnectAsync();

        Assert.Equal(1, (await observer.NextAsync()).Number);
        Assert.Equal(2, (await observer.NextAsync()).Number);
        await observer.Completed.WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SubscribeAsync_MultipleObservers_EachObserverReceivesNotifications()
    {
        using var stopping = new CancellationTokenSource();
        var pipeline = new OpenNettyPipeline(CreateLifetime(stopping));
        var first = new RecordingObserver();
        var second = new RecordingObserver();
        await using var firstSubscription = await pipeline.SubscribeAsync(first);
        await using var secondSubscription = await pipeline.SubscribeAsync(second);
        await using var connection = await pipeline.ConnectAsync();

        var notification = CreateNotification(1);
        await pipeline.PublishAsync(notification, TestContext.Current.CancellationToken);

        Assert.Same(notification, await first.NextAsync());
        Assert.Same(notification, await second.NextAsync());
    }

    [Fact]
    public async Task SubscribeAsync_DisposedSubscription_StopsReceivingNotifications()
    {
        using var stopping = new CancellationTokenSource();
        var pipeline = new OpenNettyPipeline(CreateLifetime(stopping));
        var disposed = new RecordingObserver();
        var active = new RecordingObserver();
        var subscription = await pipeline.SubscribeAsync(disposed);
        await using var activeSubscription = await pipeline.SubscribeAsync(active);
        await using var connection = await pipeline.ConnectAsync();
        await subscription.DisposeAsync();

        await pipeline.PublishAsync(CreateNotification(1), TestContext.Current.CancellationToken);

        Assert.Equal(1, (await active.NextAsync()).Number);
        Assert.False(disposed.TryNext(out _));
    }

    [Fact]
    public async Task PublishAsync_ConnectedPipeline_DeliversNotificationsInPublicationOrder()
    {
        using var stopping = new CancellationTokenSource();
        var pipeline = new OpenNettyPipeline(CreateLifetime(stopping));
        var observer = new RecordingObserver();
        await using var subscription = await pipeline.SubscribeAsync(observer);
        await using var connection = await pipeline.ConnectAsync();

        for (var number = 0; number < 50; number++)
        {
            await pipeline.PublishAsync(CreateNotification(number), TestContext.Current.CancellationToken);
        }

        for (var number = 0; number < 50; number++)
        {
            Assert.Equal(number, (await observer.NextAsync()).Number);
        }
    }

    [Fact]
    public async Task PublishAsync_BeforeConnect_DeliversNotificationsOnceConnected()
    {
        using var stopping = new CancellationTokenSource();
        var pipeline = new OpenNettyPipeline(CreateLifetime(stopping));
        var observer = new RecordingObserver();
        await using var subscription = await pipeline.SubscribeAsync(observer);

        await pipeline.PublishAsync(CreateNotification(1), TestContext.Current.CancellationToken);
        await pipeline.PublishAsync(CreateNotification(2), TestContext.Current.CancellationToken);

        Assert.False(observer.TryNext(out _));
        await using var connection = await pipeline.ConnectAsync();
        Assert.Equal(1, (await observer.NextAsync()).Number);
        Assert.Equal(2, (await observer.NextAsync()).Number);
    }

    [Fact]
    public async Task PublishAsync_ApplicationStopping_ThrowsChannelClosedException()
    {
        using var stopping = new CancellationTokenSource();
        var pipeline = new OpenNettyPipeline(CreateLifetime(stopping));
        await stopping.CancelAsync();

        await Assert.ThrowsAsync<ChannelClosedException>(
            async () => await pipeline.PublishAsync(CreateNotification(1), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishAsync_ApplicationAlreadyStopping_ThrowsChannelClosedException()
    {
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();
        var pipeline = new OpenNettyPipeline(CreateLifetime(stopping));

        await Assert.ThrowsAsync<ChannelClosedException>(
            async () => await pipeline.PublishAsync(CreateNotification(1), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        using var stopping = new CancellationTokenSource();
        var pipeline = new OpenNettyPipeline(CreateLifetime(stopping));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await pipeline.PublishAsync(CreateNotification(1), source.Token));
    }

    [Fact]
    public async Task DisposeAsync_ApplicationStopping_NoLongerClosesPipeline()
    {
        using var stopping = new CancellationTokenSource();
        var pipeline = new OpenNettyPipeline(CreateLifetime(stopping));
        await pipeline.DisposeAsync();

        await stopping.CancelAsync();

        var exception = await Record.ExceptionAsync(
            async () => await pipeline.PublishAsync(CreateNotification(1), TestContext.Current.CancellationToken));
        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        using var stopping = new CancellationTokenSource();
        var pipeline = new OpenNettyPipeline(CreateLifetime(stopping));

        await pipeline.DisposeAsync();
        var exception = await Record.ExceptionAsync(async () => await pipeline.DisposeAsync());

        Assert.Null(exception);
    }

    private static IHostApplicationLifetime CreateLifetime(CancellationTokenSource stopping)
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(stopping.Token);

        return lifetime;
    }

    private static TestNotification CreateNotification(int number) => new()
    {
        Gateway = OpenNettyGateway.Create(
            OpenNettyDevice.Create(OpenNettyBrand.BTicino, "F454", "Gateway", identifier: null),
            new IPEndPoint(IPAddress.Loopback, 20000)),
        Number = number
    };

    private sealed class TestNotification : OpenNettyNotification
    {
        public required int Number { get; init; }
    }

    private sealed class RecordingObserver : IAsyncObserver<OpenNettyNotification>
    {
        private readonly Channel<OpenNettyNotification> _notifications = Channel.CreateUnbounded<OpenNettyNotification>();
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completed => _completed.Task;

        public async Task<TestNotification> NextAsync()
            => (TestNotification) await _notifications.Reader.ReadAsync(TestContext.Current.CancellationToken)
                .AsTask().WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);

        public bool TryNext(out OpenNettyNotification? notification) => _notifications.Reader.TryRead(out notification);

        public ValueTask OnNextAsync(OpenNettyNotification value)
        {
            _notifications.Writer.TryWrite(value);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnErrorAsync(Exception error)
        {
            _completed.TrySetException(error);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnCompletedAsync()
        {
            _completed.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
