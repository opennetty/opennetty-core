/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using NSubstitute;
using Xunit;

namespace OpenNetty.Mqtt.Tests;

public sealed class OpenNettyMqttHostedServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly OpenNettyEndpoint Endpoint = CreateEndpoint();

    [Theory]
    [InlineData("client")]
    [InlineData("events")]
    [InlineData("logger")]
    [InlineData("options")]
    [InlineData("worker")]
    public void Constructor_NullArgument_ThrowsArgumentNullException(string parameter)
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new OpenNettyMqttHostedService(
            parameter is "client"  ? null! : new RecordingClient(new CallLog()),
            parameter is "events"  ? null! : new OpenNettyEvents(Substitute.For<IHostApplicationLifetime>()),
            parameter is "logger"  ? null! : new RecordingLogger(),
            parameter is "options" ? null! : Substitute.For<IOptionsMonitor<OpenNettyMqttOptions>>(),
            parameter is "worker"  ? null! : new RecordingWorker(new CallLog())));

        Assert.Equal(parameter, exception.ParamName);
    }

    public static TheoryData<string> ReportedCaseNames => [.. ReportedCases.Select(static @case => @case.Name)];

    [Theory]
    [MemberData(nameof(ReportedCaseNames))]
    public async Task ReportedEvent_ValidArguments_EnqueuesExpectedMessage(string name)
    {
        var @case = ReportedCases.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();

        var message = await harness.ReportAsync(@case.Create(Endpoint));

        AssertMessage(message, $"opennetty/endpoint/{@case.Attribute}", @case.Payload, @case.Retain, @case.Json);
        await harness.AssertNoMoreReportsAsync();
    }

    public static TheoryData<string> InvalidCaseNames => [.. InvalidCases.Select(static @case => @case.Name)];

    [Theory]
    [MemberData(nameof(InvalidCaseNames))]
    public async Task ReportedEvent_InvalidArguments_IsNotReportedAndSubscriptionKeepsWorking(string name)
    {
        var @case = InvalidCases.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();

        await harness.AssertNotReportedAsync(@case.Create(Endpoint));

        AssertMessage(await harness.ReportAsync(        new OpenNettyEvents.BatteryAlertReportedEventArgs(Endpoint)),
            "opennetty/endpoint/battery_alert", "ON", retain: true, json: false);
    }

    [Fact]
    public async Task ActionScenarioReported_ActionOnDoorbell_EnqueuesRingThenActionMessages()
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();
        var endpoint = CreateEndpoint(deviceClass: OpenNettySettings.HomeAssistantDeviceClasses.Events.Doorbell);

        var messages = await harness.ReportAsync(new OpenNettyEvents.ActionScenarioReportedEventArgs(endpoint, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action), 2);

        AssertMessage(messages[0], "opennetty/endpoint/scenario", """{"event_type":"ring"}""", retain: false, json: true);
        AssertMessage(messages[1], "opennetty/endpoint/scenario", """{"event_type":"action"}""", retain: false, json: true);
        await harness.AssertNoMoreReportsAsync();
    }

    [Fact]
    public async Task ActionScenarioReported_StopActionOnDoorbell_EnqueuesOnlyStopActionMessage()
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();
        var endpoint = CreateEndpoint(deviceClass: OpenNettySettings.HomeAssistantDeviceClasses.Events.Doorbell);

        var message = await harness.ReportAsync(new OpenNettyEvents.ActionScenarioReportedEventArgs(endpoint, OpenNettyModels.ScenariosPlus.ActionScenarioType.StopAction));

        AssertMessage(message, "opennetty/endpoint/scenario", """{"event_type":"stop_action"}""", retain: false, json: true);
        await harness.AssertNoMoreReportsAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("button")]
    [InlineData("DOORBELL")]
    public async Task ActionScenarioReported_ActionOnOtherDeviceClass_EnqueuesOnlyActionMessage(string? deviceClass)
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();
        var endpoint = CreateEndpoint(deviceClass: deviceClass);

        var message = await harness.ReportAsync(new OpenNettyEvents.ActionScenarioReportedEventArgs(endpoint, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action));

        AssertMessage(message, "opennetty/endpoint/scenario", """{"event_type":"action"}""", retain: false, json: true);
        await harness.AssertNoMoreReportsAsync();
    }

    [Fact]
    public async Task DeviceCommunicationReported_Communication_EnqueuesCurrentUtcDateInRoundtripFormat()
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();
        var before = DateTimeOffset.UtcNow;

        var message = await harness.ReportAsync(new OpenNettyEvents.DeviceCommunicationReportedEventArgs(Endpoint, CreateFrameMessage()));

        var after = DateTimeOffset.UtcNow;
        Assert.Equal("opennetty/endpoint/last_communication_date", message.Topic);
        Assert.True(message.Retain);
        Assert.Null(message.ContentType);
        var payload = message.ConvertPayloadToString();
        var date = DateTimeOffset.ParseExact(payload, "o", CultureInfo.InvariantCulture);
        Assert.Equal(TimeSpan.Zero, date.Offset);
        Assert.InRange(date, before, after);
        Assert.Equal(date.ToString("o", CultureInfo.InvariantCulture), payload);
    }

    [Theory]
    [InlineData(OpenNettySessionType.Command, "command", true)]
    [InlineData(OpenNettySessionType.Event, "event", true)]
    [InlineData(OpenNettySessionType.Generic, "generic", true)]
    [InlineData(OpenNettySessionType.Command, "command", false)]
    [InlineData(OpenNettySessionType.Event, "event", false)]
    [InlineData(OpenNettySessionType.Generic, "generic", false)]
    public async Task MessageReported_Message_EnqueuesParsedAndRawMessageWithSession(
        OpenNettySessionType type, string expectedType, bool incoming)
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();
        var frameMessage = CreateFrameMessage();
        var session = CreateSession(type);

        var message = await harness.ReportAsync(incoming ?
            new OpenNettyEvents.IncomingMessageReportedEventArgs(Endpoint, frameMessage, session) :
            new OpenNettyEvents.OutgoingMessageReportedEventArgs(Endpoint, frameMessage, session));

        Assert.Equal(incoming ? "opennetty/endpoint/incoming_message" : "opennetty/endpoint/outgoing_message", message.Topic);
        Assert.False(message.Retain);
        Assert.Equal("application/json", message.ContentType);
        Assert.Equal(MqttQualityOfServiceLevel.ExactlyOnce, message.QualityOfServiceLevel);
        var payload = JsonNode.Parse(message.ConvertPayloadToString())!;
        Assert.Equal(frameMessage.Frame.ToString(), (string?) payload["message"]!["raw"]);
        Assert.Equal(frameMessage.ToJsonObject().ToJsonString(), payload["message"]!["parsed"]!.ToJsonString());
        Assert.Equal(session.Id.ToString(), (string?) payload["session"]!["id"]);
        Assert.Equal(expectedType, (string?) payload["session"]!["type"]);
    }

    [Theory]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.Comfort, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, "comfort")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.Comfort, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, "comfort:4h")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.Comfort, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, "comfort:8h")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, "comfort-1")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, "comfort-1:4h")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, "comfort-1:8h")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, "comfort-2")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, "comfort-2:4h")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, "comfort-2:8h")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.Eco, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, "eco")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.Eco, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, "eco:4h")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.Eco, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, "eco:8h")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, "frost_protection")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, "frost_protection:4h")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, "frost_protection:8h")]
    [InlineData(null, null, "none")]
    [InlineData(null, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, "none")]
    [InlineData(null, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, "none")]
    [InlineData((OpenNettyModels.TemperatureControl.PilotWireMode) 99, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, "none")]
    public async Task PilotWireDerogationModeReported_Derogation_EnqueuesExpectedMessage(
        OpenNettyModels.TemperatureControl.PilotWireMode? mode, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration? duration, string payload)
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();

        var message = await harness.ReportAsync(new OpenNettyEvents.PilotWireDerogationModeReportedEventArgs(Endpoint, mode, duration));

        AssertMessage(message, "opennetty/endpoint/pilot_wire_derogation_mode", payload, retain: true, json: false);
    }

    [Fact]
    public async Task PressureScenarioReported_PressureOnDoorbell_EnqueuesRingThenPressureMessages()
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();
        var endpoint = CreateEndpoint(deviceClass: OpenNettySettings.HomeAssistantDeviceClasses.Events.Doorbell);

        var messages = await harness.ReportAsync(
            new OpenNettyEvents.PressureScenarioReportedEventArgs(endpoint, OpenNettyModels.Scenarios.PressureScenarioType.Pressure, Button: 2), 2);

        AssertMessage(messages[0], "opennetty/endpoint/scenario",
            """{"event_type":"ring","scenario_type":"evolved","button":2}""", retain: false, json: true);
        AssertMessage(messages[1], "opennetty/endpoint/scenario",
            """{"event_type":"pressure","scenario_type":"evolved","button":2}""", retain: false, json: true);
        await harness.AssertNoMoreReportsAsync();
    }

    [Theory]
    [InlineData(OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterShortPressure, "release_after_short_pressure")]
    [InlineData(OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterExtendedPressure, "release_after_extended_pressure")]
    [InlineData(OpenNettyModels.Scenarios.PressureScenarioType.ExtendedPressure, "extended_pressure")]
    public async Task PressureScenarioReported_NonPressureTypeOnDoorbell_EnqueuesOnlyScenarioMessage(
        OpenNettyModels.Scenarios.PressureScenarioType type, string eventType)
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();
        var endpoint = CreateEndpoint(deviceClass: OpenNettySettings.HomeAssistantDeviceClasses.Events.Doorbell);

        var message = await harness.ReportAsync(new OpenNettyEvents.PressureScenarioReportedEventArgs(endpoint, type, Button: 2));

        AssertMessage(message, "opennetty/endpoint/scenario",
            $$"""{"event_type":"{{eventType}}","scenario_type":"evolved","button":2}""", retain: false, json: true);
        await harness.AssertNoMoreReportsAsync();
    }

    [Fact]
    public async Task PressureScenarioReported_PressureOnOtherDeviceClass_EnqueuesOnlyPressureMessage()
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();
        var endpoint = CreateEndpoint(deviceClass: "button");

        var message = await harness.ReportAsync(
            new OpenNettyEvents.PressureScenarioReportedEventArgs(endpoint, OpenNettyModels.Scenarios.PressureScenarioType.Pressure, Button: 2));

        AssertMessage(message, "opennetty/endpoint/scenario",
            """{"event_type":"pressure","scenario_type":"evolved","button":2}""", retain: false, json: true);
        await harness.AssertNoMoreReportsAsync();
    }

    [Theory]
    [InlineData((byte) 3)]
    [InlineData(null)]
    public async Task PressureScenarioPlusReported_ShortPressureOnDoorbell_EnqueuesRingThenShortPressureMessages(byte? button)
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();
        var endpoint = CreateEndpoint(deviceClass: OpenNettySettings.HomeAssistantDeviceClasses.Events.Doorbell);
        var value = button is null ? "null" : button.Value.ToString(CultureInfo.InvariantCulture);

        var messages = await harness.ReportAsync(
            new OpenNettyEvents.PressureScenarioPlusReportedEventArgs(endpoint, OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure, button), 2);

        AssertMessage(messages[0], "opennetty/endpoint/scenario",
            $$"""{"event_type":"ring","scenario_type":"plus","button":{{value}}}""", retain: false, json: true);
        AssertMessage(messages[1], "opennetty/endpoint/scenario",
            $$"""{"event_type":"short_pressure","scenario_type":"plus","button":{{value}}}""", retain: false, json: true);
        await harness.AssertNoMoreReportsAsync();
    }

    [Theory]
    [InlineData(OpenNettyModels.ScenariosPlus.PressureScenarioType.StartOfExtendedPressure, "start_of_extended_pressure")]
    [InlineData(OpenNettyModels.ScenariosPlus.PressureScenarioType.ExtendedPressure, "extended_pressure")]
    [InlineData(OpenNettyModels.ScenariosPlus.PressureScenarioType.EndOfExtendedPressure, "end_of_extended_pressure")]
    public async Task PressureScenarioPlusReported_NonShortPressureTypeOnDoorbell_EnqueuesOnlyScenarioMessage(
        OpenNettyModels.ScenariosPlus.PressureScenarioType type, string eventType)
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();
        var endpoint = CreateEndpoint(deviceClass: OpenNettySettings.HomeAssistantDeviceClasses.Events.Doorbell);

        var message = await harness.ReportAsync(new OpenNettyEvents.PressureScenarioPlusReportedEventArgs(endpoint, type, Button: 1));

        AssertMessage(message, "opennetty/endpoint/scenario",
            $$"""{"event_type":"{{eventType}}","scenario_type":"plus","button":1}""", retain: false, json: true);
        await harness.AssertNoMoreReportsAsync();
    }

    [Fact]
    public async Task PressureScenarioPlusReported_ShortPressureOnOtherDeviceClass_EnqueuesOnlyShortPressureMessage()
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();
        var endpoint = CreateEndpoint(deviceClass: "button");

        var message = await harness.ReportAsync(
            new OpenNettyEvents.PressureScenarioPlusReportedEventArgs(endpoint, OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure, Button: 1));

        AssertMessage(message, "opennetty/endpoint/scenario",
            """{"event_type":"short_pressure","scenario_type":"plus","button":1}""", retain: false, json: true);
        await harness.AssertNoMoreReportsAsync();
    }

    [Fact]
    public async Task ShutterPositionReported_UnknownPosition_IsNotReported()
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();

        await harness.        AssertNotReportedAsync(new OpenNettyEvents.ShutterPositionReportedEventArgs(Endpoint, Position: null));
    }

    [Theory]
    [InlineData("Endpoint", null, "opennetty/endpoint/battery_alert")]
    [InlineData("Living Room_Light", null, "opennetty/living room_light/battery_alert")]
    [InlineData("Endpoint", "custom", "opennetty/custom/battery_alert")]
    [InlineData("Endpoint", "floor1/kitchen", "opennetty/floor1/kitchen/battery_alert")]
    public async Task ReportedEvent_Endpoint_UsesMqttTopicSettingOrLowercaseName(string name, string? mqttTopic, string expected)
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();

        var message = await harness.ReportAsync(new OpenNettyEvents.BatteryAlertReportedEventArgs(CreateEndpoint(name, mqttTopic)));

        Assert.Equal(expected, message.Topic);
    }

    [Fact]
    public async Task ReportedEvent_RootTopicChanged_UsesCurrentRootTopic()
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();
        harness.Options.RootTopic = "home";

        var message = await harness.ReportAsync(new OpenNettyEvents.BatteryAlertReportedEventArgs(Endpoint));

        Assert.Equal("home/endpoint/battery_alert", message.Topic);
    }

    [Fact]
    public async Task ReportedEvent_EnqueueFails_IsDroppedAndSubscriptionKeepsWorking()
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();
        harness.Client.FailNextEnqueue(new InvalidOperationException());

        await harness.AssertNotReportedAsync(new OpenNettyEvents.BatteryAlertReportedEventArgs(Endpoint));

        var message = await harness.ReportAsync(new OpenNettyEvents.BatteryAlertReportedEventArgs(Endpoint));
        Assert.Equal("opennetty/endpoint/battery_alert", message.Topic);
    }

    [Fact]
    public async Task ReportedEvent_EventsOfSeveralTypes_AreReportedInOrder()
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();

        await harness.PublishAsync(new OpenNettyEvents.BatteryAlertReportedEventArgs(Endpoint));
        await harness.PublishAsync(new OpenNettyEvents.BatteryLevelReportedEventArgs(Endpoint, 10));
        await harness.PublishAsync(new OpenNettyEvents.SwitchStateReportedEventArgs(Endpoint, OpenNettyModels.Lighting.SwitchState.On));

        Assert.Equal("opennetty/endpoint/battery_alert", (await harness.Client.NextEnqueuedAsync()).Topic);
        Assert.Equal("opennetty/endpoint/battery_level", (await harness.Client.NextEnqueuedAsync()).Topic);
        Assert.Equal("opennetty/endpoint/switch_state", (await harness.Client.NextEnqueuedAsync()).Topic);
    }

    [Fact]
    public async Task ReportedEvent_SubscriptionsDisposed_IsNoLongerReported()
    {
        await using var harness = new ServiceHarness();
        await harness.SubscribeAsync();
        var observer = new RecordingObserver<OpenNettyEvents.BatteryAlertReportedEventArgs>();
        await using var subscription = await harness.Events.BatteryAlertReported.SubscribeAsync(observer);
        await harness.UnsubscribeAsync();

        await harness.PublishAsync(new OpenNettyEvents.BatteryAlertReportedEventArgs(Endpoint));
        await harness.PublishAsync(new OpenNettyEvents.BatteryAlertReportedEventArgs(CreateEndpoint("Other")));
        await observer.NextAsync();
        await observer.NextAsync();

        Assert.False(harness.Client.TryTakeEnqueued(out _));
    }

    [Fact]
    public async Task ExecuteAsync_NoClientOptions_DoesNothing()
    {
        await using var harness = new ServiceHarness(static options => options.ClientOptions = null);

        await harness.StartAsync();

        await harness.Service.ExecuteTask!.WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);
        Assert.Empty(harness.Log.Entries);
        Assert.Null(harness.Client.StartedOptions);
    }

    [Theory]
    [InlineData("a/get", "ON")]
    [InlineData("a/set", "OFF")]
    [InlineData("opennetty/endpoint/get", "")]
    [InlineData("opennetty/endpoint/set", "{\"state\":\"ON\"}")]
    [InlineData("opennetty/with/several/levels/set", "42")]
    public async Task ApplicationMessageReceived_GetOrSetTopic_IsDispatchedToWorkerAndLogged(string topic, string payload)
    {
        await using var harness = new ServiceHarness();
        await harness.StartAsync();
        var call = await harness.Worker.NextProcessCallAsync();
        var message = CreateMessage(topic, payload);

        await harness.Client.RaiseReceivedAsync(message);

        Assert.Same(message, await ReadAsync(call.Reader));
        var entry = Assert.Single(harness.Logger.Entries, static entry => entry.EventId is 6021);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Contains(topic, entry.Message, StringComparison.Ordinal);
        Assert.Contains(payload, entry.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/get")]
    [InlineData("/set")]
    [InlineData("get")]
    [InlineData("opennetty/endpoint/state")]
    [InlineData("opennetty/endpoint/GET")]
    [InlineData("opennetty/endpoint/Set")]
    [InlineData("opennetty/endpoint/get/")]
    [InlineData("opennetty/endpoint/set/attribute")]
    [InlineData("opennetty/endpoint/forget")]
    public async Task ApplicationMessageReceived_OtherTopic_IsIgnored(string topic)
    {
        await using var harness = new ServiceHarness();
        await harness.StartAsync();
        var call = await harness.Worker.NextProcessCallAsync();
        var expected = CreateMessage("opennetty/endpoint/set", "ON");

        await harness.Client.RaiseReceivedAsync(CreateMessage(topic, "ON"));
        await harness.Client.RaiseReceivedAsync(expected);

        Assert.Same(expected, await ReadAsync(call.Reader));
        Assert.False(call.Reader.TryRead(out _));
        Assert.Single(harness.Logger.Entries, static entry => entry.EventId is 6021);
    }

    [Fact]
    public async Task ApplicationMessageReceived_MessageWithoutPayload_IsDispatchedToWorker()
    {
        await using var harness = new ServiceHarness();
        await harness.StartAsync();
        var call = await harness.Worker.NextProcessCallAsync();
        var message = new MqttApplicationMessage { Topic = "opennetty/endpoint/get" };

        await harness.Client.RaiseReceivedAsync(message);

        Assert.Same(message, await ReadAsync(call.Reader));
        Assert.Single(harness.Logger.Entries, static entry => entry.EventId is 6021);
    }

    [Fact]
    public async Task ApplicationMessageReceived_SeveralMessages_AreDispatchedInOrder()
    {
        await using var harness = new ServiceHarness();
        await harness.StartAsync();
        var call = await harness.Worker.NextProcessCallAsync();
        var messages = Enumerable.Range(0, 5)
            .Select(static index => CreateMessage(
                $"opennetty/endpoint{index.ToString(CultureInfo.InvariantCulture)}/set", index.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        foreach (var message in messages)
        {
            await harness.Client.RaiseReceivedAsync(message);
        }

        foreach (var message in messages)
        {
            Assert.Same(message, await ReadAsync(call.Reader));
        }
    }

    [Fact]
    public async Task ApplicationMessageReceived_ServiceStopped_ThrowsOperationCanceledException()
    {
        await using var harness = new ServiceHarness();
        await harness.StartAsync();
        await harness.Worker.NextProcessCallAsync();
        await harness.StopAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await harness.Client.RaiseReceivedAsync(CreateMessage("opennetty/endpoint/set", "ON")));
    }

    [Fact]
    public async Task ConnectingFailed_Raised_LogsWarningWithException()
    {
        await using var harness = new ServiceHarness();
        await harness.StartAsync();
        await harness.Worker.NextProcessCallAsync();
        var failure = new InvalidOperationException();

        await harness.Client.RaiseConnectingFailedAsync(failure);

        var entry = Assert.Single(harness.Logger.Entries, static entry => entry.EventId is 6019);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Same(failure, entry.Exception);
    }

    [Fact]
    public async Task ApplicationMessageProcessed_Succeeded_LogsDebugWithTopicAndPayload()
    {
        await using var harness = new ServiceHarness();
        await harness.StartAsync();
        await harness.Worker.NextProcessCallAsync();

        await harness.Client.RaiseProcessedAsync(CreateMessage("opennetty/endpoint/state", "ON"), exception: null);

        var entry = Assert.Single(harness.Logger.Entries, static entry => entry.EventId is 6022);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Contains("opennetty/endpoint/state", entry.Message, StringComparison.Ordinal);
        Assert.Contains("ON", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Logger.Entries, static entry => entry.EventId is 6023);
    }

    [Fact]
    public async Task ApplicationMessageProcessed_Failed_LogsErrorWithExceptionTopicAndPayload()
    {
        await using var harness = new ServiceHarness();
        await harness.StartAsync();
        await harness.Worker.NextProcessCallAsync();
        var failure = new InvalidOperationException();

        await harness.Client.RaiseProcessedAsync(CreateMessage("opennetty/endpoint/state", "ON"), failure);

        var entry = Assert.Single(harness.Logger.Entries, static entry => entry.EventId is 6023);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(failure, entry.Exception);
        Assert.Contains("opennetty/endpoint/state", entry.Message, StringComparison.Ordinal);
        Assert.Contains("ON", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Logger.Entries, static entry => entry.EventId is 6022);
    }

    [Fact]
    public async Task ExecuteAsync_ClientOptions_StartsManagedClientWithThem()
    {
        await using var harness = new ServiceHarness();

        await harness.StartAsync();
        await harness.Worker.NextProcessCallAsync();

        Assert.Same(harness.Options.ClientOptions, harness.Client.StartedOptions!.ClientOptions);
    }

    [Theory]
    [InlineData("opennetty")]
    [InlineData("home/opennetty")]
    public async Task ExecuteAsync_ClientStarted_SubscribesToRootTopicWithExactlyOnceQos(string rootTopic)
    {
        await using var harness = new ServiceHarness(options => options.RootTopic = rootTopic);

        await harness.StartAsync();
        await harness.Worker.NextProcessCallAsync();

        var filter = Assert.Single(harness.Client.Subscriptions);
        Assert.Equal($"{rootTopic}/#", filter.Topic);
        Assert.Equal(MqttQualityOfServiceLevel.ExactlyOnce, filter.QualityOfServiceLevel);
    }

    [Fact]
    public async Task ExecuteAsync_Running_StartsClientThenSubscribesThenRunsWorkerAndStopsClientLast()
    {
        await using var harness = new ServiceHarness();
        await harness.StartAsync();
        await harness.Worker.NextProcessCallAsync();
        await harness.Worker.NextAnnounceCallAsync();

        await harness.StopAsync();

        Assert.Equal(["start", "subscribe", "process", "announce", "stop"], harness.Log.Entries);
    }

    [Fact]
    public async Task ExecuteAsync_Running_PassesClientAndStoppingTokenToWorker()
    {
        await using var harness = new ServiceHarness();
        await harness.StartAsync();

        var process = await harness.Worker.NextProcessCallAsync();
        var announce = await harness.Worker.NextAnnounceCallAsync();

        Assert.Same(harness.Client, process.Client);
        Assert.Same(harness.Client, announce.Client);
        Assert.False(process.CancellationToken.IsCancellationRequested);
        Assert.False(announce.CancellationToken.IsCancellationRequested);

        await harness.StopAsync();

        Assert.True(process.CancellationToken.IsCancellationRequested);
        Assert.True(announce.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task ExecuteAsync_StopRequested_StopsManagedClientGracefullyAndCompletes()
    {
        await using var harness = new ServiceHarness();
        await harness.StartAsync();
        await harness.Worker.NextProcessCallAsync();

        await harness.StopAsync();

        await harness.Service.ExecuteTask!.WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);
        Assert.Equal(TaskStatus.RanToCompletion, harness.Service.ExecuteTask.Status);
        Assert.Equal([true], harness.Client.StopRequests);
    }

    [Fact]
    public async Task ExecuteAsync_WorkersCanceledOnStop_StopsManagedClientAndCompletesAsCanceled()
    {
        await using var harness = new ServiceHarness();
        harness.Worker.ProcessBehavior = static async call =>
        {
            await WaitForCancellationAsync(call.CancellationToken);
            call.CancellationToken.ThrowIfCancellationRequested();
        };
        await harness.StartAsync();
        await harness.Worker.NextProcessCallAsync();

        await harness.StopAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await harness.Service.ExecuteTask!);
        Assert.Equal(["start", "subscribe", "process", "announce", "stop"], harness.Log.Entries);
    }

    [Fact]
    public async Task ExecuteAsync_AnnouncementStillRunning_DoesNotStopClientUntilItCompletes()
    {
        await using var harness = new ServiceHarness();
        var announcement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Worker.ProcessBehavior = static _ => Task.CompletedTask;
        harness.Worker.AnnounceBehavior = _ => announcement.Task;
        await harness.StartAsync();
        await harness.Worker.NextAnnounceCallAsync();

        Assert.False(harness.Service.ExecuteTask!.IsCompleted);
        Assert.DoesNotContain(harness.Log.Entries, static entry => entry is "stop");

        announcement.SetResult();
        await harness.Service.ExecuteTask.WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.Equal("stop", harness.Log.Entries[^1]);
    }

    [Fact]
    public async Task ExecuteAsync_MessageProcessingFails_StopsClientAndPropagatesException()
    {
        await using var harness = new ServiceHarness();
        var failure = new InvalidOperationException();
        harness.Worker.ProcessBehavior = async _ =>
        {
            await Task.Yield();
            throw failure;
        };
        harness.Worker.AnnounceBehavior = static _ => Task.CompletedTask;

        await harness.StartAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await harness.Service.ExecuteTask!);
        Assert.Same(failure, exception);
        Assert.Equal("stop", harness.Log.Entries[^1]);
    }

    [Fact]
    public async Task ExecuteAsync_AnnouncementFails_StopsClientAndPropagatesException()
    {
        await using var harness = new ServiceHarness();
        var failure = new InvalidOperationException();
        harness.Worker.ProcessBehavior = static _ => Task.CompletedTask;
        harness.Worker.AnnounceBehavior = async _ =>
        {
            await Task.Yield();
            throw failure;
        };

        await harness.StartAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await harness.Service.ExecuteTask!);
        Assert.Same(failure, exception);
        Assert.Equal("stop", harness.Log.Entries[^1]);
    }

    [Fact]
    public async Task ExecuteAsync_SubscriptionFails_StopsClientWithoutRunningWorkersAndPropagatesException()
    {
        await using var harness = new ServiceHarness();
        var failure = new InvalidOperationException();
        harness.Client.SubscribeFailure = failure;

        await harness.StartAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await harness.Service.ExecuteTask!);
        Assert.Same(failure, exception);
        Assert.Equal(["start", "subscribe", "stop"], harness.Log.Entries);
    }

    [Fact]
    public async Task ExecuteAsync_StartFails_PropagatesExceptionWithoutStoppingClient()
    {
        await using var harness = new ServiceHarness();
        var failure = new InvalidOperationException();
        harness.Client.StartFailure = failure;

        await harness.StartAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await harness.Service.ExecuteTask!);
        Assert.Same(failure, exception);
        Assert.Equal(["start"], harness.Log.Entries);
    }

    [Fact]
    public async Task ExecuteAsync_StopFails_PropagatesException()
    {
        await using var harness = new ServiceHarness();
        var failure = new InvalidOperationException();
        harness.Client.StopFailure = failure;
        await harness.StartAsync();
        await harness.Worker.NextProcessCallAsync();

        await harness.StopAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await harness.Service.ExecuteTask!);
        Assert.Same(failure, exception);
    }

    private static readonly ReportedCase[] ReportedCases =
    [
        Scenario("ActionScenario_Action", static e => new OpenNettyEvents.ActionScenarioReportedEventArgs(e, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action), """{"event_type":"action"}"""),
        Scenario("ActionScenario_StopAction", static e => new OpenNettyEvents.ActionScenarioReportedEventArgs(e, OpenNettyModels.ScenariosPlus.ActionScenarioType.StopAction), """{"event_type":"stop_action"}"""),
        State("Availability_Offline", static e => new OpenNettyEvents.AvailabilityReportedEventArgs(e, OpenNettyModels.Diagnostics.Availability.Offline), "availability", "offline"),
        State("Availability_Online", static e => new OpenNettyEvents.AvailabilityReportedEventArgs(e, OpenNettyModels.Diagnostics.Availability.Online), "availability", "online"),
        State("BatteryAlert", static e => new OpenNettyEvents.BatteryAlertReportedEventArgs(e), "battery_alert", "ON"),
        State("BatteryLevel_Zero", static e => new OpenNettyEvents.BatteryLevelReportedEventArgs(e, 0), "battery_level", "0"),
        State("BatteryLevel_Value", static e => new OpenNettyEvents.BatteryLevelReportedEventArgs(e, 42), "battery_level", "42"),
        State("BatteryLevel_Max", static e => new OpenNettyEvents.BatteryLevelReportedEventArgs(e, 255), "battery_level", "255"),
        State("Brightness_Zero", static e => new OpenNettyEvents.BrightnessReportedEventArgs(e, 0), "brightness", "0"),
        State("Brightness_Max", static e => new OpenNettyEvents.BrightnessReportedEventArgs(e, 100), "brightness", "100"),
        Scenario("DimmingScenario_Positive", static e => new OpenNettyEvents.DimmingScenarioReportedEventArgs(e, 3), """{"event_type":"dimming","dimming_step":3}"""),
        Scenario("DimmingScenario_Negative", static e => new OpenNettyEvents.DimmingScenarioReportedEventArgs(e, -2), """{"event_type":"dimming","dimming_step":-2}"""),
        Scenario("DryContactScenario_Closed", static e => new OpenNettyEvents.DryContactScenarioReportedEventArgs(e, OpenNettyModels.ScenariosPlus.DryContactScenarioType.Closed), """{"event_type":"contact_closed"}"""),
        Scenario("DryContactScenario_Open", static e => new OpenNettyEvents.DryContactScenarioReportedEventArgs(e, OpenNettyModels.ScenariosPlus.DryContactScenarioType.Open), """{"event_type":"contact_open"}"""),
        JsonState("DryContactState_ClosedStateRequest", static e => new OpenNettyEvents.DryContactStateReportedEventArgs(e, OpenNettyModels.ScenariosPlus.DryContactState.Closed, OpenNettyModels.ScenariosPlus.DryContactStateOrigin.StateRequest), "dry_contact_state", """{"state":"closed","origin":"state_request"}"""),
        JsonState("DryContactState_ClosedSystemEvent", static e => new OpenNettyEvents.DryContactStateReportedEventArgs(e, OpenNettyModels.ScenariosPlus.DryContactState.Closed, OpenNettyModels.ScenariosPlus.DryContactStateOrigin.SystemEvent), "dry_contact_state", """{"state":"closed","origin":"system_event"}"""),
        JsonState("DryContactState_OpenStateRequest", static e => new OpenNettyEvents.DryContactStateReportedEventArgs(e, OpenNettyModels.ScenariosPlus.DryContactState.Open, OpenNettyModels.ScenariosPlus.DryContactStateOrigin.StateRequest), "dry_contact_state", """{"state":"open","origin":"state_request"}"""),
        JsonState("DryContactState_OpenSystemEvent", static e => new OpenNettyEvents.DryContactStateReportedEventArgs(e, OpenNettyModels.ScenariosPlus.DryContactState.Open, OpenNettyModels.ScenariosPlus.DryContactStateOrigin.SystemEvent), "dry_contact_state", """{"state":"open","origin":"system_event"}"""),
        Scenario("OnOffScenario_Off", static e => new OpenNettyEvents.OnOffScenarioReportedEventArgs(e, OpenNettyModels.Lighting.OnOffScenarioType.Off), """{"event_type":"switch_off"}"""),
        Scenario("OnOffScenario_On", static e => new OpenNettyEvents.OnOffScenarioReportedEventArgs(e, OpenNettyModels.Lighting.OnOffScenarioType.On), """{"event_type":"switch_on"}"""),
        State("PilotWireSetpointMode_Comfort", static e => new OpenNettyEvents.PilotWireSetpointModeReportedEventArgs(e, OpenNettyModels.TemperatureControl.PilotWireMode.Comfort), "pilot_wire_setpoint_mode", "comfort"),
        State("PilotWireSetpointMode_ComfortMinusOne", static e => new OpenNettyEvents.PilotWireSetpointModeReportedEventArgs(e, OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne), "pilot_wire_setpoint_mode", "comfort-1"),
        State("PilotWireSetpointMode_ComfortMinusTwo", static e => new OpenNettyEvents.PilotWireSetpointModeReportedEventArgs(e, OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo), "pilot_wire_setpoint_mode", "comfort-2"),
        State("PilotWireSetpointMode_Eco", static e => new OpenNettyEvents.PilotWireSetpointModeReportedEventArgs(e, OpenNettyModels.TemperatureControl.PilotWireMode.Eco), "pilot_wire_setpoint_mode", "eco"),
        State("PilotWireSetpointMode_FrostProtection", static e => new OpenNettyEvents.PilotWireSetpointModeReportedEventArgs(e, OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection), "pilot_wire_setpoint_mode", "frost_protection"),
        State("PilotWireShutdownMode_Active", static e => new OpenNettyEvents.PilotWireShutdownModeReportedEventArgs(e, Active: true), "pilot_wire_shutdown_mode", "ON"),
        State("PilotWireShutdownMode_Inactive", static e => new OpenNettyEvents.PilotWireShutdownModeReportedEventArgs(e, Active: false), "pilot_wire_shutdown_mode", "OFF"),
        Scenario("PressureScenario_Pressure", static e => new OpenNettyEvents.PressureScenarioReportedEventArgs(e, OpenNettyModels.Scenarios.PressureScenarioType.Pressure, 4), """{"event_type":"pressure","scenario_type":"evolved","button":4}"""),
        Scenario("PressureScenario_ReleaseAfterShortPressure", static e => new OpenNettyEvents.PressureScenarioReportedEventArgs(e, OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterShortPressure, 4), """{"event_type":"release_after_short_pressure","scenario_type":"evolved","button":4}"""),
        Scenario("PressureScenarioPlus_ShortPressure", static e => new OpenNettyEvents.PressureScenarioPlusReportedEventArgs(e, OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure, 4), """{"event_type":"short_pressure","scenario_type":"plus","button":4}"""),
        Scenario("PressureScenarioPlus_StartOfExtendedPressureWithoutButton", static e => new OpenNettyEvents.PressureScenarioPlusReportedEventArgs(e, OpenNettyModels.ScenariosPlus.PressureScenarioType.StartOfExtendedPressure, null), """{"event_type":"start_of_extended_pressure","scenario_type":"plus","button":null}"""),
        Scenario("ProgressiveScenario", static e => new OpenNettyEvents.ProgressiveScenarioReportedEventArgs(e, TimeSpan.FromSeconds(1.5)), """{"event_type":"progressive_action","duration":1.5}"""),
        State("ShutterPosition_Closed", static e => new OpenNettyEvents.ShutterPositionReportedEventArgs(e, 0), "shutter_position", "0"),
        State("ShutterPosition_Partial", static e => new OpenNettyEvents.ShutterPositionReportedEventArgs(e, 50), "shutter_position", "50"),
        State("ShutterPosition_Open", static e => new OpenNettyEvents.ShutterPositionReportedEventArgs(e, 100), "shutter_position", "100"),
        State("ShutterState_Stopped", static e => new OpenNettyEvents.ShutterStateReportedEventArgs(e, OpenNettyModels.Automation.ShutterState.Stopped), "shutter_state", "stopped"),
        State("ShutterState_Opening", static e => new OpenNettyEvents.ShutterStateReportedEventArgs(e, OpenNettyModels.Automation.ShutterState.Opening), "shutter_state", "opening"),
        State("ShutterState_Closing", static e => new OpenNettyEvents.ShutterStateReportedEventArgs(e, OpenNettyModels.Automation.ShutterState.Closing), "shutter_state", "closing"),
        State("ShutterState_Open", static e => new OpenNettyEvents.ShutterStateReportedEventArgs(e, OpenNettyModels.Automation.ShutterState.Open), "shutter_state", "open"),
        State("ShutterState_Closed", static e => new OpenNettyEvents.ShutterStateReportedEventArgs(e, OpenNettyModels.Automation.ShutterState.Closed), "shutter_state", "closed"),
        State("SmartMeterPowerCutMode_Active", static e => new OpenNettyEvents.SmartMeterPowerCutModeReportedEventArgs(e, Active: true), "smart_meter_power_cut_mode", "ON"),
        State("SmartMeterPowerCutMode_Inactive", static e => new OpenNettyEvents.SmartMeterPowerCutModeReportedEventArgs(e, Active: false), "smart_meter_power_cut_mode", "OFF"),
        State("SmartMeterRateType_Peak", static e => new OpenNettyEvents.SmartMeterRateTypeReportedEventArgs(e, OpenNettyModels.TemperatureControl.SmartMeterRateType.Peak), "smart_meter_rate_type", "peak"),
        State("SmartMeterRateType_OffPeak", static e => new OpenNettyEvents.SmartMeterRateTypeReportedEventArgs(e, OpenNettyModels.TemperatureControl.SmartMeterRateType.OffPeak), "smart_meter_rate_type", "off_peak"),
        Scenario("StopUpDownScenario_Stop", static e => new OpenNettyEvents.StopUpDownScenarioReportedEventArgs(e, OpenNettyModels.Automation.StopUpDownScenarioType.Stop), """{"event_type":"shutter_stop"}"""),
        Scenario("StopUpDownScenario_Up", static e => new OpenNettyEvents.StopUpDownScenarioReportedEventArgs(e, OpenNettyModels.Automation.StopUpDownScenarioType.Up), """{"event_type":"shutter_up"}"""),
        Scenario("StopUpDownScenario_Down", static e => new OpenNettyEvents.StopUpDownScenarioReportedEventArgs(e, OpenNettyModels.Automation.StopUpDownScenarioType.Down), """{"event_type":"shutter_down"}"""),
        State("SwitchState_Off", static e => new OpenNettyEvents.SwitchStateReportedEventArgs(e, OpenNettyModels.Lighting.SwitchState.Off), "switch_state", "OFF"),
        State("SwitchState_On", static e => new OpenNettyEvents.SwitchStateReportedEventArgs(e, OpenNettyModels.Lighting.SwitchState.On), "switch_state", "ON"),
        Scenario("TimedScenario", static e => new OpenNettyEvents.TimedScenarioReportedEventArgs(e, TimeSpan.FromSeconds(30)), """{"event_type":"timed_action","duration":30}"""),
        Scenario("ToggleScenario", static e => new OpenNettyEvents.ToggleScenarioReportedEventArgs(e), """{"event_type":"switch_toggle"}"""),
        State("WaterHeaterSetpointMode_Automatic", static e => new OpenNettyEvents.WaterHeaterSetpointModeReportedEventArgs(e, OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic), "water_heater_setpoint_mode", "automatic"),
        State("WaterHeaterSetpointMode_ForcedOff", static e => new OpenNettyEvents.WaterHeaterSetpointModeReportedEventArgs(e, OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOff), "water_heater_setpoint_mode", "forced_off"),
        State("WaterHeaterSetpointMode_ForcedOn", static e => new OpenNettyEvents.WaterHeaterSetpointModeReportedEventArgs(e, OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn), "water_heater_setpoint_mode", "forced_on"),
        State("WaterHeaterState_Idle", static e => new OpenNettyEvents.WaterHeaterStateReportedEventArgs(e, OpenNettyModels.TemperatureControl.WaterHeaterState.Idle), "water_heater_state", "idle"),
        State("WaterHeaterState_Heating", static e => new OpenNettyEvents.WaterHeaterStateReportedEventArgs(e, OpenNettyModels.TemperatureControl.WaterHeaterState.Heating), "water_heater_state", "heating"),
        State("WirelessBurglarAlarmState_Disarmed", static e => new OpenNettyEvents.WirelessBurglarAlarmStateReportedEventArgs(e, OpenNettyModels.Alarm.WirelessBurglarAlarmState.Disarmed), "wireless_burglar_alarm_state", "disarmed"),
        State("WirelessBurglarAlarmState_Armed", static e => new OpenNettyEvents.WirelessBurglarAlarmStateReportedEventArgs(e, OpenNettyModels.Alarm.WirelessBurglarAlarmState.Armed), "wireless_burglar_alarm_state", "armed"),
        State("WirelessBurglarAlarmState_PartiallyArmed", static e => new OpenNettyEvents.WirelessBurglarAlarmStateReportedEventArgs(e, OpenNettyModels.Alarm.WirelessBurglarAlarmState.PartiallyArmed), "wireless_burglar_alarm_state", "partially_armed"),
        State("WirelessBurglarAlarmState_ExitDelayElapsed", static e => new OpenNettyEvents.WirelessBurglarAlarmStateReportedEventArgs(e, OpenNettyModels.Alarm.WirelessBurglarAlarmState.ExitDelayElapsed), "wireless_burglar_alarm_state", "exit_delay_elapsed"),
        State("WirelessBurglarAlarmState_Triggered", static e => new OpenNettyEvents.WirelessBurglarAlarmStateReportedEventArgs(e, OpenNettyModels.Alarm.WirelessBurglarAlarmState.Triggered), "wireless_burglar_alarm_state", "triggered"),
        State("WirelessBurglarAlarmState_EventDetected", static e => new OpenNettyEvents.WirelessBurglarAlarmStateReportedEventArgs(e, OpenNettyModels.Alarm.WirelessBurglarAlarmState.EventDetected), "wireless_burglar_alarm_state", "event_detected"),
        State("ZigbeeBindingEvent_Closed", static e => new OpenNettyEvents.ZigbeeBindingEventReportedEventArgs(e, OpenNettyModels.ScenariosPlus.ZigbeeBindingEventType.Closed), "zigbee_binding", "closed"),
        State("ZigbeeBindingEvent_Canceled", static e => new OpenNettyEvents.ZigbeeBindingEventReportedEventArgs(e, OpenNettyModels.ScenariosPlus.ZigbeeBindingEventType.Canceled), "zigbee_binding", "canceled"),
        State("ZigbeeBindingEvent_Open", static e => new OpenNettyEvents.ZigbeeBindingEventReportedEventArgs(e, OpenNettyModels.ScenariosPlus.ZigbeeBindingEventType.Open), "zigbee_binding", "open"),
        State("ZigbeeNetworkEvent_Closed", static e => new OpenNettyEvents.ZigbeeNetworkEventReportedEventArgs(e, OpenNettyModels.Management.ZigbeeNetworkEventType.Closed), "zigbee_network", "closed"),
        State("ZigbeeNetworkEvent_Created", static e => new OpenNettyEvents.ZigbeeNetworkEventReportedEventArgs(e, OpenNettyModels.Management.ZigbeeNetworkEventType.Created), "zigbee_network", "created"),
        State("ZigbeeNetworkEvent_Joined", static e => new OpenNettyEvents.ZigbeeNetworkEventReportedEventArgs(e, OpenNettyModels.Management.ZigbeeNetworkEventType.Joined), "zigbee_network", "joined"),
        State("ZigbeeNetworkEvent_Left", static e => new OpenNettyEvents.ZigbeeNetworkEventReportedEventArgs(e, OpenNettyModels.Management.ZigbeeNetworkEventType.Left), "zigbee_network", "left"),
        State("ZigbeeNetworkEvent_Open", static e => new OpenNettyEvents.ZigbeeNetworkEventReportedEventArgs(e, OpenNettyModels.Management.ZigbeeNetworkEventType.Open), "zigbee_network", "open")
    ];

    private static readonly InvalidCase[] InvalidCases =
    [
        new("ActionScenario", static e => new OpenNettyEvents.ActionScenarioReportedEventArgs(e, (OpenNettyModels.ScenariosPlus.ActionScenarioType) 99)),
        new("Availability", static e => new OpenNettyEvents.AvailabilityReportedEventArgs(e, (OpenNettyModels.Diagnostics.Availability) 99)),
        new("DryContactScenario", static e => new OpenNettyEvents.DryContactScenarioReportedEventArgs(e, (OpenNettyModels.ScenariosPlus.DryContactScenarioType) 99)),
        new("DryContactState_State", static e => new OpenNettyEvents.DryContactStateReportedEventArgs(e, (OpenNettyModels.ScenariosPlus.DryContactState) 99, OpenNettyModels.ScenariosPlus.DryContactStateOrigin.StateRequest)),
        new("DryContactState_Origin", static e => new OpenNettyEvents.DryContactStateReportedEventArgs(e, OpenNettyModels.ScenariosPlus.DryContactState.Open, (OpenNettyModels.ScenariosPlus.DryContactStateOrigin) 99)),
        new("IncomingMessage_SessionType", static e => new OpenNettyEvents.IncomingMessageReportedEventArgs(e, CreateFrameMessage(), CreateSession((OpenNettySessionType) 99))),
        new("OnOffScenario", static e => new OpenNettyEvents.OnOffScenarioReportedEventArgs(e, (OpenNettyModels.Lighting.OnOffScenarioType) 99)),
        new("OutgoingMessage_SessionType", static e => new OpenNettyEvents.OutgoingMessageReportedEventArgs(e, CreateFrameMessage(), CreateSession((OpenNettySessionType) 99))),
        new("PilotWireDerogationMode_ComfortWithoutDuration", static e => new OpenNettyEvents.PilotWireDerogationModeReportedEventArgs(e, OpenNettyModels.TemperatureControl.PilotWireMode.Comfort, null)),
        new("PilotWireDerogationMode_ComfortInvalidDuration", static e => new OpenNettyEvents.PilotWireDerogationModeReportedEventArgs(e, OpenNettyModels.TemperatureControl.PilotWireMode.Comfort, (OpenNettyModels.TemperatureControl.PilotWireDerogationDuration) 99)),
        new("PilotWireDerogationMode_ComfortMinusOneInvalidDuration", static e => new OpenNettyEvents.PilotWireDerogationModeReportedEventArgs(e, OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne, (OpenNettyModels.TemperatureControl.PilotWireDerogationDuration) 99)),
        new("PilotWireDerogationMode_ComfortMinusTwoInvalidDuration", static e => new OpenNettyEvents.PilotWireDerogationModeReportedEventArgs(e, OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo, (OpenNettyModels.TemperatureControl.PilotWireDerogationDuration) 99)),
        new("PilotWireDerogationMode_EcoInvalidDuration", static e => new OpenNettyEvents.PilotWireDerogationModeReportedEventArgs(e, OpenNettyModels.TemperatureControl.PilotWireMode.Eco, (OpenNettyModels.TemperatureControl.PilotWireDerogationDuration) 99)),
        new("PilotWireDerogationMode_FrostProtectionInvalidDuration", static e => new OpenNettyEvents.PilotWireDerogationModeReportedEventArgs(e, OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection, (OpenNettyModels.TemperatureControl.PilotWireDerogationDuration) 99)),
        new("PilotWireSetpointMode", static e => new OpenNettyEvents.PilotWireSetpointModeReportedEventArgs(e, (OpenNettyModels.TemperatureControl.PilotWireMode) 99)),
        new("PressureScenario", static e => new OpenNettyEvents.PressureScenarioReportedEventArgs(e, (OpenNettyModels.Scenarios.PressureScenarioType) 99, 1)),
        new("PressureScenarioPlus", static e => new OpenNettyEvents.PressureScenarioPlusReportedEventArgs(e, (OpenNettyModels.ScenariosPlus.PressureScenarioType) 99, 1)),
        new("ShutterState", static e => new OpenNettyEvents.ShutterStateReportedEventArgs(e, (OpenNettyModels.Automation.ShutterState) 99)),
        new("SmartMeterRateType", static e => new OpenNettyEvents.SmartMeterRateTypeReportedEventArgs(e, (OpenNettyModels.TemperatureControl.SmartMeterRateType) 99)),
        new("StopUpDownScenario", static e => new OpenNettyEvents.StopUpDownScenarioReportedEventArgs(e, (OpenNettyModels.Automation.StopUpDownScenarioType) 99)),
                new("WaterHeaterSetpointMode", static e => new OpenNettyEvents.WaterHeaterSetpointModeReportedEventArgs(e, (OpenNettyModels.TemperatureControl.WaterHeaterMode) 99)),
                new("WaterHeaterState", static e => new OpenNettyEvents.WaterHeaterStateReportedEventArgs(e, (OpenNettyModels.TemperatureControl.WaterHeaterState) 99)),
                new("WirelessBurglarAlarmState", static e => new OpenNettyEvents.WirelessBurglarAlarmStateReportedEventArgs(e, (OpenNettyModels.Alarm.WirelessBurglarAlarmState) 99)),
                new("ZigbeeBindingEvent", static e => new OpenNettyEvents.ZigbeeBindingEventReportedEventArgs(e, (OpenNettyModels.ScenariosPlus.ZigbeeBindingEventType) 99)),
                new("ZigbeeNetworkEvent", static e => new OpenNettyEvents.ZigbeeNetworkEventReportedEventArgs(e, (OpenNettyModels.Management.ZigbeeNetworkEventType) 99))
    ];

    private static ReportedCase Scenario(string name, Func<OpenNettyEndpoint, OpenNettyEvents.EventArgs> create, string payload)
        => new(name, create, OpenNettyMqttAttributes.Scenario, payload, Retain: false, Json: true);

    private static ReportedCase State(string name, Func<OpenNettyEndpoint, OpenNettyEvents.EventArgs> create, string attribute, string payload)
        => new(name, create, attribute, payload, Retain: true, Json: false);

    private static ReportedCase JsonState(string name, Func<OpenNettyEndpoint, OpenNettyEvents.EventArgs> create, string attribute, string payload)
        => new(name, create, attribute, payload, Retain: true, Json: true);

    private static void AssertMessage(MqttApplicationMessage message, string topic, string payload, bool retain, bool json)
    {
        Assert.Equal(topic, message.Topic);
        Assert.Equal(payload, message.ConvertPayloadToString());
        Assert.Equal(retain, message.Retain);
        Assert.Equal(json ? "application/json" : null, message.ContentType);
        Assert.Equal(MqttQualityOfServiceLevel.ExactlyOnce, message.QualityOfServiceLevel);
        Assert.Equal(MqttPayloadFormatIndicator.CharacterData, message.PayloadFormatIndicator);
    }

    private static OpenNettyMessage CreateFrameMessage() => OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1*21##");

    private static OpenNettySession CreateSession(OpenNettySessionType type)
    {
        var session = (OpenNettySession) RuntimeHelpers.GetUninitializedObject(typeof(OpenNettySession));
        GetSessionType(session) = type;
        GetSessionId(session) = Guid.NewGuid();

        return session;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_type")]
    private static extern ref OpenNettySessionType GetSessionType(OpenNettySession session);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<Id>k__BackingField")]
    private static extern ref Guid GetSessionId(OpenNettySession session);

    private static OpenNettyEndpoint CreateEndpoint(string name = "Endpoint", string? mqttTopic = null, string? deviceClass = null)
    {
        var settings = ImmutableDictionary<OpenNettySetting, string>.Empty;

        if (mqttTopic is not null)
        {
            settings = settings.Add(OpenNettySettings.MqttTopic, mqttTopic);
        }

        if (deviceClass is not null)
        {
            settings = settings.Add(OpenNettySettings.HomeAssistantScenarioDeviceClass, deviceClass);
        }

        return new OpenNettyEndpoint
        {
            Name = name,
            Protocol = OpenNettyProtocol.Scs,
            Settings = settings
        };
    }

    private static MqttApplicationMessage CreateMessage(string topic, string payload)
        => new() { Topic = topic, PayloadSegment = Encoding.UTF8.GetBytes(payload) };

    private static async Task<T> ReadAsync<T>(ChannelReader<T> reader)
        => await reader.ReadAsync(TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);

    private static Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(static state => ((TaskCompletionSource) state!).TrySetResult(), source);

        return source.Task;
    }

    private sealed record class ReportedCase(
        string Name, Func<OpenNettyEndpoint, OpenNettyEvents.EventArgs> Create, string Attribute, string Payload, bool Retain, bool Json);

    private sealed record class InvalidCase(string Name, Func<OpenNettyEndpoint, OpenNettyEvents.EventArgs> Create);

    private sealed class RecordingObserver<T> : IAsyncObserver<T>
    {
        private readonly Channel<T> _items = Channel.CreateUnbounded<T>();

        public async Task<T> NextAsync() => await _items.Reader.ReadAsync(TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);

        public ValueTask OnNextAsync(T value)
        {
            _items.Writer.TryWrite(value);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnErrorAsync(Exception error) => ValueTask.CompletedTask;

        public ValueTask OnCompletedAsync() => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CallLog
    {
        private readonly ConcurrentQueue<string> _entries = [];

        public IReadOnlyList<string> Entries => [.. _entries];

        public void Add(string entry) => _entries.Enqueue(entry);
    }

    private sealed record class LogEntry(LogLevel Level, int EventId, Exception? Exception, string Message);

    private sealed class RecordingLogger : ILogger<OpenNettyMqttHostedService>
    {
        private readonly ConcurrentQueue<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries => [.. _entries];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _entries.Enqueue(new LogEntry(logLevel, eventId.Id, exception, formatter(state, exception)));
    }

    private sealed record class ProcessCall(
        IManagedMqttClient Client, ChannelReader<MqttApplicationMessage> Reader, CancellationToken CancellationToken);

    private sealed record class AnnounceCall(IManagedMqttClient Client, CancellationToken CancellationToken);

    private sealed class RecordingWorker(CallLog log) : IOpenNettyMqttWorker
    {
        private readonly TaskCompletionSource<AnnounceCall> _announced = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ProcessCall> _processed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<AnnounceCall, Task>? AnnounceBehavior { get; set; }

        public Func<ProcessCall, Task>? ProcessBehavior { get; set; }

        public Task<AnnounceCall> NextAnnounceCallAsync()
            => _announced.Task.WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);

        public Task<ProcessCall> NextProcessCallAsync()
            => _processed.Task.WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);

        public Task AnnounceDevicesAsync(IManagedMqttClient client, CancellationToken cancellationToken)
        {
            var call = new AnnounceCall(client, cancellationToken);
            log.Add("announce");
            _announced.TrySetResult(call);

            return AnnounceBehavior?.Invoke(call) ?? WaitForCancellationAsync(cancellationToken);
        }

        public Task ProcessMessagesAsync(
            IManagedMqttClient client, ChannelReader<MqttApplicationMessage> reader, CancellationToken cancellationToken)
        {
            var call = new ProcessCall(client, reader, cancellationToken);
            log.Add("process");
            _processed.TrySetResult(call);

            return ProcessBehavior?.Invoke(call) ?? WaitForCancellationAsync(cancellationToken);
        }
    }

    private sealed class RecordingClient(CallLog log) : IManagedMqttClient
    {
        private readonly Channel<MqttApplicationMessage> _enqueued = Channel.CreateUnbounded<MqttApplicationMessage>();
        private readonly ConcurrentQueue<bool> _stopRequests = [];
        private readonly ConcurrentQueue<MqttTopicFilter> _subscriptions = [];
        private Exception? _enqueueFailure;

        public event Func<ApplicationMessageProcessedEventArgs, Task>? ApplicationMessageProcessedAsync;

        public event Func<MqttApplicationMessageReceivedEventArgs, Task>? ApplicationMessageReceivedAsync;

        public event Func<ConnectingFailedEventArgs, Task>? ConnectingFailedAsync;

        public event Func<ApplicationMessageSkippedEventArgs, Task> ApplicationMessageSkippedAsync { add { } remove { } }

        public event Func<MqttClientConnectedEventArgs, Task> ConnectedAsync { add { } remove { } }

        public event Func<EventArgs, Task> ConnectionStateChangedAsync { add { } remove { } }

        public event Func<MqttClientDisconnectedEventArgs, Task> DisconnectedAsync { add { } remove { } }

        public event Func<ManagedProcessFailedEventArgs, Task> SynchronizingSubscriptionsFailedAsync { add { } remove { } }

        public event Func<SubscriptionsChangedEventArgs, Task> SubscriptionsChangedAsync { add { } remove { } }

        public IMqttClient InternalClient => throw new NotSupportedException();

        public bool IsConnected => throw new NotSupportedException();

        public bool IsStarted => throw new NotSupportedException();

        public ManagedMqttClientOptions Options => throw new NotSupportedException();

        public int PendingApplicationMessagesCount => throw new NotSupportedException();

        public Exception? StartFailure { get; set; }

        public Exception? StopFailure { get; set; }

        public Exception? SubscribeFailure { get; set; }

        public ManagedMqttClientOptions? StartedOptions { get; private set; }

        public IReadOnlyList<bool> StopRequests => [.. _stopRequests];

        public IReadOnlyList<MqttTopicFilter> Subscriptions => [.. _subscriptions];

        public void FailNextEnqueue(Exception exception) => _enqueueFailure = exception;

        public async Task<MqttApplicationMessage> NextEnqueuedAsync() => await _enqueued.Reader
            .ReadAsync(TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(Timeout, TimeProvider.System, TestContext.Current.CancellationToken);

        public bool TryTakeEnqueued(out MqttApplicationMessage? message) => _enqueued.Reader.TryRead(out message);

        public Task RaiseConnectingFailedAsync(Exception exception)
            => ConnectingFailedAsync!.Invoke(new ConnectingFailedEventArgs(null, exception));

        public Task RaiseProcessedAsync(MqttApplicationMessage message, Exception? exception)
            => ApplicationMessageProcessedAsync!.Invoke(new ApplicationMessageProcessedEventArgs(
                new ManagedMqttApplicationMessageBuilder().WithApplicationMessage(message).Build(), exception));

        public Task RaiseReceivedAsync(MqttApplicationMessage message)
            => ApplicationMessageReceivedAsync!.Invoke(new MqttApplicationMessageReceivedEventArgs(
                "client", message, new MqttPublishPacket(), static (_, _) => Task.CompletedTask));

        public Task EnqueueAsync(MqttApplicationMessage applicationMessage)
        {
            if (Interlocked.Exchange(ref _enqueueFailure, null) is Exception exception)
            {
                return Task.FromException(exception);
            }

            _enqueued.Writer.TryWrite(applicationMessage);

            return Task.CompletedTask;
        }

        public Task EnqueueAsync(ManagedMqttApplicationMessage applicationMessage) => throw new NotSupportedException();

        public Task PingAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task StartAsync(ManagedMqttClientOptions options)
        {
            log.Add("start");
            StartedOptions = options;

            if (StartFailure is Exception exception)
            {
                await Task.Yield();
                throw exception;
            }
        }

        public async Task StopAsync(bool cleanDisconnect = true)
        {
            log.Add("stop");
            _stopRequests.Enqueue(cleanDisconnect);

            if (StopFailure is Exception exception)
            {
                await Task.Yield();
                throw exception;
            }
        }

        public async Task SubscribeAsync(IEnumerable<MqttTopicFilter> topicFilters)
        {
            log.Add("subscribe");

            foreach (var filter in topicFilters)
            {
                _subscriptions.Enqueue(filter);
            }

            if (SubscribeFailure is Exception exception)
            {
                await Task.Yield();
                throw exception;
            }
        }

        public Task UnsubscribeAsync(IEnumerable<string> topics) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class ServiceHarness : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        private IAsyncDisposable? _connection;
        private IAsyncDisposable? _subscription;

        public ServiceHarness(Action<OpenNettyMqttOptions>? configure = null)
        {
            Options = new OpenNettyMqttOptions
            {
                ClientOptions = new MqttClientOptions(),
                RootTopic = "opennetty"
            };

            configure?.Invoke(Options);

            var lifetime = Substitute.For<IHostApplicationLifetime>();
            lifetime.ApplicationStopping.Returns(_stopping.Token);

            var monitor = Substitute.For<IOptionsMonitor<OpenNettyMqttOptions>>();
            monitor.CurrentValue.Returns(Options);

            Client = new RecordingClient(Log);
            Events = new OpenNettyEvents(lifetime);
            Worker = new RecordingWorker(Log);
            Service = new OpenNettyMqttHostedService(Client, Events, Logger, monitor, Worker);
        }

        public RecordingClient Client { get; }

        public OpenNettyEvents Events { get; }

        public CallLog Log { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public OpenNettyMqttOptions Options { get; }

        public OpenNettyMqttHostedService Service { get; }

        public RecordingWorker Worker { get; }

        public Task StartAsync() => Service.StartAsync(TestContext.Current.CancellationToken);

        public Task StopAsync() => Service.StopAsync(TestContext.Current.CancellationToken);

        public async Task SubscribeAsync()
        {
            _subscription = await ((IOpenNettyHandler) Service).SubscribeAsync();
            _connection = await Events.ConnectAsync();
        }

        public async Task UnsubscribeAsync()
        {
            await _subscription!.DisposeAsync();
            _subscription = null;
        }

        public async Task PublishAsync(OpenNettyEvents.EventArgs arguments)
            => await Events.PublishAsync(arguments, TestContext.Current.CancellationToken);

        public async Task<MqttApplicationMessage> ReportAsync(OpenNettyEvents.EventArgs arguments)
        {
            await PublishAsync(arguments);

            return await Client.NextEnqueuedAsync();
        }

        public async Task<IReadOnlyList<MqttApplicationMessage>> ReportAsync(OpenNettyEvents.EventArgs arguments, int count)
        {
            await PublishAsync(arguments);

            List<MqttApplicationMessage> messages = [];

            for (var index = 0; index < count; index++)
            {
                messages.Add(await Client.NextEnqueuedAsync());
            }

            return messages;
        }

        public async Task AssertNotReportedAsync(OpenNettyEvents.EventArgs arguments)
        {
            await PublishAsync(arguments);
            await AssertNoMoreReportsAsync();
        }

        public async Task AssertNoMoreReportsAsync()
        {
            await PublishAsync(new OpenNettyEvents.BatteryAlertReportedEventArgs(CreateEndpoint("Sentinel")));

            var message = await Client.NextEnqueuedAsync();

            Assert.Equal("opennetty/sentinel/battery_alert", message.Topic);
        }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();

            if (_subscription is not null)
            {
                await _subscription.DisposeAsync();
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            await Service.StopAsync(CancellationToken.None);
            Service.Dispose();
            await Events.DisposeAsync();
            _stopping.Dispose();
        }
    }
}
