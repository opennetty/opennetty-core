/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyEventsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly OpenNettyEndpoint Endpoint = new()
    {
        Name = "Endpoint",
        Protocol = OpenNettyProtocol.Scs
    };

    private static readonly OpenNettyMessage Message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1*21##");

    private static readonly Dictionary<string, Func<OpenNettyEvents, Task>> Cases = CreateCases();

    [Fact]
    public async Task ApplicationStopping_Triggered_CompletesObserversAfterPendingEvents()
    {
        using var stopping = new CancellationTokenSource();
        var events = new OpenNettyEvents(CreateLifetime(stopping));
        var observer = new RecordingObserver<OpenNettyEvents.ToggleScenarioReportedEventArgs>();
        await using var subscription = await events.ToggleScenarioReported.SubscribeAsync(observer);
        var arguments = new OpenNettyEvents.ToggleScenarioReportedEventArgs(Endpoint);
        await events.PublishAsync(arguments, TestContext.Current.CancellationToken);

        await stopping.CancelAsync();
        await using var connection = await events.ConnectAsync();

        Assert.Same(arguments, await observer.NextAsync());
        await observer.Completed.WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void ObservableProperties_AreAllCoveredByTheRoutingTests()
    {
        var properties = typeof(OpenNettyEvents)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.PropertyType.IsGenericType &&
                property.PropertyType.GetGenericTypeDefinition() == typeof(IAsyncObservable<>))
            .Select(static property => property.Name);

        Assert.Equal(properties.Order(StringComparer.Ordinal), Cases.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task DisposeAsync_ApplicationStopping_NoLongerClosesEvents()
    {
        using var stopping = new CancellationTokenSource();
        var events = new OpenNettyEvents(CreateLifetime(stopping));
        await events.DisposeAsync();

        await stopping.CancelAsync();

        var exception = await Record.ExceptionAsync(async () => await events.PublishAsync(
            new OpenNettyEvents.ToggleScenarioReportedEventArgs(Endpoint), TestContext.Current.CancellationToken));
        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        using var stopping = new CancellationTokenSource();
        var events = new OpenNettyEvents(CreateLifetime(stopping));

        await events.DisposeAsync();
        var exception = await Record.ExceptionAsync(async () => await events.DisposeAsync());

        Assert.Null(exception);
    }

    public static TheoryData<string> EventNames => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(EventNames))]
    public async Task PublishAsync_EventArguments_AreRoutedToMatchingObservableOnly(string name)
    {
        using var stopping = new CancellationTokenSource();
        var events = new OpenNettyEvents(CreateLifetime(stopping));

        await Cases[name](events);
    }

    [Fact]
    public async Task PublishAsync_ConnectedEvents_DeliversEventsInPublicationOrder()
    {
        using var stopping = new CancellationTokenSource();
        var events = new OpenNettyEvents(CreateLifetime(stopping));
        var observer = new RecordingObserver<OpenNettyEvents.BatteryLevelReportedEventArgs>();
        await using var subscription = await events.BatteryLevelReported.SubscribeAsync(observer);
        await using var connection = await events.ConnectAsync();

        for (byte level = 0; level < 50; level++)
        {
            await events.PublishAsync(new OpenNettyEvents.BatteryLevelReportedEventArgs(Endpoint, level), TestContext.Current.CancellationToken);
        }

        for (byte level = 0; level < 50; level++)
        {
            Assert.Equal(level, (await observer.NextAsync()).Level);
        }
    }

    [Fact]
    public async Task PublishAsync_BeforeConnect_DeliversEventsOnceConnected()
    {
        using var stopping = new CancellationTokenSource();
        var events = new OpenNettyEvents(CreateLifetime(stopping));
        var observer = new RecordingObserver<OpenNettyEvents.BatteryAlertReportedEventArgs>();
        await using var subscription = await events.BatteryAlertReported.SubscribeAsync(observer);
        var arguments = new OpenNettyEvents.BatteryAlertReportedEventArgs(Endpoint);

        await events.PublishAsync(arguments, TestContext.Current.CancellationToken);

        Assert.False(observer.TryNext(out _));
        await using var connection = await events.ConnectAsync();
        Assert.Same(arguments, await observer.NextAsync());
    }

    [Fact]
    public async Task PublishAsync_MultipleSubscribers_EachSubscriberReceivesEvent()
    {
        using var stopping = new CancellationTokenSource();
        var events = new OpenNettyEvents(CreateLifetime(stopping));
        var first = new RecordingObserver<OpenNettyEvents.ToggleScenarioReportedEventArgs>();
        var second = new RecordingObserver<OpenNettyEvents.ToggleScenarioReportedEventArgs>();
        await using var firstSubscription = await events.ToggleScenarioReported.SubscribeAsync(first);
        await using var secondSubscription = await events.ToggleScenarioReported.SubscribeAsync(second);
        await using var connection = await events.ConnectAsync();
        var arguments = new OpenNettyEvents.ToggleScenarioReportedEventArgs(Endpoint);

        await events.PublishAsync(arguments, TestContext.Current.CancellationToken);

        Assert.Same(arguments, await first.NextAsync());
        Assert.Same(arguments, await second.NextAsync());
    }

    [Fact]
    public async Task PublishAsync_ApplicationStopping_ThrowsChannelClosedException()
    {
        using var stopping = new CancellationTokenSource();
        var events = new OpenNettyEvents(CreateLifetime(stopping));
        await stopping.CancelAsync();

        await Assert.ThrowsAsync<ChannelClosedException>(async () => await events.PublishAsync(
            new OpenNettyEvents.ToggleScenarioReportedEventArgs(Endpoint), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        using var stopping = new CancellationTokenSource();
        var events = new OpenNettyEvents(CreateLifetime(stopping));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await events.PublishAsync(
            new OpenNettyEvents.ToggleScenarioReportedEventArgs(Endpoint), source.Token));
    }

    [Fact]
    public void EventArguments_ExposeEndpoint()
    {
        OpenNettyEvents.EventArgs arguments = new OpenNettyEvents.ShutterPositionReportedEventArgs(Endpoint, 42);

        Assert.Same(Endpoint, arguments.Endpoint);
    }

    private static IHostApplicationLifetime CreateLifetime(CancellationTokenSource stopping)
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(stopping.Token);

        return lifetime;
    }

    private static Dictionary<string, Func<OpenNettyEvents, Task>> CreateCases() => new[]
    {
        Case(nameof(OpenNettyEvents.ActionScenarioReported), static events => events.ActionScenarioReported,
            new OpenNettyEvents.ActionScenarioReportedEventArgs(Endpoint, default)),

        Case(nameof(OpenNettyEvents.AvailabilityReported), static events => events.AvailabilityReported,
            new OpenNettyEvents.AvailabilityReportedEventArgs(Endpoint, default)),

        Case(nameof(OpenNettyEvents.BatteryAlertReported), static events => events.BatteryAlertReported,
            new OpenNettyEvents.BatteryAlertReportedEventArgs(Endpoint)),

        Case(nameof(OpenNettyEvents.BatteryLevelReported), static events => events.BatteryLevelReported,
            new OpenNettyEvents.BatteryLevelReportedEventArgs(Endpoint, 80)),

        Case(nameof(OpenNettyEvents.BrightnessReported), static events => events.BrightnessReported,
            new OpenNettyEvents.BrightnessReportedEventArgs(Endpoint, 60)),

        Case(nameof(OpenNettyEvents.DeviceCommunicationReported), static events => events.DeviceCommunicationReported,
            new OpenNettyEvents.DeviceCommunicationReportedEventArgs(Endpoint, Message)),

        Case(nameof(OpenNettyEvents.DimmingScenarioReported), static events => events.DimmingScenarioReported,
            new OpenNettyEvents.DimmingScenarioReportedEventArgs(Endpoint, -3)),

        Case(nameof(OpenNettyEvents.DryContactScenarioReported), static events => events.DryContactScenarioReported,
            new OpenNettyEvents.DryContactScenarioReportedEventArgs(Endpoint, default)),

        Case(nameof(OpenNettyEvents.DryContactStateReported), static events => events.DryContactStateReported,
            new OpenNettyEvents.DryContactStateReportedEventArgs(Endpoint, default, default)),

        Case(nameof(OpenNettyEvents.IncomingMessageReported), static events => events.IncomingMessageReported,
            new OpenNettyEvents.IncomingMessageReportedEventArgs(Endpoint, Message, null!)),

        Case(nameof(OpenNettyEvents.OnOffScenarioReported), static events => events.OnOffScenarioReported,
            new OpenNettyEvents.OnOffScenarioReportedEventArgs(Endpoint, default)),

        Case(nameof(OpenNettyEvents.OutgoingMessageReported), static events => events.OutgoingMessageReported,
            new OpenNettyEvents.OutgoingMessageReportedEventArgs(Endpoint, Message, null!)),

        Case(nameof(OpenNettyEvents.PilotWireDerogationModeReported), static events => events.PilotWireDerogationModeReported,
            new OpenNettyEvents.PilotWireDerogationModeReportedEventArgs(Endpoint, null, null)),

        Case(nameof(OpenNettyEvents.PilotWireSetpointModeReported), static events => events.PilotWireSetpointModeReported,
            new OpenNettyEvents.PilotWireSetpointModeReportedEventArgs(Endpoint, default)),

        Case(nameof(OpenNettyEvents.PilotWireShutdownModeReported), static events => events.PilotWireShutdownModeReported,
            new OpenNettyEvents.PilotWireShutdownModeReportedEventArgs(Endpoint, true)),

        Case(nameof(OpenNettyEvents.PressureScenarioReported), static events => events.PressureScenarioReported,
            new OpenNettyEvents.PressureScenarioReportedEventArgs(Endpoint, default, 2)),

        Case(nameof(OpenNettyEvents.PressureScenarioPlusReported), static events => events.PressureScenarioPlusReported,
            new OpenNettyEvents.PressureScenarioPlusReportedEventArgs(Endpoint, default, null)),

        Case(nameof(OpenNettyEvents.ProgressiveScenarioReported), static events => events.ProgressiveScenarioReported,
            new OpenNettyEvents.ProgressiveScenarioReportedEventArgs(Endpoint, TimeSpan.FromSeconds(5))),

        Case(nameof(OpenNettyEvents.ShutterPositionReported), static events => events.ShutterPositionReported,
            new OpenNettyEvents.ShutterPositionReportedEventArgs(Endpoint, 42)),

        Case(nameof(OpenNettyEvents.ShutterStateReported), static events => events.ShutterStateReported,
            new OpenNettyEvents.ShutterStateReportedEventArgs(Endpoint, default)),

        Case(nameof(OpenNettyEvents.SmartMeterPowerCutModeReported), static events => events.SmartMeterPowerCutModeReported,
            new OpenNettyEvents.SmartMeterPowerCutModeReportedEventArgs(Endpoint, true)),

        Case(nameof(OpenNettyEvents.SmartMeterRateTypeReported), static events => events.SmartMeterRateTypeReported,
            new OpenNettyEvents.SmartMeterRateTypeReportedEventArgs(Endpoint, default)),

        Case(nameof(OpenNettyEvents.StopUpDownScenarioReported), static events => events.StopUpDownScenarioReported,
            new OpenNettyEvents.StopUpDownScenarioReportedEventArgs(Endpoint, default)),

        Case(nameof(OpenNettyEvents.SwitchStateReported), static events => events.SwitchStateReported,
            new OpenNettyEvents.SwitchStateReportedEventArgs(Endpoint, default)),

        Case(nameof(OpenNettyEvents.TimedScenarioReported), static events => events.TimedScenarioReported,
            new OpenNettyEvents.TimedScenarioReportedEventArgs(Endpoint, TimeSpan.FromMinutes(1))),

        Case(nameof(OpenNettyEvents.ToggleScenarioReported), static events => events.ToggleScenarioReported,
            new OpenNettyEvents.ToggleScenarioReportedEventArgs(Endpoint)),

        Case(nameof(OpenNettyEvents.WaterHeaterSetpointModeReported), static events => events.WaterHeaterSetpointModeReported,
            new OpenNettyEvents.WaterHeaterSetpointModeReportedEventArgs(Endpoint, default)),

        Case(nameof(OpenNettyEvents.WaterHeaterStateReported), static events => events.WaterHeaterStateReported,
            new OpenNettyEvents.WaterHeaterStateReportedEventArgs(Endpoint, default)),

        Case(nameof(OpenNettyEvents.WirelessBurglarAlarmStateReported), static events => events.WirelessBurglarAlarmStateReported,
            new OpenNettyEvents.WirelessBurglarAlarmStateReportedEventArgs(Endpoint, default)),

        Case(nameof(OpenNettyEvents.ZigbeeBindingEventReported), static events => events.ZigbeeBindingEventReported,
            new OpenNettyEvents.ZigbeeBindingEventReportedEventArgs(Endpoint, default)),

        Case(nameof(OpenNettyEvents.ZigbeeNetworkEventReported), static events => events.ZigbeeNetworkEventReported,
            new OpenNettyEvents.ZigbeeNetworkEventReportedEventArgs(Endpoint, default))
    }.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

    private static KeyValuePair<string, Func<OpenNettyEvents, Task>> Case<TEventArgs>(
        string name, Func<OpenNettyEvents, IAsyncObservable<TEventArgs>> selector, TEventArgs arguments)
        where TEventArgs : OpenNettyEvents.EventArgs
        => new(name, async events =>
        {
            var observer = new RecordingObserver<TEventArgs>();
            await using var subscription = await selector(events).SubscribeAsync(observer);
            await using var connection = await events.ConnectAsync();

            // Publish an event of another type first: an observable that doesn't filter by type would return it.
            OpenNettyEvents.EventArgs other = arguments is OpenNettyEvents.ToggleScenarioReportedEventArgs
                ? new OpenNettyEvents.BatteryAlertReportedEventArgs(Endpoint)
                : new OpenNettyEvents.ToggleScenarioReportedEventArgs(Endpoint);

            await events.PublishAsync(other, TestContext.Current.CancellationToken);

            await events.PublishAsync(arguments, TestContext.Current.CancellationToken);

            Assert.Equal(arguments, await observer.NextAsync());
        });

    private sealed class RecordingObserver<T> : IAsyncObserver<T>
    {
        private readonly Channel<T> _items = Channel.CreateUnbounded<T>();
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completed => _completed.Task;

        public async Task<T> NextAsync() => await _items.Reader.ReadAsync(TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);

        public bool TryNext(out T? item) => _items.Reader.TryRead(out item);

        public ValueTask OnNextAsync(T value)
        {
            _items.Writer.TryWrite(value);
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
