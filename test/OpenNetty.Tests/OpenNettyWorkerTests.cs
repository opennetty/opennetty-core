/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyWorkerTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(10);

    private const string Ack = "*#*1##";
    private const string Nack = "*#*0##";
    private const string BusyNack = "*#*6##";
    private const string ScsFrame = "*1*1*21##";
    private const string NitooFrame = "*1*1*1234##";
    private const string ZigbeeFrame = "*1*1*1234501#9##";

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new OpenNettyWorker(null!));

        Assert.Equal("logger", exception.ParamName);
    }

    [Theory]
    [InlineData("gateway")]
    [InlineData("reader")]
    [InlineData("writer")]
    public void ProcessNotificationsAsync_NullArgument_ThrowsArgumentNullException(string parameter)
    {
        var worker = new OpenNettyWorker(new RecordingLogger());
        var device = OpenNettyLoopbackGateway.CreateDevice(OpenNettyProtocol.Scs);
        var gateway = parameter is "gateway" ? null : OpenNettyGateway.Create(device, new IPEndPoint(IPAddress.Loopback, 20000));
        var channel = Channel.CreateUnbounded<OpenNettyNotification>();

        var exception = Assert.Throws<ArgumentNullException>(() =>
        {
            _ = worker.ProcessNotificationsAsync(
                gateway!, CreateWorkerOptions(), parameter is "reader" ? null! : channel.Reader,
                parameter is "writer" ? null! : channel.Writer, TestContext.Current.CancellationToken);
        });

        Assert.Equal(parameter, exception.ParamName);
    }

    [Fact]
    public async Task ProcessNotificationsAsync_GatewayWithoutSessionCapability_CompletesImmediately()
    {
        var logger = new RecordingLogger();
        var worker = new OpenNettyWorker(logger);
        var gateway = OpenNettyGateway.Create(
            OpenNettyLoopbackGateway.CreateDevice(OpenNettyProtocol.Scs), new IPEndPoint(IPAddress.Loopback, 20000));
        var channel = Channel.CreateUnbounded<OpenNettyNotification>();

        await worker.ProcessNotificationsAsync(
            gateway, CreateWorkerOptions(), channel.Reader, channel.Writer, TestContext.Current.CancellationToken);

        Assert.Equal([6004, 6005], logger.EventIds);
    }

    [Fact]
    public async Task ProcessNotificationsAsync_NoCommandSessionAllowed_CompletesImmediately()
    {
        var logger = new RecordingLogger();
        var worker = new OpenNettyWorker(logger);
        var gateway = OpenNettyGateway.Create(
            OpenNettyLoopbackGateway.CreateDevice(OpenNettyProtocol.Scs, OpenNettyCapabilities.OpenWebNetCommandSession),
            new IPEndPoint(IPAddress.Loopback, 20000));
        var channel = Channel.CreateUnbounded<OpenNettyNotification>();

        await worker.ProcessNotificationsAsync(
            gateway, CreateWorkerOptions(sessions: 0), channel.Reader, channel.Writer, TestContext.Current.CancellationToken);

        Assert.Equal([6004, 6005], logger.EventIds);
    }

    [Fact]
    public async Task ProcessNotificationsAsync_GatewaySupportingAllSessionTypes_StartsOneWorkerPerSession()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var logger = new RecordingLogger();
        var worker = new OpenNettyWorker(logger);
        await using var server = new OpenNettyLoopbackGateway();
        var gateway = server.CreateGateway(
            OpenNettyLoopbackGateway.CreateDevice(
                OpenNettyProtocol.Scs,
                OpenNettyCapabilities.OpenWebNetCommandSession,
                OpenNettyCapabilities.OpenWebNetEventSession,
                OpenNettyCapabilities.OpenWebNetGenericSession),
            configure: WithoutRetries);
        var channel = Channel.CreateUnbounded<OpenNettyNotification>();

        var processing = worker.ProcessNotificationsAsync(
            gateway, CreateWorkerOptions(sessions: 3), channel.Reader, channel.Writer, source.Token);
        await source.CancelAsync();

        Assert.Equal(5, logger.EventIds.Count(static identifier => identifier is 6006));
        await Record.ExceptionAsync(async () => await processing);
    }

    [Fact]
    public async Task Generic_SessionEstablished_PublishesSessionOpen()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);

        await harness.AcceptAsync();
        var notification = await harness.NextAsync<OpenNettyNotifications.SessionOpen>();

        Assert.Equal(OpenNettySessionType.Generic, notification.Session.Type);
        Assert.Same(harness.Gateway, notification.Gateway);
        Assert.Equal([6004, 6006, 6005, 6007], harness.Logger.EventIds.Take(4));
    }

    [Fact]
    public async Task Generic_IncomingMessage_PublishesMessageReceived()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        var peer = await harness.AcceptAsync();
        var opened = await harness.NextAsync<OpenNettyNotifications.SessionOpen>();

        await peer.SendAsync(NitooFrame);

        var received = await harness.NextAsync<OpenNettyNotifications.MessageReceived>();
        Assert.Equal(OpenNettyFrame.Parse(NitooFrame), received.Message.Frame);
        Assert.Same(opened.Session, received.Session);
        Assert.Same(harness.Gateway, received.Gateway);
        Assert.Contains(6009, harness.Logger.EventIds);
    }

    [Fact]
    public async Task Generic_MessageReady_SendsMessageAndPublishesMessageSent()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        var peer = await harness.AcceptAsync();
        var opened = await harness.NextAsync<OpenNettyNotifications.SessionOpen>();
        var ready = harness.CreateMessageReady(NitooFrame, options => options with { DisableActionValidation = true });

        await harness.PublishAsync(ready);
        await peer.ExpectAsync(NitooFrame);
        await peer.SendAsync(Ack);

        var sent = await harness.NextAsync<OpenNettyNotifications.MessageSent>();
        Assert.Equal(ready.Transaction, sent.Transaction);
        Assert.Equal(ready.Message, sent.Message);
        Assert.Same(opened.Session, sent.Session);
        Assert.Contains(6024, harness.Logger.EventIds);
        Assert.Contains(6010, harness.Logger.EventIds);
    }

    [Fact]
    public async Task Generic_NonMessageReadyNotification_IsIgnored()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        var peer = await harness.AcceptAsync();
        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();
        var ready = harness.CreateMessageReady(NitooFrame, options => options with { DisableActionValidation = true });

        await harness.PublishAsync(new UnknownNotification { Gateway = harness.Gateway });
        await harness.PublishAsync(ready);
        await peer.ExpectAsync(NitooFrame);
        await peer.SendAsync(Ack);

        var sent = await harness.NextAsync<OpenNettyNotifications.MessageSent>();
        Assert.Equal(ready.Transaction, sent.Transaction);
    }

    [Fact]
    public async Task Generic_MessageRejectedByGateway_PublishesMessageError()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        var peer = await harness.AcceptAsync();
        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();
        var ready = harness.CreateMessageReady(NitooFrame);

        await harness.PublishAsync(ready);
        await peer.ExpectAsync(NitooFrame);
        await peer.SendAsync(Nack);

        var error = await harness.NextAsync<OpenNettyNotifications.MessageError>();
        Assert.Equal(OpenNettyErrorCode.InvalidFrame, error.Exception.ErrorCode);
        Assert.Equal(ready.Transaction, error.Transaction);
        Assert.Equal(ready.Message, error.Message);
        Assert.Contains(6015, harness.Logger.EventIds);
        Assert.DoesNotContain(6010, harness.Logger.EventIds);
    }

    [Fact]
    public async Task Generic_InvalidAction_PublishesMessageError()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        var peer = await harness.AcceptAsync();
        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();

        await harness.PublishAsync(harness.CreateMessageReady(NitooFrame));
        await peer.ExpectAsync(NitooFrame);
        await peer.SendAsync(Ack);
        await peer.SendAsync("*1000*73*1234##");

        var error = await harness.NextAsync<OpenNettyNotifications.MessageError>();
        Assert.Equal(OpenNettyErrorCode.InvalidAction, error.Exception.ErrorCode);
        Assert.Contains(6012, harness.Logger.EventIds);
    }

    [Fact]
    public async Task Generic_NoActionReceived_PublishesMessageError()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        var peer = await harness.AcceptAsync();
        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();

        await harness.PublishAsync(harness.CreateMessageReady(NitooFrame, options => options with { ActionValidationTimeout = ShortTimeout }));
        await peer.ExpectAsync(NitooFrame);
        await peer.SendAsync(Ack);

        var error = await harness.NextAsync<OpenNettyNotifications.MessageError>();
        Assert.Equal(OpenNettyErrorCode.NoActionReceived, error.Exception.ErrorCode);
        Assert.Contains(6013, harness.Logger.EventIds);
    }

    [Fact]
    public async Task Generic_GatewayBusy_PublishesMessageError()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Zigbee, OpenNettySessionType.Generic);
        var peer = await harness.AcceptAsync();
        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();

        await harness.PublishAsync(harness.CreateMessageReady(ZigbeeFrame));
        await peer.ExpectAsync(ZigbeeFrame);
        await peer.SendAsync(BusyNack);
        await peer.SendAsync(Nack);

        var error = await harness.NextAsync<OpenNettyNotifications.MessageError>();
        Assert.Equal(OpenNettyErrorCode.GatewayBusy, error.Exception.ErrorCode);
        Assert.Contains(6011, harness.Logger.EventIds);
    }

    [Fact]
    public async Task Generic_NoStatusReportReceived_PublishesMessageError()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Zigbee, OpenNettySessionType.Generic);
        var peer = await harness.AcceptAsync();
        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();

        await harness.PublishAsync(harness.CreateMessageReady(ZigbeeFrame, options => options with { StatusReportTimeout = ShortTimeout }));
        await peer.ExpectAsync(ZigbeeFrame);
        await peer.SendAsync(Ack);

        var error = await harness.NextAsync<OpenNettyNotifications.MessageError>();
        Assert.Equal(OpenNettyErrorCode.NoStatusReportReceived, error.Exception.ErrorCode);
        Assert.Contains(6025, harness.Logger.EventIds);
    }

    [Fact]
    public async Task Generic_CustomValidationFailed_PublishesMessageError()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        var peer = await harness.AcceptAsync();
        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();

        await harness.PublishAsync(harness.CreateMessageReady(NitooFrame, options => options with
        {
            DisableActionValidation = true,
            CustomValidationDelegate = static (_, _) => ValueTask.FromResult(false)
        }));
        await peer.ExpectAsync(NitooFrame);
        await peer.SendAsync(Ack);

        var error = await harness.NextAsync<OpenNettyNotifications.MessageError>();
        Assert.Equal(OpenNettyErrorCode.CustomValidationFailed, error.Exception.ErrorCode);
    }

    [Fact]
    public async Task Generic_NoAcknowledgementAndSessionKeptAlive_PublishesMessageErrorAndReusesSession()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        var peer = await harness.AcceptAsync();
        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();

        await harness.PublishAsync(harness.CreateMessageReady(NitooFrame, options => options with
        {
            AcknowledgementTimeout = ShortTimeout,
            KeepSessionAliveOnMissingAcknowledgement = true
        }));
        await peer.ExpectAsync(NitooFrame);
        var error = await harness.NextAsync<OpenNettyNotifications.MessageError>();
        var next = harness.CreateMessageReady(NitooFrame, options => options with { DisableActionValidation = true });
        await harness.PublishAsync(next);
        await peer.ExpectAsync(NitooFrame);
        await peer.SendAsync(Ack);

        var sent = await harness.NextAsync<OpenNettyNotifications.MessageSent>();
        Assert.Equal(OpenNettyErrorCode.NoAcknowledgementReceived, error.Exception.ErrorCode);
        Assert.Equal(next.Transaction, sent.Transaction);
        Assert.Contains(6014, harness.Logger.EventIds);
    }

    [Fact]
    public async Task Generic_NoAcknowledgementAndSessionNotKeptAlive_FaultsSessionWorker()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        var peer = await harness.AcceptAsync();
        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();

        await harness.PublishAsync(harness.CreateMessageReady(NitooFrame, options => options with { AcknowledgementTimeout = ShortTimeout }));
        await peer.ExpectAsync(NitooFrame);

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await harness.Worker);
        Assert.Equal(OpenNettyErrorCode.NoAcknowledgementReceived, exception.ErrorCode);
        Assert.Contains(6014, harness.Logger.EventIds);
        await peer.ExpectClosedAsync();
    }

    [Fact]
    public async Task Generic_GatewayClosesConnection_PublishesSessionClosed()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        var peer = await harness.AcceptAsync();
        var opened = await harness.NextAsync<OpenNettyNotifications.SessionOpen>();

        peer.Disconnect();

        var closed = await harness.NextAsync<OpenNettyNotifications.SessionClosed>();
        Assert.Same(opened.Session, closed.Session);
        Assert.Contains(6008, harness.Logger.EventIds);
        await harness.Worker.WaitAsync(LongTimeout, TimeProvider.System, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Generic_WorkerCanceled_ClosesSessionAndStops()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        var peer = await harness.AcceptAsync();
        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();

        await harness.StopAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await harness.Worker);
        await peer.ExpectClosedAsync();
    }

    [Fact]
    public async Task Generic_SessionCreationFails_UsesResiliencePipelineToCreateNewSession()
    {
        var retry = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions
        {
            Delay = TimeSpan.Zero,
            MaxRetryAttempts = 1,
            ShouldHandle = static arguments => ValueTask.FromResult(arguments.Outcome.Exception is OpenNettyException)
        }).Build();
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic, configure: options => options with
        {
            DefaultSessionOptions = options.DefaultSessionOptions with { SessionResiliencePipeline = retry }
        });

        var first = await harness.Server.AcceptAsync();
        await first.ExpectAsync("*#13**16##");
        first.Disconnect();
        await harness.AcceptAsync();

        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();
    }

    [Fact]
    public async Task Generic_SessionResilienceContext_ExposesGatewayLoggerAndSessionType()
    {
        OpenNettyGateway? gateway = null;
        ILogger<OpenNettyWorker>? logger = null;
        var type = null as OpenNettySessionType?;
        var inspection = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = arguments =>
            {
                arguments.Context.Properties.TryGetValue(new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)), out gateway);
                arguments.Context.Properties.TryGetValue(new ResiliencePropertyKey<ILogger<OpenNettyWorker>>(nameof(ILogger<>)), out logger);
                arguments.Context.Properties.TryGetValue(new ResiliencePropertyKey<OpenNettySessionType>(nameof(OpenNettySessionType)), out OpenNettySessionType value);
                type = value;
                return ValueTask.FromResult(false);
            }
        }).Build();
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Scs, OpenNettySessionType.Event, configure: options => options with
        {
            DefaultSessionOptions = options.DefaultSessionOptions with { SessionResiliencePipeline = inspection }
        });
        var peer = await harness.AcceptAsync();
        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();

        peer.Disconnect();
        await harness.Worker.WaitAsync(LongTimeout, TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.Same(harness.Gateway, gateway);
        Assert.Same(harness.Logger, logger);
        Assert.Equal(OpenNettySessionType.Event, type);
    }

    [Fact]
    public async Task Event_IncomingMessage_PublishesMessageReceived()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Scs, OpenNettySessionType.Event);
        var peer = await harness.AcceptAsync();
        var opened = await harness.NextAsync<OpenNettyNotifications.SessionOpen>();

        await peer.SendAsync(ScsFrame);

        var received = await harness.NextAsync<OpenNettyNotifications.MessageReceived>();
        Assert.Equal(OpenNettySessionType.Event, opened.Session.Type);
        Assert.Equal(OpenNettyFrame.Parse(ScsFrame), received.Message.Frame);
        Assert.Same(opened.Session, received.Session);
    }

    [Fact]
    public async Task Event_MessageReady_IsNotConsumed()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Scs, OpenNettySessionType.Event);
        await harness.AcceptAsync();
        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();
        var ready = harness.CreateMessageReady(ScsFrame);

        await harness.PublishAsync(ready);

        Assert.True(harness.Input.Reader.TryRead(out var notification));
        Assert.Same(ready, notification);
    }

    [Fact]
    public async Task Event_GatewayClosesConnection_PublishesSessionClosed()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Scs, OpenNettySessionType.Event);
        var peer = await harness.AcceptAsync();
        var opened = await harness.NextAsync<OpenNettyNotifications.SessionOpen>();

        peer.Disconnect();

        var closed = await harness.NextAsync<OpenNettyNotifications.SessionClosed>();
        Assert.Same(opened.Session, closed.Session);
        await harness.Worker.WaitAsync(LongTimeout, TimeProvider.System, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Event_WorkerCanceled_ClosesSessionAndStops()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Scs, OpenNettySessionType.Event);
        var peer = await harness.AcceptAsync();
        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();

        await harness.StopAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await harness.Worker);
        await peer.ExpectClosedAsync();
    }

    [Fact]
    public async Task Command_MessageReady_OpensSessionSendsMessageAndClosesSession()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        var ready = harness.CreateMessageReady(ScsFrame);
        await harness.PublishAsync(ready);

        var peer = await harness.AcceptAsync();
        await peer.ExpectAsync(ScsFrame);
        await peer.SendAsync(Ack);

        var opened = await harness.NextAsync<OpenNettyNotifications.SessionOpen>();
        var sent = await harness.NextAsync<OpenNettyNotifications.MessageSent>();
        var closed = await harness.NextAsync<OpenNettyNotifications.SessionClosed>();
        Assert.Equal(OpenNettySessionType.Command, opened.Session.Type);
        Assert.Equal(ready.Transaction, sent.Transaction);
        Assert.Same(opened.Session, sent.Session);
        Assert.Same(opened.Session, closed.Session);
        await peer.ExpectClosedAsync();
    }

    [Fact]
    public async Task Command_NoMessageReady_DoesNotOpenSession()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Scs, OpenNettySessionType.Command);

        await harness.PublishAsync(new UnknownNotification { Gateway = harness.Gateway });
        await harness.PublishAsync(harness.CreateMessageReady(ScsFrame));

        var peer = await harness.AcceptAsync();
        await peer.ExpectAsync(ScsFrame);
        await peer.SendAsync(Ack);
        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();
        await harness.NextAsync<OpenNettyNotifications.MessageSent>();
    }

    [Fact]
    public async Task Command_MessageRejectedByGateway_PublishesMessageErrorThenSessionClosed()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        var ready = harness.CreateMessageReady(ScsFrame);
        await harness.PublishAsync(ready);

        var peer = await harness.AcceptAsync();
        await peer.ExpectAsync(ScsFrame);
        await peer.SendAsync(Nack);

        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();
        var error = await harness.NextAsync<OpenNettyNotifications.MessageError>();
        await harness.NextAsync<OpenNettyNotifications.SessionClosed>();
        Assert.Equal(OpenNettyErrorCode.InvalidFrame, error.Exception.ErrorCode);
        Assert.Equal(ready.Transaction, error.Transaction);
    }

    [Fact]
    public async Task Command_IncomingMessageDuringSession_PublishesMessageReceived()
    {
        await using var harness = WorkerHarness.Start(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.PublishAsync(harness.CreateMessageReady(ScsFrame));
        var peer = await harness.AcceptAsync();
        await peer.ExpectAsync(ScsFrame);
        await peer.SendAsync("*1*0*22##");
        await peer.SendAsync(Ack);

        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();
        var received = await harness.NextAsync<OpenNettyNotifications.MessageReceived>();
        await harness.NextAsync<OpenNettyNotifications.MessageSent>();

        Assert.Equal(OpenNettyFrame.Parse("*1*0*22##"), received.Message.Frame);
    }

    [Fact]
    public async Task Command_AnotherMessageReadyWithinLifetime_ReusesSession()
    {
        await using var harness = WorkerHarness.Start(
            OpenNettyProtocol.Scs, OpenNettySessionType.Command, lifetime: TimeSpan.FromSeconds(2));
        var first = harness.CreateMessageReady(ScsFrame);
        await harness.PublishAsync(first);
        var peer = await harness.AcceptAsync();
        await peer.ExpectAsync(ScsFrame);
        await peer.SendAsync(Ack);
        var opened = await harness.NextAsync<OpenNettyNotifications.SessionOpen>();
        await harness.NextAsync<OpenNettyNotifications.MessageSent>();

        var second = harness.CreateMessageReady("*1*0*21##");
        await harness.PublishAsync(second);
        await peer.ExpectAsync("*1*0*21##");
        await peer.SendAsync(Ack);

        var sent = await harness.NextAsync<OpenNettyNotifications.MessageSent>();
        Assert.Equal(second.Transaction, sent.Transaction);
        Assert.Same(opened.Session, sent.Session);
    }

    [Fact]
    public async Task Command_WorkerCanceledDuringSession_ClosesSessionAndStops()
    {
        await using var harness = WorkerHarness.Start(
            OpenNettyProtocol.Scs, OpenNettySessionType.Command, lifetime: TimeSpan.FromSeconds(2));
        await harness.PublishAsync(harness.CreateMessageReady(ScsFrame));
        var peer = await harness.AcceptAsync();
        await peer.ExpectAsync(ScsFrame);
        await peer.SendAsync(Ack);
        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();
        await harness.NextAsync<OpenNettyNotifications.MessageSent>();

        await harness.StopAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await harness.Worker);
        await peer.ExpectClosedAsync();
    }

    [Fact]
    public async Task Command_GatewayClosesConnectionDuringSession_ClosesSessionPromptly()
    {
        await using var harness = WorkerHarness.Start(
            OpenNettyProtocol.Scs, OpenNettySessionType.Command, lifetime: TimeSpan.FromMinutes(1));
        await harness.PublishAsync(harness.CreateMessageReady(ScsFrame));
        var peer = await harness.AcceptAsync();
        await peer.ExpectAsync(ScsFrame);
        await peer.SendAsync(Ack);
        await harness.NextAsync<OpenNettyNotifications.SessionOpen>();
        await harness.NextAsync<OpenNettyNotifications.MessageSent>();

        peer.Disconnect();

        await harness.NextAsync<OpenNettyNotifications.SessionClosed>();
    }

    private static OpenNettyWorkerOptions CreateWorkerOptions(TimeSpan? lifetime = null, byte sessions = 1) => new()
    {
        CommandSessionMaximumLifetime = lifetime ?? TimeSpan.Zero,
        MaximumConcurrentCommandSessions = sessions
    };

    private static OpenNettyGatewayOptions WithoutRetries(OpenNettyGatewayOptions options) => options with
    {
        DefaultSessionOptions = options.DefaultSessionOptions with { SessionResiliencePipeline = ResiliencePipeline.Empty }
    };

    private sealed class UnknownNotification : OpenNettyNotification;

    private sealed class RecordingLogger : ILogger<OpenNettyWorker>
    {
        private readonly ConcurrentQueue<int> _eventIds = [];

        public IReadOnlyList<int> EventIds => [.. _eventIds];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _eventIds.Enqueue(eventId.Id);
    }

    private sealed class WorkerHarness : IAsyncDisposable
    {
        private readonly CancellationTokenSource _source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        private readonly Channel<OpenNettyNotification> _output = Channel.CreateUnbounded<OpenNettyNotification>();
        private readonly OpenNettySessionType _type;

        private WorkerHarness(OpenNettySessionType type, OpenNettyLoopbackGateway server, OpenNettyGateway gateway, TimeSpan lifetime)
        {
            _type = type;
            Server = server;
            Gateway = gateway;
            Worker = new OpenNettyWorker(Logger).ProcessNotificationsAsync(
                gateway, CreateWorkerOptions(lifetime), Input.Reader, _output.Writer, _source.Token);
        }

        public Channel<OpenNettyNotification> Input { get; } = Channel.CreateUnbounded<OpenNettyNotification>();

        public RecordingLogger Logger { get; } = new();

        public OpenNettyLoopbackGateway Server { get; }

        public OpenNettyGateway Gateway { get; }

        public Task Worker { get; }

        public static WorkerHarness Start(
            OpenNettyProtocol protocol, OpenNettySessionType type,
            Func<OpenNettyGatewayOptions, OpenNettyGatewayOptions>? configure = null, TimeSpan? lifetime = null)
        {
            var server = new OpenNettyLoopbackGateway();
            var device = OpenNettyLoopbackGateway.CreateDevice(protocol, type switch
            {
                OpenNettySessionType.Command => OpenNettyCapabilities.OpenWebNetCommandSession,
                OpenNettySessionType.Event   => OpenNettyCapabilities.OpenWebNetEventSession,
                _                            => OpenNettyCapabilities.OpenWebNetGenericSession
            });

            return new WorkerHarness(type, server, server.CreateGateway(device, configure: configure ?? WithoutRetries), lifetime ?? TimeSpan.Zero);
        }

        public async Task<OpenNettyLoopbackGateway.Peer> AcceptAsync()
        {
            var peer = await Server.AcceptAsync();
            await peer.NegotiateSessionAsync(Gateway.Protocol, _type);

            return peer;
        }

        public async Task<TNotification> NextAsync<TNotification>() where TNotification : OpenNettyNotification
        {
            while (true)
            {
                var notification = await _output.Reader.ReadAsync(TestContext.Current.CancellationToken)
                    .AsTask().WaitAsync(LongTimeout, TimeProvider.System, TestContext.Current.CancellationToken);

                // Every frame returned by the gateway (including ACK/NACK frames) is also published as a MessageReceived notification.
                if (typeof(TNotification) != typeof(OpenNettyNotifications.MessageReceived) &&
                    notification is OpenNettyNotifications.MessageReceived)
                {
                    continue;
                }

                return Assert.IsType<TNotification>(notification);
            }
        }

        public async Task PublishAsync(OpenNettyNotification notification)
            => await Input.Writer.WriteAsync(notification, TestContext.Current.CancellationToken);

        public OpenNettyNotifications.MessageReady CreateMessageReady(
            string frame, Func<OpenNettyTransmissionOptions, OpenNettyTransmissionOptions>? configure = null)
        {
            var options = Gateway.Options.DefaultTransmissionOptions with
            {
                AcknowledgementTimeout = LongTimeout,
                ActionValidationTimeout = LongTimeout,
                PostSendingDelay = TimeSpan.Zero,
                StatusReportTimeout = LongTimeout
            };

            return new OpenNettyNotifications.MessageReady
            {
                Gateway = Gateway,
                Message = OpenNettyMessage.CreateFromFrame(Gateway.Protocol, frame),
                Options = configure?.Invoke(options) ?? options,
                Transaction = OpenNettyTransaction.Create()
            };
        }

        public Task StopAsync() => _source.CancelAsync();

        public async ValueTask DisposeAsync()
        {
            await _source.CancelAsync();
            await Record.ExceptionAsync(async () => await Worker.WaitAsync(LongTimeout, TimeProvider.System));
            await Server.DisposeAsync();
            _source.Dispose();
        }
    }
}
