/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyHostedServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly OpenNettyEndpoint Endpoint = new()
    {
        Name = "Endpoint",
        Protocol = OpenNettyProtocol.Scs
    };

    [Theory]
    [InlineData("events")]
    [InlineData("handlers")]
    [InlineData("logger")]
    [InlineData("options")]
    [InlineData("pipeline")]
    [InlineData("worker")]
    public void Constructor_NullArgument_ThrowsArgumentNullException(string parameter)
    {
        using var stopping = new CancellationTokenSource();
        var lifetime = CreateLifetime(stopping);

        var exception = Assert.Throws<ArgumentNullException>(() => new OpenNettyHostedService(
            parameter is "events"   ? null! : new OpenNettyEvents(lifetime),
            parameter is "handlers" ? null! : [],
            parameter is "logger"   ? null! : new RecordingLogger(),
            parameter is "options"  ? null! : Substitute.For<IOptionsMonitor<OpenNettyOptions>>(),
            parameter is "pipeline" ? null! : Substitute.For<IOpenNettyPipeline>(),
            parameter is "worker"   ? null! : Substitute.For<IOpenNettyWorker>()));

        Assert.Equal(parameter, exception.ParamName);
    }

    [Fact]
    public async Task StartAsync_NoGateway_LogsStartupAndDoesNotStartWorker()
    {
        await using var harness = new ServiceHarness();

        await harness.StartAsync();
        await harness.Logger.WaitAsync(6001);

        Assert.Equal([6000, 6001], harness.Logger.EventIds);
        Assert.Empty(harness.Worker.Calls);
        await harness.Service.ExecuteTask!.WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartAsync_Handlers_AreSubscribedInOrderBeforeWorkersAreStarted()
    {
        List<string> calls = [];
        var first = CreateHandler("first", calls);
        var second = CreateHandler("second", calls);
        await using var harness = new ServiceHarness(handlers: [first, second]);
        harness.Worker.Started = calls.Add;
        harness.AddGateway(20001);

        await harness.StartAsync();
        await harness.Worker.NextCallAsync();

        Assert.Equal(["first", "second", "worker"], calls);
    }

    [Fact]
    public async Task StartAsync_Gateways_StartsOneWorkerPerGatewayWithItsWorkerOptions()
    {
        await using var harness = new ServiceHarness();
        var first = harness.AddGateway(20001);
        var second = harness.AddGateway(20002);

        await harness.StartAsync();
        var calls = new[] { await harness.Worker.NextCallAsync(), await harness.Worker.NextCallAsync() };

        Assert.Equal([first, second], calls.Select(static call => call.Gateway).Order(new GatewayPortComparer()));
        Assert.All(calls, call => Assert.Same(call.Gateway.Options.DefaultWorkerOptions, call.Options));
    }

    [Fact]
    public async Task StartAsync_StartedWorker_ReceivesNotificationsOfItsGatewayOnly()
    {
        await using var harness = new ServiceHarness();
        var first = harness.AddGateway(20001);
        var second = harness.AddGateway(20002);
        await harness.StartAsync();
        var firstCall = await harness.Worker.NextCallAsync(first);
        await harness.Worker.NextCallAsync(second);
        await harness.Logger.WaitAsync(6001);

        var foreign = new UnknownNotification { Gateway = second };
        var expected = new UnknownNotification { Gateway = first };
        await harness.Pipeline.PublishAsync(foreign, TestContext.Current.CancellationToken);
        await harness.Pipeline.PublishAsync(expected, TestContext.Current.CancellationToken);

        var notification = await firstCall.Reader.ReadAsync(TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);
        Assert.Same(expected, notification);
    }

    [Fact]
    public async Task StartAsync_NotificationWrittenByWorker_IsPublishedInPipeline()
    {
        await using var harness = new ServiceHarness();
        var gateway = harness.AddGateway(20001);
        var observer = new RecordingObserver<OpenNettyNotification>();
        await using var subscription = await harness.Pipeline.SubscribeAsync(observer);
        await harness.StartAsync();
        var call = await harness.Worker.NextCallAsync();
        await harness.Logger.WaitAsync(6001);

        var notification = new UnknownNotification { Gateway = gateway };
        await call.Writer.WriteAsync(notification, TestContext.Current.CancellationToken);

        Assert.Same(notification, await observer.NextAsync());
    }

    [Fact]
    public async Task StartAsync_EventsAndPipeline_AreConnectedBeforeStartupIsReported()
    {
        await using var harness = new ServiceHarness();
        var observer = new RecordingObserver<OpenNettyEvents.BatteryAlertReportedEventArgs>();
        await using var subscription = await harness.Events.BatteryAlertReported.SubscribeAsync(observer);
        var arguments = new OpenNettyEvents.BatteryAlertReportedEventArgs(Endpoint);

        await harness.Events.PublishAsync(arguments, TestContext.Current.CancellationToken);
        Assert.False(observer.TryNext(out _));
        await harness.StartAsync();
        await harness.Logger.WaitAsync(6001);

        Assert.Same(arguments, await observer.NextAsync());
    }

    [Fact]
    public async Task StopAsync_RunningService_CompletesGracefullyAndDisposesHandlerSubscriptions()
    {
        var subscription = Substitute.For<IAsyncDisposable>();
        var handler = Substitute.For<IOpenNettyHandler>();
#pragma warning disable CA2012 // NSubstitute requires the ValueTask-returning call to be used as the receiver of Returns().
        handler.SubscribeAsync().Returns(ValueTask.FromResult(subscription));
#pragma warning restore CA2012
        await using var harness = new ServiceHarness(handlers: [handler]);
        harness.AddGateway(20001);
        await harness.StartAsync();
        await harness.Worker.NextCallAsync();
        await harness.Logger.WaitAsync(6001);

        await harness.StopAsync();

        await harness.Service.ExecuteTask!.WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);
        await subscription.Received(1).DisposeAsync();
        Assert.Contains(6002, harness.Logger.EventIds);
        Assert.DoesNotContain(6003, harness.Logger.EventIds);
    }

    [Fact]
    public async Task StopAsync_RunningService_CancelsWorkerToken()
    {
        await using var harness = new ServiceHarness();
        harness.AddGateway(20001);
        await harness.StartAsync();
        var call = await harness.Worker.NextCallAsync();

        await harness.StopAsync();

        Assert.True(call.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task ExecuteAsync_WorkerFails_LogsCriticalErrorAndFaultsOnceStopped()
    {
        await using var harness = new ServiceHarness();
        harness.AddGateway(20001);
        var failure = new InvalidOperationException();
        harness.Worker.Behavior = _ => Task.FromException(failure);
        await harness.StartAsync();
        await harness.Logger.WaitAsync(6001);

        await harness.StopAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await harness.Service.ExecuteTask!);
        Assert.Same(failure, exception);
        var entry = Assert.Single(harness.Logger.Entries, static entry => entry.EventId is 6003);
        Assert.Equal(LogLevel.Critical, entry.Level);
        Assert.Same(failure, entry.Exception);
        Assert.DoesNotContain(6002, harness.Logger.EventIds);
    }

    [Fact]
    public async Task StartAsync_HandlerSubscriptionFails_LogsCriticalErrorAndDisposesPreviousSubscriptions()
    {
        var subscription = Substitute.For<IAsyncDisposable>();
        var healthy = Substitute.For<IOpenNettyHandler>();
#pragma warning disable CA2012 // NSubstitute requires the ValueTask-returning call to be used as the receiver of Returns().
        healthy.SubscribeAsync().Returns(ValueTask.FromResult(subscription));
        var failure = new InvalidOperationException();
        var failing = Substitute.For<IOpenNettyHandler>();
        failing.SubscribeAsync().Returns(_ => ValueTask.FromException<IAsyncDisposable>(failure));
#pragma warning restore CA2012
        await using var harness = new ServiceHarness(handlers: [healthy, failing]);
        harness.AddGateway(20001);

        await harness.StartAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await harness.Service.ExecuteTask!);
        Assert.Same(failure, exception);
        Assert.Contains(6003, harness.Logger.EventIds);
        Assert.Empty(harness.Worker.Calls);
        await subscription.Received(1).DisposeAsync();
    }

    private static IHostApplicationLifetime CreateLifetime(CancellationTokenSource stopping)
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(stopping.Token);

        return lifetime;
    }

    private static IOpenNettyHandler CreateHandler(string name, List<string> calls)
    {
        var handler = Substitute.For<IOpenNettyHandler>();
#pragma warning disable CA2012 // NSubstitute requires the ValueTask-returning call to be used as the receiver of Returns().
        handler.SubscribeAsync().Returns(_ =>
        {
            calls.Add(name);
            return ValueTask.FromResult(Substitute.For<IAsyncDisposable>());
        });
#pragma warning restore CA2012

        return handler;
    }

    private sealed class UnknownNotification : OpenNettyNotification;

    private sealed class GatewayPortComparer : IComparer<OpenNettyGateway>
    {
        public int Compare(OpenNettyGateway? x, OpenNettyGateway? y)
            => ((IPEndPoint) x!.Endpoint!).Port.CompareTo(((IPEndPoint) y!.Endpoint!).Port);
    }

    private sealed class ServiceHarness : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        private readonly OpenNettyOptions _options = new();

        public ServiceHarness(IEnumerable<IOpenNettyHandler>? handlers = null)
        {
            var lifetime = CreateLifetime(_stopping);
            var monitor = Substitute.For<IOptionsMonitor<OpenNettyOptions>>();
            monitor.CurrentValue.Returns(_options);

            Events = new OpenNettyEvents(lifetime);
            Pipeline = new OpenNettyPipeline(lifetime);
            Service = new OpenNettyHostedService(Events, handlers ?? [], Logger, monitor, Pipeline, Worker);
        }

        public OpenNettyEvents Events { get; }

        public RecordingLogger Logger { get; } = new();

        public OpenNettyPipeline Pipeline { get; }

        public OpenNettyHostedService Service { get; }

        public RecordingWorker Worker { get; } = new();

        public OpenNettyGateway AddGateway(int port)
        {
            var device = OpenNettyLoopbackGateway.CreateDevice(OpenNettyProtocol.Scs);
            var gateway = OpenNettyGateway.Create(device, new IPEndPoint(IPAddress.Loopback, port));
            _options.Gateways.Add(gateway);

            return gateway;
        }

        public Task StartAsync() => Service.StartAsync(TestContext.Current.CancellationToken);

        public Task StopAsync() => Service.StopAsync(TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync(CancellationToken.None);
            Service.Dispose();
            await Pipeline.DisposeAsync();
            await Events.DisposeAsync();
            _stopping.Dispose();
        }
    }

    private sealed class RecordingWorker : IOpenNettyWorker
    {
        private readonly ConcurrentQueue<WorkerCall> _calls = [];
        private readonly Channel<WorkerCall> _started = Channel.CreateUnbounded<WorkerCall>();
        private readonly List<WorkerCall> _pending = [];

        public Func<WorkerCall, Task>? Behavior { get; set; }

        public IReadOnlyCollection<WorkerCall> Calls => [.. _calls];

        public Action<string>? Started { get; set; }

        public async Task<WorkerCall> NextCallAsync(OpenNettyGateway? gateway = null)
        {
            if (gateway is not null)
            {
                var match = _pending.Find(call => call.Gateway == gateway);
                if (match is not null)
                {
                    _pending.Remove(match);
                    return match;
                }
            }

            while (true)
            {
                var call = await _started.Reader.ReadAsync(TestContext.Current.CancellationToken)
                    .AsTask().WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);

                if (gateway is null || call.Gateway == gateway)
                {
                    return call;
                }

                _pending.Add(call);
            }
        }

        public Task ProcessNotificationsAsync(
            OpenNettyGateway gateway, OpenNettyWorkerOptions options, ChannelReader<OpenNettyNotification> reader,
            ChannelWriter<OpenNettyNotification> writer, CancellationToken cancellationToken)
        {
            var call = new WorkerCall(gateway, options, reader, writer, cancellationToken);
            _calls.Enqueue(call);
            Started?.Invoke("worker");
            _started.Writer.TryWrite(call);

            return Behavior?.Invoke(call) ?? Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, TimeProvider.System, cancellationToken);
        }
    }

    private sealed record class WorkerCall(
        OpenNettyGateway Gateway, OpenNettyWorkerOptions Options, ChannelReader<OpenNettyNotification> Reader,
        ChannelWriter<OpenNettyNotification> Writer, CancellationToken CancellationToken);

    private sealed record class LogEntry(LogLevel Level, int EventId, Exception? Exception);

    private sealed class RecordingLogger : ILogger<OpenNettyHostedService>
    {
        private readonly ConcurrentQueue<LogEntry> _entries = [];
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _signals = new();

        public IReadOnlyCollection<LogEntry> Entries => [.. _entries];

        public IReadOnlyList<int> EventIds => [.. _entries.Select(static entry => entry.EventId)];

        public Task WaitAsync(int eventId) => GetSignal(eventId).Task
            .WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _entries.Enqueue(new LogEntry(logLevel, eventId.Id, exception));
            GetSignal(eventId.Id).TrySetResult();
        }

        private TaskCompletionSource GetSignal(int eventId)
            => _signals.GetOrAdd(eventId, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    private sealed class RecordingObserver<T> : IAsyncObserver<T>
    {
        private readonly Channel<T> _items = Channel.CreateUnbounded<T>();

        public async Task<T> NextAsync() => await _items.Reader.ReadAsync(TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);

        public bool TryNext(out T? item) => _items.Reader.TryRead(out item);

        public ValueTask OnNextAsync(T value)
        {
            _items.Writer.TryWrite(value);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnErrorAsync(Exception error) => ValueTask.CompletedTask;

        public ValueTask OnCompletedAsync() => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
