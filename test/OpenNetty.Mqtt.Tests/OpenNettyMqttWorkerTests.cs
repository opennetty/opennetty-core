/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Buffers.Text;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Hashing;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using NSubstitute;
using NSubstitute.Core;
using Xunit;

namespace OpenNetty.Mqtt.Tests;

public sealed class OpenNettyMqttWorkerTests
{
    private const string BarrierEndpointName = "barrier";
    private const string BarrierTopic = "barrier";
    private const string CoverDiscriminator = "9b138d62-bb0d-49cb-8624-1d85f9e86a6e";
    private const string EventDiscriminator = "7faeaa9a-ae51-43f4-af82-65d2e24e14d0";
    private const string LightDiscriminator = "feb44223-4814-4652-933c-53dbbaabac3f";

    private static readonly TimeSpan BarrierRetryInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData("controller")]
    [InlineData("logger")]
    [InlineData("manager")]
    [InlineData("options")]
    public void Constructor_NullDependency_ThrowsArgumentNullException(string parameterName)
    {
        var manager = new WorkerManager(new OpenNettyOptions());

        var exception = Assert.Throws<ArgumentNullException>(() => new OpenNettyMqttWorker(
            parameterName is "controller" ? null! : Substitute.For<OpenNettyController>(manager, Substitute.For<IOpenNettyService>()),
            parameterName is "logger" ? null! : new WorkerLogger(),
            parameterName is "manager" ? null! : manager,
            parameterName is "options" ? null! : Substitute.For<IOptionsMonitor<OpenNettyMqttOptions>>()));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void Constructor_ValidDependencies_CreatesWorker()
    {
        var manager = new WorkerManager(new OpenNettyOptions());

        var worker = new OpenNettyMqttWorker(
            Substitute.For<OpenNettyController>(manager, Substitute.For<IOpenNettyService>()),
            new WorkerLogger(),
            manager,
            Substitute.For<IOptionsMonitor<OpenNettyMqttOptions>>());

        Assert.IsAssignableFrom<IOpenNettyMqttWorker>(worker);
    }

    [Theory]
    [InlineData("opennetty/kitchen/switch_state")]
    [InlineData("opennetty/kitchen/switch_state/delete")]
    [InlineData("opennetty/kitchen/switch_state/SET")]
    [InlineData("opennetty/kitchen/switch_state/Get")]
    [InlineData("opennetty/switch_state/set")]
    [InlineData("switch_state/set")]
    [InlineData("opennetty")]
    public async Task ProcessMessagesAsync_InvalidTopic_IgnoresMessage(string topic)
    {
        await using var harness = new WorkerHarness().Start();
        harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage(topic, "on", responseTopic: "reply"));

        Assert.Empty(harness.ControllerCalls);
        Assert.Empty(harness.Published.Drain());
    }

    [Fact]
    public async Task ProcessMessagesAsync_RootTopic_IsNotValidated()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage("another-root/kitchen/switch_state/set", "on"));

        await harness.Controller.Received(1).SwitchOnAsync(endpoint, null, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("opennetty//kitchen/switch_state/set")]
    [InlineData("opennetty/kitchen/switch_state/set/")]
    [InlineData("/opennetty/kitchen/switch_state/set")]
    public async Task ProcessMessagesAsync_TopicWithEmptySegments_IgnoresEmptySegments(string topic)
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");

        harness.Post(CreateMessage(topic, "on", responseTopic: "reply"));
        await harness.Published.WaitAsync(1);

        await harness.Controller.Received(1).SwitchOnAsync(endpoint, null, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Kitchen", null, "opennetty/kitchen/switch_state/set")]
    [InlineData("Living Room", "lights/living-room", "opennetty/lights/living-room/switch_state/set")]
    [InlineData("Kitchen", "Kitchen", "opennetty/Kitchen/switch_state/set")]
    public async Task ProcessMessagesAsync_MatchingTopic_ExecutesCommand(string name, string? topicSetting, string topic)
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint(name, settings: topicSetting is null ? null : CreateSettings((OpenNettySettings.MqttTopic, topicSetting)));

        await harness.ProcessAsync(CreateMessage(topic, "on"));

        await harness.Controller.Received(1).SwitchOnAsync(endpoint, null, Arg.Any<CancellationToken>());
        Assert.Equal([nameof(OpenNettyController.SwitchOnAsync)], harness.ControllerCalls);
    }

    [Theory]
    [InlineData("Kitchen", "lights/kitchen", "opennetty/kitchen/switch_state/set")]
    [InlineData("Kitchen", null, "opennetty/Kitchen/switch_state/set")]
    [InlineData("Kitchen", null, "opennetty/kitchen/extra/switch_state/set")]
    [InlineData("Kitchen", null, "opennetty/kitch/switch_state/set")]
    [InlineData("Kitchen", null, "opennetty/unknown/switch_state/set")]
    public async Task ProcessMessagesAsync_UnknownTopic_IgnoresMessage(string name, string? topicSetting, string topic)
    {
        await using var harness = new WorkerHarness().Start();
        harness.AddEndpoint(name, settings: topicSetting is null ? null : CreateSettings((OpenNettySettings.MqttTopic, topicSetting)));

        harness.Post(CreateMessage(topic, "on", responseTopic: "reply"));
        await harness.Manager.WaitForLookupAsync();

        Assert.Empty(harness.ControllerCalls);
        Assert.Empty(harness.Published.Drain());
    }

    [Fact]
    public async Task ProcessMessagesAsync_EndpointRegisteredAfterStartup_IsResolvedWhenMessageIsReceived()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Late");

        await harness.ProcessAsync(CreateMessage("opennetty/late/brightness/set", "10"));

        await harness.Controller.Received(1).SetBrightnessAsync(endpoint, 10, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_EndpointsSharingTheSameTopic_ExecutesCommandForEveryEndpoint()
    {
        await using var harness = new WorkerHarness().Start();
        var settings = CreateSettings((OpenNettySettings.MqttTopic, "lights/all"));
        var first = harness.AddEndpoint("First", settings: settings);
        var second = harness.AddEndpoint("Second", settings: settings);

        await harness.ProcessAsync(CreateMessage("opennetty/lights/all/switch_state/set", "off"));

        await harness.Controller.Received(1).SwitchOffAsync(first, Arg.Any<CancellationToken>());
        await harness.Controller.Received(1).SwitchOffAsync(second, Arg.Any<CancellationToken>());
        Assert.Equal(2, harness.ControllerCalls.Count);
    }

    [Fact]
    public async Task ProcessMessagesAsync_MessagesForTheSameEndpoint_AreProcessedInOrder()
    {
        await using var harness = new WorkerHarness().Start();
        harness.AddEndpoint("Kitchen");

        harness.Post(CreateMessage("opennetty/kitchen/switch_state/set", "on"));
        harness.Post(CreateMessage("opennetty/kitchen/switch_state/set", "off"));
        harness.Post(CreateMessage("opennetty/kitchen/switch_state/set", "toggle"));
        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/switch_state/set", "on"));

        Assert.Equal(
        [
            nameof(OpenNettyController.SwitchOnAsync),
            nameof(OpenNettyController.SwitchOffAsync),
            nameof(OpenNettyController.ToggleAsync),
            nameof(OpenNettyController.SwitchOnAsync)
        ], harness.ControllerCalls);
    }

    [Fact]
    public async Task ProcessMessagesAsync_MessagesForDifferentEndpoints_AreProcessedConcurrently()
    {
        await using var harness = new WorkerHarness().Start();
        var first = harness.AddEndpoint("First");
        var second = harness.AddEndpoint("Second");
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Setup(controller => controller.SwitchOnAsync(first, null, Arg.Any<CancellationToken>()),
            async _ => await secondStarted.Task);
        harness.Setup(controller => controller.SwitchOnAsync(second, null, Arg.Any<CancellationToken>()), _ =>
        {
            secondStarted.SetResult();

            return ValueTask.CompletedTask;
        });

        harness.Post(CreateMessage("opennetty/first/switch_state/set", "on", responseTopic: "reply"));
        harness.Post(CreateMessage("opennetty/second/switch_state/set", "on"));
        await harness.Published.WaitAsync(1);

        Assert.Equal(2, harness.ControllerCalls.Count);
    }

    [Fact]
    public async Task ProcessMessagesAsync_EndpointsSharingTheSameTopic_AreProcessedConcurrently()
    {
        await using var harness = new WorkerHarness().Start();
        var settings = CreateSettings((OpenNettySettings.MqttTopic, "lights/all"));
        var first = harness.AddEndpoint("First", settings: settings);
        var second = harness.AddEndpoint("Second", settings: settings);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Setup(controller => controller.SwitchOnAsync(first, null, Arg.Any<CancellationToken>()),
            async _ => await secondStarted.Task);
        harness.Setup(controller => controller.SwitchOnAsync(second, null, Arg.Any<CancellationToken>()), _ =>
        {
            secondStarted.SetResult();

            return ValueTask.CompletedTask;
        });

        harness.Post(CreateMessage("opennetty/lights/all/switch_state/set", "on", responseTopic: "reply"));
        await harness.Published.WaitAsync(2);

        Assert.Equal(2, harness.ControllerCalls.Count);
    }

    [Fact]
    public async Task ProcessMessagesAsync_Cancellation_CancelsRunningCommands()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Setup(controller => controller.SwitchOnAsync(endpoint, null, Arg.Any<CancellationToken>()), async call =>
        {
            using var registration = call.Arg<CancellationToken>().Register(static state => ((TaskCompletionSource) state!).SetResult(), canceled);
            started.SetResult();
            await canceled.Task;
        });

        harness.Post(CreateMessage("opennetty/kitchen/switch_state/set", "on"));
        await started.Task.WaitAsync(WaitTimeout, TimeProvider.System, TestContext.Current.CancellationToken);
        await harness.StopAsync();

        Assert.True(canceled.Task.IsCompletedSuccessfully);
        Assert.True(harness.IsCompleted);
    }

    [Fact]
    public async Task ProcessMessagesAsync_SuccessfulRequestWithResponseTopic_PublishesAcknowledgement()
    {
        await using var harness = new WorkerHarness().Start();
        harness.AddEndpoint("Kitchen");

        harness.Post(CreateMessage("opennetty/kitchen/switch_state/set", "on", responseTopic: "clients/42/reply", correlationData: [4, 5, 6]));
        var response = Assert.Single(await harness.Published.WaitAsync(1));

        Assert.Equal("clients/42/reply", response.Topic);
        Assert.Equal([4, 5, 6], response.CorrelationData);
        Assert.Equal(MqttQualityOfServiceLevel.ExactlyOnce, response.QualityOfServiceLevel);
        Assert.Null(response.ContentType);
        Assert.Equal(0, response.PayloadSegment.Count);
        Assert.False(response.Retain);
    }

    [Fact]
    public async Task ProcessMessagesAsync_SuccessfulRequestWithoutResponseTopic_DoesNotPublishAnything()
    {
        await using var harness = new WorkerHarness().Start();
        harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/switch_state/set", "on"));

        Assert.Empty(harness.Published.Drain());
        Assert.Empty(harness.Logger.Entries.Drain());
    }

    [Fact]
    public async Task ProcessMessagesAsync_OpenNettyExceptionWithResponseTopic_PublishesErrorAndLogsWarning()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        var exception = new OpenNettyException(OpenNettyErrorCode.NoAcknowledgementReceived, "No acknowledgement was received.");
        harness.Setup(controller => controller.SwitchOnAsync(endpoint, null, Arg.Any<CancellationToken>()), exception);

        harness.Post(CreateMessage("opennetty/kitchen/switch_state/set", "on", responseTopic: "reply", correlationData: [7, 8]));
        var response = Assert.Single(await harness.Published.WaitAsync(1));
        var entry = Assert.Single(await harness.Logger.Entries.WaitAsync(1));

        Assert.Equal("reply", response.Topic);
        Assert.Equal([7, 8], response.CorrelationData);
        Assert.Equal("application/json", response.ContentType);
        Assert.Equal(MqttPayloadFormatIndicator.CharacterData, response.PayloadFormatIndicator);
        Assert.Equal(MqttQualityOfServiceLevel.ExactlyOnce, response.QualityOfServiceLevel);
        Assert.False(response.Retain);
        Assert.Equal("No acknowledgement was received.", (string?) JsonNode.Parse(response.ConvertPayloadToString())?["error"]);

        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(6018, entry.EventId.Id);
        Assert.Same(exception, entry.Exception);
    }

    [Fact]
    public async Task ProcessMessagesAsync_OpenNettyExceptionWithoutResponseTopic_LogsWarningOnly()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        var exception = new OpenNettyException(OpenNettyErrorCode.GatewayBusy, "The gateway is busy.");
        harness.Setup(controller => controller.SwitchOnAsync(endpoint, null, Arg.Any<CancellationToken>()), exception);

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/switch_state/set", "on"));

        var entry = Assert.Single(harness.Logger.Entries.Drain());
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(6018, entry.EventId.Id);
        Assert.Same(exception, entry.Exception);
        Assert.Empty(harness.Published.Drain());
    }

    [Fact]
    public async Task ProcessMessagesAsync_UnexpectedExceptionWithResponseTopic_LogsWarningWithoutPublishingError()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        var exception = new InvalidOperationException();
        harness.Setup(controller => controller.SwitchOnAsync(endpoint, null, Arg.Any<CancellationToken>()), exception);

        harness.Post(CreateMessage("opennetty/kitchen/switch_state/set", "on", responseTopic: "reply"));
        var entry = Assert.Single(await harness.Logger.Entries.WaitAsync(1));
        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/switch_state/set", "off"));

        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Same(exception, entry.Exception);
        Assert.Empty(harness.Published.Drain());
    }

    [Fact]
    public async Task ProcessMessagesAsync_FailedCommand_DoesNotStopProcessingFollowingMessagesForTheSameEndpoint()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        harness.Setup(controller => controller.SwitchOnAsync(endpoint, null, Arg.Any<CancellationToken>()),
            new OpenNettyException(OpenNettyErrorCode.GatewayBusy, "The gateway is busy."));

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/switch_state/set", "on"));
        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/switch_state/set", "off"));

        await harness.Controller.Received(1).SwitchOffAsync(endpoint, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_FailedCommand_DoesNotStopProcessingMessagesForOtherEndpoints()
    {
        await using var harness = new WorkerHarness().Start();
        var kitchen = harness.AddEndpoint("Kitchen");
        var bedroom = harness.AddEndpoint("Bedroom");
        harness.Setup(controller => controller.SwitchOnAsync(kitchen, null, Arg.Any<CancellationToken>()),
            new OpenNettyException(OpenNettyErrorCode.GatewayBusy, "The gateway is busy."));

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/switch_state/set", "on"));
        await harness.ProcessAsync(CreateMessage("opennetty/bedroom/switch_state/set", "off"));

        await harness.Controller.Received(1).SwitchOffAsync(bedroom, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Cancellation_PublishesOfflineAvailabilityForEveryEndpoint()
    {
        await using var harness = new WorkerHarness().Start();
        harness.AddEndpoint("Kitchen");
        harness.AddEndpoint("Living Room", settings: CreateSettings((OpenNettySettings.MqttTopic, "lights/living-room")));

        await harness.StopAsync();

        var messages = harness.Published.Drain();

        Assert.Equal(
        [
            "opennetty/barrier/availability",
            "opennetty/kitchen/availability",
            "opennetty/lights/living-room/availability"
        ], messages.Select(static message => message.Topic).Order(StringComparer.Ordinal), StringComparer.Ordinal);

        Assert.All(messages, static message =>
        {
            Assert.Equal("offline", message.ConvertPayloadToString());
            Assert.Equal(MqttQualityOfServiceLevel.ExactlyOnce, message.QualityOfServiceLevel);
            Assert.Equal(MqttPayloadFormatIndicator.CharacterData, message.PayloadFormatIndicator);
        });
    }

    [Fact]
    public async Task ProcessMessagesAsync_Cancellation_UsesConfiguredRootTopicForAvailability()
    {
        await using var harness = new WorkerHarness(static options => options.RootTopic = "home/opennetty").Start();

        await harness.StopAsync();

        var message = Assert.Single(harness.Published.Drain());

        Assert.Equal("home/opennetty/barrier/availability", message.Topic);
    }

    [Fact]
    public async Task ProcessMessagesAsync_Cancellation_RetainsOfflineAvailabilityMessages()
    {
        await using var harness = new WorkerHarness().Start();

        await harness.StopAsync();

        var message = Assert.Single(harness.Published.Drain());

        Assert.True(message.Retain);
    }

    [Fact]
    public async Task ProcessMessagesAsync_Cancellation_WaitsForPendingMessagesToBeSent()
    {
        await using var harness = new WorkerHarness().Start();
        harness.Client.PendingApplicationMessagesCount.Returns(2, 1, 0);

        await harness.StopAsync();

        _ = harness.Client.Received(3).PendingApplicationMessagesCount;
    }

    [Fact]
    public async Task ProcessMessagesAsync_CompletedReader_KeepsRunningUntilCancellation()
    {
        await using var harness = new WorkerHarness().Start();

        harness.Complete();

        Assert.False(harness.IsCompleted);

        await harness.StopAsync();

        Assert.True(harness.IsCompleted);
        Assert.Single(harness.Published.Drain());
    }

    [Fact]
    public async Task ProcessMessagesAsync_ClosedReader_KeepsRunningUntilCancellation()
    {
        await using var harness = new WorkerHarness().Start(new WorkerClosedReader());

        Assert.False(harness.IsCompleted);

        await harness.StopAsync();

        Assert.Single(harness.Published.Drain());
    }

    [Fact]
    public async Task ProcessMessagesAsync_ReaderFailure_ReadsFromTheReaderAgain()
    {
        var reader = new WorkerFailingReader();
        await using var harness = new WorkerHarness().Start(reader);

        await reader.Retried.WaitAsync(WaitTimeout, TimeProvider.System, TestContext.Current.CancellationToken);
        await harness.StopAsync();

        Assert.Single(harness.Published.Drain());
    }

    public static TheoryData<string, string, string, object?[]> SetCommands
    {
        get
        {
            TheoryData<string, string, string, object?[]> data = new()
            {
                { OpenNettyMqttAttributes.ActuatorLock, "lock", nameof(OpenNettyController.LockActuatorAsync), [] },
                { OpenNettyMqttAttributes.ActuatorLock, "unlock", nameof(OpenNettyController.UnlockActuatorAsync), [] },
                { OpenNettyMqttAttributes.Brightness, "0", nameof(OpenNettyController.SetBrightnessAsync), [(byte) 0, null] },
                { OpenNettyMqttAttributes.Brightness, "42", nameof(OpenNettyController.SetBrightnessAsync), [(byte) 42, null] },
                { OpenNettyMqttAttributes.Brightness, "100", nameof(OpenNettyController.SetBrightnessAsync), [(byte) 100, null] },
                { OpenNettyMqttAttributes.Brightness, "255", nameof(OpenNettyController.SetBrightnessAsync), [(byte) 255, null] },
                { OpenNettyMqttAttributes.PilotWireSetpointMode, "comfort", nameof(OpenNettyController.SetPilotWireSetpointModeAsync), [OpenNettyModels.TemperatureControl.PilotWireMode.Comfort, null] },
                { OpenNettyMqttAttributes.PilotWireSetpointMode, "comfort-1", nameof(OpenNettyController.SetPilotWireSetpointModeAsync), [OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne, null] },
                { OpenNettyMqttAttributes.PilotWireSetpointMode, "comfort-2", nameof(OpenNettyController.SetPilotWireSetpointModeAsync), [OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo, null] },
                { OpenNettyMqttAttributes.PilotWireSetpointMode, "eco", nameof(OpenNettyController.SetPilotWireSetpointModeAsync), [OpenNettyModels.TemperatureControl.PilotWireMode.Eco, null] },
                { OpenNettyMqttAttributes.PilotWireSetpointMode, "frost_protection", nameof(OpenNettyController.SetPilotWireSetpointModeAsync), [OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection, null] },
                { OpenNettyMqttAttributes.PilotWireShutdownMode, "on", nameof(OpenNettyController.ActivatePilotWireShutdownModeAsync), [null] },
                { OpenNettyMqttAttributes.PilotWireShutdownMode, "off", nameof(OpenNettyController.CancelPilotWireShutdownModeAsync), [null] },
                { OpenNettyMqttAttributes.ShutterPosition, "0", nameof(OpenNettyController.SetShutterPositionAsync), [(byte) 0] },
                { OpenNettyMqttAttributes.ShutterPosition, "50", nameof(OpenNettyController.SetShutterPositionAsync), [(byte) 50] },
                { OpenNettyMqttAttributes.ShutterPosition, "100", nameof(OpenNettyController.SetShutterPositionAsync), [(byte) 100] },
                { OpenNettyMqttAttributes.ShutterState, "close", nameof(OpenNettyController.MoveShutterDownAsync), [] },
                { OpenNettyMqttAttributes.ShutterState, "open", nameof(OpenNettyController.MoveShutterUpAsync), [] },
                { OpenNettyMqttAttributes.ShutterState, "stop", nameof(OpenNettyController.StopShutterAsync), [] },
                { OpenNettyMqttAttributes.SwitchState, "on", nameof(OpenNettyController.SwitchOnAsync), [null] },
                { OpenNettyMqttAttributes.SwitchState, "off", nameof(OpenNettyController.SwitchOffAsync), [] },
                { OpenNettyMqttAttributes.SwitchState, "toggle", nameof(OpenNettyController.ToggleAsync), [] },
                { OpenNettyMqttAttributes.WaterHeaterSetpointMode, "automatic", nameof(OpenNettyController.SetWaterHeaterSetpointModeAsync), [OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic] },
                { OpenNettyMqttAttributes.WaterHeaterSetpointMode, "forced_off", nameof(OpenNettyController.SetWaterHeaterSetpointModeAsync), [OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOff] },
                { OpenNettyMqttAttributes.WaterHeaterSetpointMode, "forced_on", nameof(OpenNettyController.SetWaterHeaterSetpointModeAsync), [OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn] },
                { OpenNettyMqttAttributes.ZigbeeBinding, "bind", nameof(OpenNettyController.BindZigbeeEndpointAsync), [] },
                { OpenNettyMqttAttributes.ZigbeeBinding, "unbind", nameof(OpenNettyController.UnbindZigbeeEndpointAsync), [] },
                { OpenNettyMqttAttributes.ZigbeeNetwork, "close", nameof(OpenNettyController.CloseZigbeeNetworkAsync), [] },
                { OpenNettyMqttAttributes.ZigbeeNetwork, "create", nameof(OpenNettyController.CreateZigbeeNetworkAsync), [] },
                { OpenNettyMqttAttributes.ZigbeeNetwork, "join", nameof(OpenNettyController.JoinZigbeeNetworkAsync), [] },
                { OpenNettyMqttAttributes.ZigbeeNetwork, "leave", nameof(OpenNettyController.LeaveZigbeeNetworkAsync), [] },
                { OpenNettyMqttAttributes.ZigbeeNetwork, "open", nameof(OpenNettyController.OpenZigbeeNetworkAsync), [] },
                { OpenNettyMqttAttributes.ZigbeeSupervision, "disable", nameof(OpenNettyController.DisableZigbeeSupervisionAsync), [] },
                { OpenNettyMqttAttributes.ZigbeeSupervision, "enable", nameof(OpenNettyController.EnableZigbeeSupervisionAsync), [] }
            };

            return data;
        }
    }

    public static TheoryData<string, string, string, object?[]> PilotWireDerogationCommands
    {
        get
        {
            TheoryData<string, string, string, object?[]> data = new()
            {
                { OpenNettyMqttAttributes.PilotWireDerogationMode, "none", nameof(OpenNettyController.CancelPilotWireDerogationModeAsync), [null] }
            };

            foreach (var (name, mode) in new[]
            {
                ("comfort", OpenNettyModels.TemperatureControl.PilotWireMode.Comfort),
                ("comfort-1", OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne),
                ("comfort-2", OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo),
                ("eco", OpenNettyModels.TemperatureControl.PilotWireMode.Eco),
                ("frost_protection", OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection)
            })
            {
                data.Add(OpenNettyMqttAttributes.PilotWireDerogationMode, name,
                    nameof(OpenNettyController.SetPilotWireDerogationModeAsync), [mode, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, null]);
                data.Add(OpenNettyMqttAttributes.PilotWireDerogationMode, $"{name}:4h",
                    nameof(OpenNettyController.SetPilotWireDerogationModeAsync), [mode, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, null]);
                data.Add(OpenNettyMqttAttributes.PilotWireDerogationMode, $"{name}:8h",
                    nameof(OpenNettyController.SetPilotWireDerogationModeAsync), [mode, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, null]);
            }

            return data;
        }
    }

    public static TheoryData<string, string, string, object?[]> ScenarioCommands => new()
    {
        { OpenNettyMqttAttributes.Scenario, "action", nameof(OpenNettyController.DispatchActionScenarioAsync), [OpenNettyModels.ScenariosPlus.ActionScenarioType.Action] },
        { OpenNettyMqttAttributes.Scenario, """{"event_type":"action"}""", nameof(OpenNettyController.DispatchActionScenarioAsync), [OpenNettyModels.ScenariosPlus.ActionScenarioType.Action] },
        { OpenNettyMqttAttributes.Scenario, "stop_action", nameof(OpenNettyController.DispatchActionScenarioAsync), [OpenNettyModels.ScenariosPlus.ActionScenarioType.StopAction] },
        { OpenNettyMqttAttributes.Scenario, """{"event_type":"dimming","dimming_step":5}""", nameof(OpenNettyController.DispatchDimmingScenarioAsync), [(short) 5] },
        { OpenNettyMqttAttributes.Scenario, """{"event_type":"dimming","dimming_step":-5}""", nameof(OpenNettyController.DispatchDimmingScenarioAsync), [(short) -5] },
        { OpenNettyMqttAttributes.Scenario, "contact_open", nameof(OpenNettyController.DispatchDryContactScenarioAsync), [OpenNettyModels.ScenariosPlus.DryContactScenarioType.Open] },
        { OpenNettyMqttAttributes.Scenario, "contact_closed", nameof(OpenNettyController.DispatchDryContactScenarioAsync), [OpenNettyModels.ScenariosPlus.DryContactScenarioType.Closed] },
        { OpenNettyMqttAttributes.Scenario, "end_of_extended_pressure", nameof(OpenNettyController.DispatchPressureScenarioPlusAsync), [OpenNettyModels.ScenariosPlus.PressureScenarioType.EndOfExtendedPressure, null] },
        { OpenNettyMqttAttributes.Scenario, """{"event_type":"end_of_extended_pressure","button":3}""", nameof(OpenNettyController.DispatchPressureScenarioPlusAsync), [OpenNettyModels.ScenariosPlus.PressureScenarioType.EndOfExtendedPressure, (byte) 3] },
        { OpenNettyMqttAttributes.Scenario, "start_of_extended_pressure", nameof(OpenNettyController.DispatchPressureScenarioPlusAsync), [OpenNettyModels.ScenariosPlus.PressureScenarioType.StartOfExtendedPressure, null] },
        { OpenNettyMqttAttributes.Scenario, """{"event_type":"start_of_extended_pressure","button":2}""", nameof(OpenNettyController.DispatchPressureScenarioPlusAsync), [OpenNettyModels.ScenariosPlus.PressureScenarioType.StartOfExtendedPressure, (byte) 2] },
        { OpenNettyMqttAttributes.Scenario, """{"event_type":"extended_pressure","scenario_type":"evolved","button":4}""", nameof(OpenNettyController.DispatchPressureScenarioAsync), [OpenNettyModels.Scenarios.PressureScenarioType.ExtendedPressure, (byte) 4] },
        { OpenNettyMqttAttributes.Scenario, """{"event_type":"extended_pressure","scenario_type":"plus","button":4}""", nameof(OpenNettyController.DispatchPressureScenarioPlusAsync), [OpenNettyModels.ScenariosPlus.PressureScenarioType.ExtendedPressure, (byte) 4] },
        { OpenNettyMqttAttributes.Scenario, """{"event_type":"extended_pressure","scenario_type":"plus"}""", nameof(OpenNettyController.DispatchPressureScenarioPlusAsync), [OpenNettyModels.ScenariosPlus.PressureScenarioType.ExtendedPressure, null] },
        { OpenNettyMqttAttributes.Scenario, """{"event_type":"pressure","button":1}""", nameof(OpenNettyController.DispatchPressureScenarioAsync), [OpenNettyModels.Scenarios.PressureScenarioType.Pressure, (byte) 1] },
        { OpenNettyMqttAttributes.Scenario, """{"event_type":"progressive_action","duration":"00:00:05"}""", nameof(OpenNettyController.DispatchProgressiveScenarioAsync), [TimeSpan.FromSeconds(5)] },
        { OpenNettyMqttAttributes.Scenario, """{"event_type":"release_after_short_pressure","button":6}""", nameof(OpenNettyController.DispatchPressureScenarioAsync), [OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterShortPressure, (byte) 6] },
        { OpenNettyMqttAttributes.Scenario, """{"event_type":"release_after_extended_pressure","button":7}""", nameof(OpenNettyController.DispatchPressureScenarioAsync), [OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterExtendedPressure, (byte) 7] },
        { OpenNettyMqttAttributes.Scenario, """{"event_type":"short_pressure","button":8}""", nameof(OpenNettyController.DispatchPressureScenarioPlusAsync), [OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure, (byte) 8] },
        { OpenNettyMqttAttributes.Scenario, "short_pressure", nameof(OpenNettyController.DispatchPressureScenarioPlusAsync), [OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure, null] },
        { OpenNettyMqttAttributes.Scenario, "shutter_down", nameof(OpenNettyController.DispatchStopUpDownScenarioAsync), [OpenNettyModels.Automation.StopUpDownScenarioType.Down] },
        { OpenNettyMqttAttributes.Scenario, "shutter_stop", nameof(OpenNettyController.DispatchStopUpDownScenarioAsync), [OpenNettyModels.Automation.StopUpDownScenarioType.Stop] },
        { OpenNettyMqttAttributes.Scenario, "shutter_up", nameof(OpenNettyController.DispatchStopUpDownScenarioAsync), [OpenNettyModels.Automation.StopUpDownScenarioType.Up] },
        { OpenNettyMqttAttributes.Scenario, "switch_on", nameof(OpenNettyController.DispatchOnOffScenarioAsync), [OpenNettyModels.Lighting.OnOffScenarioType.On] },
        { OpenNettyMqttAttributes.Scenario, "switch_off", nameof(OpenNettyController.DispatchOnOffScenarioAsync), [OpenNettyModels.Lighting.OnOffScenarioType.Off] },
        { OpenNettyMqttAttributes.Scenario, "SWITCH_ON", nameof(OpenNettyController.DispatchOnOffScenarioAsync), [OpenNettyModels.Lighting.OnOffScenarioType.On] },
        { OpenNettyMqttAttributes.Scenario, """{"event_type":"timed_action","duration":"00:01:30"}""", nameof(OpenNettyController.DispatchTimedScenarioAsync), [new TimeSpan(0, 1, 30)] }
    };

    [Theory]
    [MemberData(nameof(SetCommands))]
    [MemberData(nameof(PilotWireDerogationCommands))]
    [MemberData(nameof(ScenarioCommands))]
    public async Task ProcessMessagesAsync_SetCommand_ExecutesControllerOperation(
        string attribute, string payload, string method, object?[] arguments)
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage($"opennetty/kitchen/{attribute}/set", payload));

        AssertSingleCall(harness, endpoint, method, arguments);
        Assert.Empty(harness.Published.Drain());
        Assert.Empty(harness.Logger.Entries.Drain());
    }

    [Theory]
    [InlineData(OpenNettyMqttAttributes.ActuatorLock, "toggle")]
    [InlineData(OpenNettyMqttAttributes.ActuatorLock, null)]
    [InlineData(OpenNettyMqttAttributes.BatteryAlert, "on")]
    [InlineData(OpenNettyMqttAttributes.PilotWireDerogationMode, "comfort:2h")]
    [InlineData(OpenNettyMqttAttributes.PilotWireDerogationMode, "unknown")]
    [InlineData(OpenNettyMqttAttributes.PilotWireSetpointMode, "none")]
    [InlineData(OpenNettyMqttAttributes.PilotWireSetpointMode, "comfort:4h")]
    [InlineData(OpenNettyMqttAttributes.PilotWireShutdownMode, "toggle")]
    [InlineData(OpenNettyMqttAttributes.Scenario, "unknown_event")]
    [InlineData(OpenNettyMqttAttributes.Scenario, """{"event_type":"unknown_event"}""")]
    [InlineData(OpenNettyMqttAttributes.Scenario, """{"state":"action"}""")]
    [InlineData(OpenNettyMqttAttributes.Scenario, "extended_pressure")]
    [InlineData(OpenNettyMqttAttributes.Scenario, """{"event_type":"extended_pressure"}""")]
    [InlineData(OpenNettyMqttAttributes.Scenario, """{"event_type":"extended_pressure","scenario_type":"basic","button":1}""")]
    [InlineData(OpenNettyMqttAttributes.ShutterState, "pause")]
    [InlineData(OpenNettyMqttAttributes.ShutterState, null)]
    [InlineData(OpenNettyMqttAttributes.SwitchState, "blink")]
    [InlineData(OpenNettyMqttAttributes.SwitchState, null)]
    [InlineData(OpenNettyMqttAttributes.WaterHeaterSetpointMode, "eco")]
    [InlineData(OpenNettyMqttAttributes.ZigbeeBinding, "rebind")]
    [InlineData(OpenNettyMqttAttributes.ZigbeeNetwork, "destroy")]
    [InlineData(OpenNettyMqttAttributes.ZigbeeSupervision, "maybe")]
    public async Task ProcessMessagesAsync_SetCommandWithUnsupportedPayload_IgnoresMessage(string attribute, string? payload)
    {
        await using var harness = new WorkerHarness().Start();
        harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage($"opennetty/kitchen/{attribute}/set", payload));

        Assert.Empty(harness.ControllerCalls);
        Assert.Empty(harness.Published.Drain());
        Assert.Empty(harness.Logger.Entries.Drain());
    }

    [Theory]
    [InlineData("off")]
    [InlineData("OFF")]
    public async Task ProcessMessagesAsync_SetBatteryAlertToOff_ReportsRetainedOffState(string payload)
    {
        await using var harness = new WorkerHarness().Start();
        harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/battery_alert/set", payload));

        AssertReport(Assert.Single(harness.Published.Drain()), "opennetty/kitchen/battery_alert", "OFF");
        Assert.Empty(harness.ControllerCalls);
    }

    public static TheoryData<string, string> GetCommands => new()
    {
        { OpenNettyMqttAttributes.Brightness, nameof(OpenNettyController.EnumerateBrightnessAsync) },
        { OpenNettyMqttAttributes.DryContactState, nameof(OpenNettyController.GetDryContactStateAsync) },
        { OpenNettyMqttAttributes.PilotWireDerogationMode, nameof(OpenNettyController.GetPilotWireConfigurationAsync) },
        { OpenNettyMqttAttributes.PilotWireSetpointMode, nameof(OpenNettyController.GetPilotWireConfigurationAsync) },
        { OpenNettyMqttAttributes.PilotWireShutdownMode, nameof(OpenNettyController.GetPilotWireConfigurationAsync) },
        { OpenNettyMqttAttributes.ShutterPosition, nameof(OpenNettyController.EnumerateShutterPositionsAsync) },
        { OpenNettyMqttAttributes.ShutterState, nameof(OpenNettyController.EnumerateShutterStatesAsync) },
        { OpenNettyMqttAttributes.SmartMeterPowerCutMode, nameof(OpenNettyController.GetSmartMeterInformationAsync) },
        { OpenNettyMqttAttributes.SmartMeterRateType, nameof(OpenNettyController.GetSmartMeterInformationAsync) },
        { OpenNettyMqttAttributes.SwitchState, nameof(OpenNettyController.EnumerateSwitchStatesAsync) },
        { OpenNettyMqttAttributes.WaterHeaterState, nameof(OpenNettyController.GetWaterHeaterStateAsync) }
    };

    [Theory]
    [MemberData(nameof(GetCommands))]
    public async Task ProcessMessagesAsync_GetCommand_QueriesControllerWithoutReportingTheResult(string attribute, string method)
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        var barrier = attribute is OpenNettyMqttAttributes.WaterHeaterState
            ? OpenNettyMqttAttributes.DryContactState
            : OpenNettyMqttAttributes.WaterHeaterState;

        await harness.ProcessAsync(CreateMessage($"opennetty/kitchen/{attribute}/get"), barrier);

        AssertSingleCall(harness, endpoint, method);
        Assert.Empty(harness.Published.Drain());
    }

    [Theory]
    [InlineData(OpenNettyMqttAttributes.Brightness, "")]
    [InlineData(OpenNettyMqttAttributes.Brightness, "abc")]
    [InlineData(OpenNettyMqttAttributes.Brightness, "256")]
    [InlineData(OpenNettyMqttAttributes.Brightness, "-1")]
    [InlineData(OpenNettyMqttAttributes.Brightness, "4.5")]
    [InlineData(OpenNettyMqttAttributes.Brightness, "1e2")]
    [InlineData(OpenNettyMqttAttributes.ShutterPosition, "")]
    [InlineData(OpenNettyMqttAttributes.ShutterPosition, "open")]
    [InlineData(OpenNettyMqttAttributes.ShutterPosition, "256")]
    [InlineData(OpenNettyMqttAttributes.ShutterPosition, "-10")]
    [InlineData(OpenNettyMqttAttributes.Scenario, """{"event_type":"dimming"}""")]
    [InlineData(OpenNettyMqttAttributes.Scenario, """{"event_type":"pressure"}""")]
    [InlineData(OpenNettyMqttAttributes.Scenario, """{"event_type":"release_after_short_pressure"}""")]
    [InlineData(OpenNettyMqttAttributes.Scenario, """{"event_type":"release_after_extended_pressure"}""")]
    [InlineData(OpenNettyMqttAttributes.Scenario, """{"event_type":"extended_pressure","scenario_type":"evolved"}""")]
    [InlineData(OpenNettyMqttAttributes.Scenario, """{"event_type":"progressive_action"}""")]
    [InlineData(OpenNettyMqttAttributes.Scenario, """{"event_type":"progressive_action","duration":"not a duration"}""")]
    [InlineData(OpenNettyMqttAttributes.Scenario, """{"event_type":"timed_action"}""")]
    [InlineData(OpenNettyMqttAttributes.Scenario, """{"event_type":"timed_action","duration":"not a duration"}""")]
    public async Task ProcessMessagesAsync_SetCommandWithInvalidPayload_LogsInvalidDataWithoutExecutingCommand(string attribute, string payload)
    {
        await using var harness = new WorkerHarness().Start();
        harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage($"opennetty/kitchen/{attribute}/set", payload));

        AssertInvalidDataLogged(harness);
        Assert.Empty(harness.ControllerCalls);
        Assert.Empty(harness.Published.Drain());
    }

    [Fact]
    public async Task ProcessMessagesAsync_GetFirmwareVersion_ReportsRetainedVersion()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        harness.Setup(controller => controller.GetFirmwareVersionAsync(endpoint, Arg.Any<CancellationToken>()), new Version(1, 2, 3));

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/firmware_version/get"));

        AssertReport(Assert.Single(harness.Published.Drain()), "opennetty/kitchen/firmware_version", "1.2.3");
        AssertSingleCall(harness, endpoint, nameof(OpenNettyController.GetFirmwareVersionAsync));
    }

    [Fact]
    public async Task ProcessMessagesAsync_GetHardwareVersion_ReportsRetainedVersion()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        harness.Setup(controller => controller.GetHardwareVersionAsync(endpoint, Arg.Any<CancellationToken>()), new Version(4, 5));

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/hardware_version/get"));

        AssertReport(Assert.Single(harness.Published.Drain()), "opennetty/kitchen/hardware_version", "4.5");
        AssertSingleCall(harness, endpoint, nameof(OpenNettyController.GetHardwareVersionAsync));
    }

    [Fact]
    public async Task ProcessMessagesAsync_GetMacAddress_ReportsRetainedAddress()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        harness.Setup(controller => controller.GetMacAddressAsync(endpoint, Arg.Any<CancellationToken>()), "00:04:74:00:12:34");

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/mac_address/get"));

        AssertReport(Assert.Single(harness.Published.Drain()), "opennetty/kitchen/mac_address", "00:04:74:00:12:34");
        AssertSingleCall(harness, endpoint, nameof(OpenNettyController.GetMacAddressAsync));
    }

    [Theory]
    [InlineData(OpenNettyMedium.Bus, "bus")]
    [InlineData(OpenNettyMedium.Infrared, "infrared")]
    [InlineData(OpenNettyMedium.Powerline, "powerline")]
    [InlineData(OpenNettyMedium.Radio, "radio")]
    public async Task ProcessMessagesAsync_GetMemoryData_ReportsRetainedJsonArray(OpenNettyMedium medium, string name)
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        harness.Setup(controller => controller.GetMemoryDataAsync(endpoint, Arg.Any<CancellationToken>()),
        [
            new OpenNettyModels.Diagnostics.MemoryData { Address = OpenNettyAddress.FromNitooAddress(1000, 2), FunctionCode = 5, Medium = medium },
            new OpenNettyModels.Diagnostics.MemoryData { Address = OpenNettyAddress.FromNitooAddress(2000, 1), FunctionCode = 12, Medium = medium }
        ]);

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/memory_data/get"));

        AssertReport(Assert.Single(harness.Published.Drain()), "opennetty/kitchen/memory_data",
            $$"""[{"address":{"type":"nitoo","identifier":1000,"unit":2},"function_code":5,"medium":"{{name}}"},""" +
            $$"""{"address":{"type":"nitoo","identifier":2000,"unit":1},"function_code":12,"medium":"{{name}}"}]""",
            "application/json");
        AssertSingleCall(harness, endpoint, nameof(OpenNettyController.GetMemoryDataAsync));
    }

    [Fact]
    public async Task ProcessMessagesAsync_GetMemoryDataWithoutEntries_ReportsEmptyJsonArray()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        harness.Setup(controller => controller.GetMemoryDataAsync(endpoint, Arg.Any<CancellationToken>()), []);

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/memory_data/get"));

        AssertReport(Assert.Single(harness.Published.Drain()), "opennetty/kitchen/memory_data", "[]", "application/json");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessMessagesAsync_GetMemoryDataWithUnsupportedValue_LogsInvalidDataWithoutReporting(bool invalidMedium)
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        harness.Setup(controller => controller.GetMemoryDataAsync(endpoint, Arg.Any<CancellationToken>()),
        [
            new OpenNettyModels.Diagnostics.MemoryData
            {
                Address = invalidMedium ? OpenNettyAddress.FromNitooAddress(1000, 2) : OpenNettyAddress.FromScsDryContactAddress(1),
                FunctionCode = 5,
                Medium = invalidMedium ? (OpenNettyMedium) 42 : OpenNettyMedium.Bus
            }
        ]);

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/memory_data/get"));

        AssertInvalidDataLogged(harness);
        Assert.Empty(harness.Published.Drain());
    }

    [Theory]
    [InlineData(OpenNettyMqttAttributes.MemoryData)]
    [InlineData(OpenNettyMqttAttributes.OutgoingMessage)]
    [InlineData(OpenNettyMqttAttributes.Scenario)]
    public async Task ProcessMessagesAsync_SetCommandWithEmptyPayload_LogsInvalidData(string attribute)
    {
        await using var harness = new WorkerHarness().Start();
        harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage($"opennetty/kitchen/{attribute}/set"));

        AssertInvalidDataLogged(harness);
        Assert.Empty(harness.ControllerCalls);
    }

    [Theory]
    [InlineData("bus", OpenNettyMedium.Bus)]
    [InlineData("BUS", OpenNettyMedium.Bus)]
    [InlineData("infrared", OpenNettyMedium.Infrared)]
    [InlineData("Powerline", OpenNettyMedium.Powerline)]
    [InlineData("radio", OpenNettyMedium.Radio)]
    public async Task ProcessMessagesAsync_SetMemoryData_ResetsThenAddsEveryEntry(string medium, OpenNettyMedium expected)
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/memory_data/set",
            $$"""
            [
                {"address":{"type":"nitoo","identifier":1000,"unit":2},"function_code":5,"medium":"{{medium}}"},
                {"address":{"type":"nitoo","identifier":2000,"unit":1},"function_code":12,"medium":"bus"}
            ]
            """));

        AssertCalls(harness,
            (nameof(OpenNettyController.ResetMemoryDataAsync), [endpoint]),
            (nameof(OpenNettyController.AddMemoryDataAsync),
                [endpoint, new OpenNettyModels.Diagnostics.MemoryData { Address = OpenNettyAddress.FromNitooAddress(1000, 2), FunctionCode = 5, Medium = expected }]),
            (nameof(OpenNettyController.AddMemoryDataAsync),
                [endpoint, new OpenNettyModels.Diagnostics.MemoryData { Address = OpenNettyAddress.FromNitooAddress(2000, 1), FunctionCode = 12, Medium = OpenNettyMedium.Bus }]));
        Assert.Empty(harness.Logger.Entries.Drain());
    }

    [Fact]
    public async Task ProcessMessagesAsync_SetMemoryDataWithEmptyArray_OnlyResetsMemoryData()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/memory_data/set", "[]"));

        AssertSingleCall(harness, endpoint, nameof(OpenNettyController.ResetMemoryDataAsync));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[{}]")]
    [InlineData("[null]")]
    [InlineData("""[{"address":{"type":"zigbee","identifier":1,"unit":1},"function_code":1,"medium":"bus"}]""")]
    [InlineData("""[{"address":{"unit":1},"function_code":1,"medium":"bus"}]""")]
    [InlineData("""[{"address":{"type":"nitoo","unit":1},"function_code":1,"medium":"bus"}]""")]
    [InlineData("""[{"address":{"type":"nitoo","identifier":1},"function_code":1,"medium":"bus"}]""")]
    [InlineData("""[{"address":{"type":"nitoo","identifier":1,"unit":1},"medium":"bus"}]""")]
    [InlineData("""[{"address":{"type":"nitoo","identifier":1,"unit":1},"function_code":1}]""")]
    [InlineData("""[{"address":{"type":"nitoo","identifier":1,"unit":1},"function_code":1,"medium":"wifi"}]""")]
    [InlineData("""[{"address":{"type":"nitoo","identifier":1,"unit":1},"function_code":1,"medium":"bus"},{"function_code":1}]""")]
    public async Task ProcessMessagesAsync_SetMemoryDataWithInvalidPayload_LogsInvalidDataWithoutChangingMemory(string payload)
    {
        await using var harness = new WorkerHarness().Start();
        harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/memory_data/set", payload));

        AssertInvalidDataLogged(harness);
        Assert.Empty(harness.ControllerCalls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("42")]
    public async Task ProcessMessagesAsync_SetMemoryDataWithNonArrayJson_LogsInvalidData(string payload)
    {
        await using var harness = new WorkerHarness().Start();
        harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/memory_data/set", payload));

        AssertInvalidDataLogged(harness);
        Assert.Empty(harness.ControllerCalls);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task ProcessMessagesAsync_SetOutgoingMessageWithRawFrame_SendsMessageUsingTheEndpointProtocol(OpenNettyProtocol protocol)
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen", protocol: protocol);

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/outgoing_message/set", """{"message":{"raw":"*#*1##"}}"""));

        AssertSingleCall(harness, endpoint, nameof(OpenNettyController.SendRawMessageAsync),
            OpenNettyMessage.CreateFromFrame(protocol, "*#*1##"));
    }

    [Fact]
    public async Task ProcessMessagesAsync_SetOutgoingMessageWithRawBusCommand_SendsParsedMessage()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/outgoing_message/set", """{"message":{"raw":"*1*1*21##"}}"""));

        AssertSingleCall(harness, endpoint, nameof(OpenNettyController.SendRawMessageAsync),
            OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1*21##"));
    }

    [Fact]
    public async Task ProcessMessagesAsync_SetOutgoingMessageWithParsedMessage_SendsMessage()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1*21##");

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/outgoing_message/set",
            new JsonObject { ["message"] = new JsonObject { ["parsed"] = message.ToJsonObject() } }.ToJsonString()));

        AssertSingleCall(harness, endpoint, nameof(OpenNettyController.SendRawMessageAsync), message);
    }

    [Fact]
    public async Task ProcessMessagesAsync_SetOutgoingMessageWithEmptyRawFrame_FallsBackToParsedMessage()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/outgoing_message/set",
            """{"message":{"raw":"","parsed":{"protocol":"scs","type":"acknowledgement"}}}"""));

        AssertSingleCall(harness, endpoint, nameof(OpenNettyController.SendRawMessageAsync),
            OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, OpenNettyFrames.Acknowledgement));
    }

    [Fact]
    public async Task ProcessMessagesAsync_SetOutgoingMessageWithRawFrame_IgnoresParsedMessage()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/outgoing_message/set",
            """{"message":{"raw":"*1*1*21##","parsed":{"protocol":"scs","type":"acknowledgement"}}}"""));

        AssertSingleCall(harness, endpoint, nameof(OpenNettyController.SendRawMessageAsync),
            OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1*21##"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"message":{}}""")]
    [InlineData("""{"message":{"raw":""}}""")]
    [InlineData("""{"message":{"parsed":null}}""")]
    public async Task ProcessMessagesAsync_SetOutgoingMessageWithoutMessage_LogsInvalidData(string payload)
    {
        await using var harness = new WorkerHarness().Start();
        harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/outgoing_message/set", payload));

        AssertInvalidDataLogged(harness);
        Assert.Empty(harness.ControllerCalls);
    }

    [Theory]
    [InlineData("""{"message":{"raw":"not a frame"}}""")]
    [InlineData("""{"message":{"parsed":{}}}""")]
    public async Task ProcessMessagesAsync_SetOutgoingMessageWithMalformedMessage_LogsWarningWithoutSendingMessage(string payload)
    {
        await using var harness = new WorkerHarness().Start();
        harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/outgoing_message/set", payload));

        var entry = Assert.Single(harness.Logger.Entries.Drain());
        Assert.Equal(6018, entry.EventId.Id);
        Assert.NotNull(entry.Exception);
        Assert.Empty(harness.ControllerCalls);
    }

    [Theory]
    [InlineData(OpenNettyMqttAttributes.SmartMeterBaseIndex)]
    [InlineData(OpenNettyMqttAttributes.SmartMeterBlueIndex)]
    [InlineData(OpenNettyMqttAttributes.SmartMeterPeakOffPeakIndex)]
    [InlineData(OpenNettyMqttAttributes.SmartMeterRedIndex)]
    [InlineData(OpenNettyMqttAttributes.SmartMeterSubscriptionType)]
    [InlineData(OpenNettyMqttAttributes.SmartMeterWhiteIndex)]
    public async Task ProcessMessagesAsync_GetSmartMeterIndexes_ReportsBaseIndexAndSubscriptionType(string attribute)
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
            harness.Setup(controller => controller.GetSmartMeterIndexesAsync(endpoint, Arg.Any<CancellationToken>()), new OpenNettyModels.TemperatureControl.SmartMeterIndexes
        {
                BaseIndex = new OpenNettyModels.TemperatureControl.SmartMeterIndex { BaseIndex = 12345, OffPeakIndex = 0 },
            BlueIndex = null,
            PeakOffPeakIndex = null,
            RedIndex = null,
                SubscriptionType = OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.Base,
            WhiteIndex = null
        });

        await harness.ProcessAsync(CreateMessage($"opennetty/kitchen/{attribute}/get"));

        var messages = harness.Published.Drain();
        Assert.Equal(2, messages.Count);
        AssertReport(messages[0], "opennetty/kitchen/smart_meter_base_index", """{"base_index":12345}""", "application/json");
        AssertReport(messages[1], "opennetty/kitchen/smart_meter_subscription_type", "base");
        AssertSingleCall(harness, endpoint, nameof(OpenNettyController.GetSmartMeterIndexesAsync));
    }

    [Fact]
    public async Task ProcessMessagesAsync_GetSmartMeterIndexesOfPeakOffPeakSubscription_ReportsPeakOffPeakIndex()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        harness.Setup(controller => controller.GetSmartMeterIndexesAsync(endpoint, Arg.Any<CancellationToken>()),         new OpenNettyModels.TemperatureControl.SmartMeterIndexes
        {
            BaseIndex = null,
            BlueIndex = null,
                    PeakOffPeakIndex = new OpenNettyModels.TemperatureControl.SmartMeterIndex { BaseIndex = 10, OffPeakIndex = 20 },
            RedIndex = null,
                    SubscriptionType = OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.PeakOffPeak,
            WhiteIndex = null
        });

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/smart_meter_peak_off_peak_index/get"));

        var messages = harness.Published.Drain();
        Assert.Equal(2, messages.Count);
        AssertReport(messages[0], "opennetty/kitchen/smart_meter_peak_off_peak_index", """{"base_index":10,"off_peak_index":20}""", "application/json");
        AssertReport(messages[1], "opennetty/kitchen/smart_meter_subscription_type", "peak/off_peak");
    }

    [Fact]
    public async Task ProcessMessagesAsync_GetSmartMeterIndexesOfTempoSubscription_ReportsEveryTempoIndex()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        harness.Setup(controller => controller.GetSmartMeterIndexesAsync(endpoint, Arg.Any<CancellationToken>()),         new OpenNettyModels.TemperatureControl.SmartMeterIndexes
        {
            BaseIndex = null,
                    BlueIndex = new OpenNettyModels.TemperatureControl.SmartMeterIndex { BaseIndex = 1, OffPeakIndex = 2 },
            PeakOffPeakIndex = null,
                    RedIndex = new OpenNettyModels.TemperatureControl.SmartMeterIndex { BaseIndex = 3, OffPeakIndex = 4 },
                    SubscriptionType = OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.Tempo,
                    WhiteIndex = new OpenNettyModels.TemperatureControl.SmartMeterIndex { BaseIndex = 5, OffPeakIndex = 6 }
        });

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/smart_meter_blue_index/get"));

        var messages = harness.Published.Drain();
        Assert.Equal(4, messages.Count);
        AssertReport(messages[0], "opennetty/kitchen/smart_meter_blue_index", """{"base_index":1,"off_peak_index":2}""", "application/json");
        AssertReport(messages[1], "opennetty/kitchen/smart_meter_red_index", """{"base_index":3,"off_peak_index":4}""", "application/json");
        AssertReport(messages[2], "opennetty/kitchen/smart_meter_white_index", """{"base_index":5,"off_peak_index":6}""", "application/json");
        AssertReport(messages[3], "opennetty/kitchen/smart_meter_subscription_type", "tempo");
    }

    [Fact]
    public async Task ProcessMessagesAsync_GetSmartMeterIndexesWithoutIndexes_ReportsSubscriptionTypeOnly()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        harness.Setup(controller => controller.GetSmartMeterIndexesAsync(endpoint, Arg.Any<CancellationToken>()),         new OpenNettyModels.TemperatureControl.SmartMeterIndexes
        {
            BaseIndex = null,
            BlueIndex = null,
            PeakOffPeakIndex = null,
            RedIndex = null,
                    SubscriptionType = OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.Tempo,
            WhiteIndex = null
        });

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/smart_meter_subscription_type/get"));

        AssertReport(Assert.Single(harness.Published.Drain()), "opennetty/kitchen/smart_meter_subscription_type", "tempo");
    }

    [Fact]
    public async Task ProcessMessagesAsync_GetSmartMeterIndexesWithUnknownSubscriptionType_LogsInvalidData()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        harness.Setup(controller => controller.GetSmartMeterIndexesAsync(endpoint, Arg.Any<CancellationToken>()),         new OpenNettyModels.TemperatureControl.SmartMeterIndexes
        {
                    BaseIndex = new OpenNettyModels.TemperatureControl.SmartMeterIndex { BaseIndex = 1, OffPeakIndex = 0 },
            BlueIndex = null,
            PeakOffPeakIndex = null,
            RedIndex = null,
                    SubscriptionType = (OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType) 42,
            WhiteIndex = null
        });

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/smart_meter_base_index/get"));

        AssertInvalidDataLogged(harness);
        AssertReport(Assert.Single(harness.Published.Drain()), "opennetty/kitchen/smart_meter_base_index", """{"base_index":1}""", "application/json");
    }

    [Fact]
    public async Task ProcessMessagesAsync_GetSmartMeterIndexesOfBaseSubscription_ReportsBaseIndexAsJsonObject()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
            harness.Setup(controller => controller.GetSmartMeterIndexesAsync(endpoint, Arg.Any<CancellationToken>()), new OpenNettyModels.TemperatureControl.SmartMeterIndexes
        {
                BaseIndex = new OpenNettyModels.TemperatureControl.SmartMeterIndex { BaseIndex = 12345, OffPeakIndex = 0 },
            BlueIndex = null,
            PeakOffPeakIndex = null,
            RedIndex = null,
                SubscriptionType = OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.Base,
            WhiteIndex = null
        });

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/smart_meter_base_index/get"));

        var message = harness.Published.Drain()[0];
        Assert.Equal(12345, (int?) JsonNode.Parse(message.ConvertPayloadToString())?["base_index"]);
    }

    [Fact]
    public async Task ProcessMessagesAsync_GetStartupDate_ReportsRetainedUtcStartupDate()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        var uptime = TimeSpan.FromHours(2);
        harness.Setup(controller => controller.GetUptimeAsync(endpoint, Arg.Any<CancellationToken>()), uptime);

        var before = TimeProvider.System.GetUtcNow();
        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/startup_date/get"));
        var after = TimeProvider.System.GetUtcNow();

        var message = Assert.Single(harness.Published.Drain());
        AssertReport(message, "opennetty/kitchen/startup_date", message.ConvertPayloadToString());
        var date = DateTimeOffset.ParseExact(message.ConvertPayloadToString(), "o", CultureInfo.InvariantCulture);
        Assert.Equal(TimeSpan.Zero, date.Offset);
        Assert.InRange(date, before - uptime, after - uptime);
    }

    [Fact]
    public async Task ProcessMessagesAsync_PayloadIsMatchedCaseInsensitively()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/switch_state/set", "ToGgLe"));

        await harness.Controller.Received(1).ToggleAsync(endpoint, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_GetZigbeeChannel_ReportsRetainedChannel()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        harness.Setup(controller => controller.GetZigbeeChannelAsync(endpoint, Arg.Any<CancellationToken>()), (byte) 15);

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/zigbee_channel/get"));

        AssertReport(Assert.Single(harness.Published.Drain()), "opennetty/kitchen/zigbee_channel", "15");
        AssertSingleCall(harness, endpoint, nameof(OpenNettyController.GetZigbeeChannelAsync));
    }

    [Fact]
    public async Task ProcessMessagesAsync_GetZigbeeDevicesCount_ReportsRetainedCount()
    {
        await using var harness = new WorkerHarness().Start();
        var endpoint = harness.AddEndpoint("Kitchen");
        harness.Setup(controller => controller.CountZigbeeDevicesAsync(endpoint, Arg.Any<CancellationToken>()), (byte) 7);

        await harness.ProcessAsync(CreateMessage("opennetty/kitchen/zigbee_devices_count/get"));

        AssertReport(Assert.Single(harness.Published.Drain()), "opennetty/kitchen/zigbee_devices_count", "7");
        AssertSingleCall(harness, endpoint, nameof(OpenNettyController.CountZigbeeDevicesAsync));
    }

    public static TheoryData<string, string> UnsupportedOperations
    {
        get
        {
            HashSet<string> get = new(StringComparer.Ordinal)
            {
                OpenNettyMqttAttributes.Brightness, OpenNettyMqttAttributes.DryContactState, OpenNettyMqttAttributes.FirmwareVersion,
                OpenNettyMqttAttributes.HardwareVersion, OpenNettyMqttAttributes.MacAddress, OpenNettyMqttAttributes.MemoryData,
                OpenNettyMqttAttributes.PilotWireDerogationMode, OpenNettyMqttAttributes.PilotWireSetpointMode,
                OpenNettyMqttAttributes.PilotWireShutdownMode, OpenNettyMqttAttributes.ShutterPosition, OpenNettyMqttAttributes.ShutterState,
                OpenNettyMqttAttributes.SmartMeterBaseIndex, OpenNettyMqttAttributes.SmartMeterBlueIndex,
                OpenNettyMqttAttributes.SmartMeterPeakOffPeakIndex, OpenNettyMqttAttributes.SmartMeterPowerCutMode,
                OpenNettyMqttAttributes.SmartMeterRateType, OpenNettyMqttAttributes.SmartMeterRedIndex,
                OpenNettyMqttAttributes.SmartMeterSubscriptionType, OpenNettyMqttAttributes.SmartMeterWhiteIndex,
                OpenNettyMqttAttributes.StartupDate, OpenNettyMqttAttributes.SwitchState, OpenNettyMqttAttributes.WaterHeaterState,
                OpenNettyMqttAttributes.ZigbeeChannel, OpenNettyMqttAttributes.ZigbeeDevicesCount
            };

            HashSet<string> set = new(StringComparer.Ordinal)
            {
                OpenNettyMqttAttributes.ActuatorLock, OpenNettyMqttAttributes.BatteryAlert, OpenNettyMqttAttributes.Brightness,
                OpenNettyMqttAttributes.MemoryData, OpenNettyMqttAttributes.OutgoingMessage, OpenNettyMqttAttributes.PilotWireDerogationMode,
                OpenNettyMqttAttributes.PilotWireSetpointMode, OpenNettyMqttAttributes.PilotWireShutdownMode, OpenNettyMqttAttributes.Scenario,
                OpenNettyMqttAttributes.ShutterPosition, OpenNettyMqttAttributes.ShutterState, OpenNettyMqttAttributes.SwitchState,
                OpenNettyMqttAttributes.WaterHeaterSetpointMode, OpenNettyMqttAttributes.ZigbeeBinding, OpenNettyMqttAttributes.ZigbeeNetwork,
                OpenNettyMqttAttributes.ZigbeeSupervision
            };

            TheoryData<string, string> data = [];

            foreach (var attribute in typeof(OpenNettyMqttAttributes)
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(static member => member.IsLiteral)
                .Select(static member => (string) member.GetRawConstantValue()!))
            {
                if (!get.Contains(attribute))
                {
                    data.Add(attribute, "get");
                }

                if (!set.Contains(attribute))
                {
                    data.Add(attribute, "set");
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(UnsupportedOperations))]
    public async Task ProcessMessagesAsync_UnsupportedAttributeOrOperation_PublishesAcknowledgementWithoutExecutingCommand(string attribute, string operation)
    {
        await using var harness = new WorkerHarness().Start();
        harness.AddEndpoint("Kitchen");
        harness.Post(CreateMessage($"opennetty/kitchen/{attribute}/{operation}", "lock", responseTopic: "reply", correlationData: [1, 2, 3]));

        var response = Assert.Single(await harness.Published.WaitAsync(1));

        Assert.Equal("reply", response.Topic);
        Assert.Equal([1, 2, 3], response.CorrelationData);
        Assert.Empty(harness.ControllerCalls);
    }

    [Fact]
    public async Task ProcessMessagesAsync_UnknownAttribute_PublishesAcknowledgementWithoutExecutingCommand()
    {
        await using var harness = new WorkerHarness().Start();
        harness.AddEndpoint("Kitchen");
        harness.Post(CreateMessage("opennetty/kitchen/unknown/set", "on", responseTopic: "reply"));

        var response = Assert.Single(await harness.Published.WaitAsync(1));

        Assert.Equal("reply", response.Topic);
        Assert.Empty(harness.ControllerCalls);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_DiscoveryDisabled_DoesNotPublishAnything()
    {
        await using var harness = new WorkerHarness(static options => options.DisableHomeAssistantDiscovery = true);
        harness.AddDevice();

        Assert.Empty(await harness.AnnounceAsync());
    }

    [Fact]
    public async Task AnnounceDevicesAsync_NoDevice_DoesNotPublishAnything()
    {
        await using var harness = new WorkerHarness();

        Assert.Empty(await harness.AnnounceAsync());
    }

    [Fact]
    public async Task AnnounceDevicesAsync_Device_PublishesRetainedDiscoveryMessage()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice();

        var message = Assert.Single(await harness.AnnounceAsync());

        Assert.Equal("homeassistant/device/opennetty-scs/kitchen_module/config", message.Topic);
        Assert.True(message.Retain);
        Assert.Equal("application/json", message.ContentType);
        Assert.Equal(MqttPayloadFormatIndicator.CharacterData, message.PayloadFormatIndicator);
        Assert.Equal(MqttQualityOfServiceLevel.ExactlyOnce, message.QualityOfServiceLevel);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_Device_DescribesOriginAndRequestsExactlyOnceDelivery()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice();

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal(
            new JsonObject
            {
                ["name"] = "OpenNetty",
                ["sw_version"] = typeof(OpenNettyMqttWorker).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                ["support_url"] = "https://github.com/opennetty/opennetty-core"
            }.ToJsonString(),
            payload["origin"]?.ToJsonString());
        Assert.Equal(2, (int?) payload["qos"]);
        Assert.Equal(
        [
            "origin",
            "device",
            "qos",
            "components",
            "child_devices"
        ], payload.Select(static property => property.Key), StringComparer.Ordinal);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_DeviceWithoutEndpoint_PublishesEmptyComponentsAndChildDevices()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice();

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Empty(payload["components"]!.AsObject());
        Assert.Empty(payload["child_devices"]!.AsArray());
    }

    [Fact]
    public async Task AnnounceDevicesAsync_CustomDiscoveryRootTopic_UsesRootTopicInDiscoveryTopic()
    {
        await using var harness = new WorkerHarness(static options => options.HomeAssistantDiscoveryRootTopic = "custom/discovery");
        harness.AddDevice();

        var message = Assert.Single(await harness.AnnounceAsync());

        Assert.Equal("custom/discovery/device/opennetty-scs/kitchen_module/config", message.Topic);
    }

    [Theory]
    [InlineData(OpenNettyBrand.BTicino, "F411U1", "opennetty-scs")]
    [InlineData(OpenNettyBrand.Legrand, "03600", "opennetty-nitoo")]
    [InlineData(OpenNettyBrand.BTicino, "3578", "opennetty-zigbee")]
    public async Task AnnounceDevicesAsync_DeviceProtocol_UsesProtocolInDiscoveryTopic(OpenNettyBrand brand, string model, string component)
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice(brand, model);

        var message = Assert.Single(await harness.AnnounceAsync());

        Assert.Equal($"homeassistant/device/{component}/kitchen_module/config", message.Topic);
    }

    [Theory]
    [InlineData("Kitchen module", "kitchen_module")]
    [InlineData("kitchen", "kitchen")]
    [InlineData("Salon à manger", "salon_a_manger")]
    [InlineData("ÉCOLE", "ecole")]
    [InlineData("Straße 1", "strasse_1")]
    [InlineData("Œuf Æon", "oeuf_aeon")]
    [InlineData("æther", "aether")]
    [InlineData("  Leading and trailing  ", "leading_and_trailing")]
    [InlineData("A--B__C", "a_b_c")]
    [InlineData("Cuisine (RDC)", "cuisine_rdc")]
    [InlineData("Room 12", "room_12")]
    public async Task AnnounceDevicesAsync_DeviceName_IsSanitizedToBuildObjectId(string name, string expected)
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice(name: name);

        var message = Assert.Single(await harness.AnnounceAsync());
        var payload = ParsePayload(message);

        Assert.Equal($"homeassistant/device/opennetty-scs/{expected}/config", message.Topic);
        Assert.Equal(expected, (string?) payload["device"]?["identifiers"]?[0]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_ObjectIdSetting_OverridesSanitizedDeviceName()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice(settings: CreateSettings((OpenNettySettings.HomeAssistantObjectId, "custom_object_id")));

        var message = Assert.Single(await harness.AnnounceAsync());

        Assert.Equal("homeassistant/device/opennetty-scs/custom_object_id/config", message.Topic);
        Assert.Equal("custom_object_id", (string?) ParsePayload(message)["device"]?["identifiers"]?[0]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_DeviceWithDiscoveryDisabled_IsSkipped()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice(name: "Hidden", settings: CreateSettings((OpenNettySettings.HomeAssistantDiscovery, "false")));
        harness.AddDevice(name: "Visible", settings: CreateSettings((OpenNettySettings.HomeAssistantDiscovery, "true")));
        harness.AddDevice(name: "Default");

        var messages = await harness.AnnounceAsync();

        Assert.Equal(
        [
            "homeassistant/device/opennetty-scs/visible/config",
            "homeassistant/device/opennetty-scs/default/config"
        ], messages.Select(static message => message.Topic), StringComparer.Ordinal);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_MultipleDevices_PublishesOneMessagePerDeviceInOrder()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice(name: "First");
        harness.AddDevice(OpenNettyBrand.Legrand, "03600", "Second");
        harness.AddDevice(OpenNettyBrand.BTicino, "3578", "Third");

        var messages = await harness.AnnounceAsync();

        Assert.Equal(
        [
            "homeassistant/device/opennetty-scs/first/config",
            "homeassistant/device/opennetty-nitoo/second/config",
            "homeassistant/device/opennetty-zigbee/third/config"
        ], messages.Select(static message => message.Topic), StringComparer.Ordinal);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_Device_DescribesTheDevice()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice();

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal(
            """{"identifiers":["kitchen_module"],"manufacturer":"BTicino","model_id":"F411U1","name":"BTicino F411U1","model":"1-gang DIN rail switch"}""",
            payload["device"]?.ToJsonString());
    }

    [Fact]
    public async Task AnnounceDevicesAsync_LegrandDevice_UsesLegrandIdentity()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice(OpenNettyBrand.Legrand, "03847");

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal("Legrand", (string?) payload["device"]?["manufacturer"]);
        Assert.Equal("03847", (string?) payload["device"]?["model_id"]);
        Assert.Equal("Legrand 03847", (string?) payload["device"]?["name"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_DeviceWithIdentifier_IncludesIdentifierInDefaultName()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice(identifier: OpenNettyDeviceIdentifier.FromScsSerialNumber("0001ABCD"));

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal("BTicino F411U1 (0001ABCD)", (string?) payload["device"]?["name"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_DeviceNameSetting_OverridesDefaultName()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice(
            identifier: OpenNettyDeviceIdentifier.FromScsSerialNumber("0001ABCD"),
            settings: CreateSettings((OpenNettySettings.HomeAssistantDeviceName, "My kitchen module")));

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal("My kitchen module", (string?) payload["device"]?["name"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_DeviceWithoutIdentifier_DoesNotDescribeSerialNumberOrConnections()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice();

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.False(payload["device"]!.AsObject().ContainsKey("serial_number"));
        Assert.False(payload["device"]!.AsObject().ContainsKey("connections"));
    }

    [Theory]
    [InlineData(OpenNettyBrand.BTicino, "F411U1", "ScsSerialNumber", "0001ABCD", "0001ABCD")]
    [InlineData(OpenNettyBrand.Legrand, "03600", "NitooSerialNumber", "1234", "001234")]
    [InlineData(OpenNettyBrand.BTicino, "3528N", "ZigbeeSerialNumber", "00AB12CD", "00AB12CD")]
    public async Task AnnounceDevicesAsync_DeviceWithSerialNumber_DescribesSerialNumber(
        OpenNettyBrand brand, string model, string type, string value, string expected)
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice(brand, model, identifier: type switch
        {
            "ScsSerialNumber"    => OpenNettyDeviceIdentifier.FromScsSerialNumber(value),
            "NitooSerialNumber"  => OpenNettyDeviceIdentifier.FromNitooSerialNumber(uint.Parse(value, CultureInfo.InvariantCulture)),
            "ZigbeeSerialNumber" => OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(value),

            _ => throw new ArgumentOutOfRangeException(nameof(type))
        });

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal(expected, (string?) payload["device"]?["serial_number"]);
        Assert.False(payload["device"]!.AsObject().ContainsKey("connections"));
    }

    [Fact]
    public async Task AnnounceDevicesAsync_DeviceWithMacAddress_DescribesConnection()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice(identifier: OpenNettyDeviceIdentifier.FromMacAddress("00:04:74:00:12:34"));

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal("""[["mac","00:04:74:00:12:34"]]""", payload["device"]?["connections"]?.ToJsonString());
        Assert.False(payload["device"]!.AsObject().ContainsKey("serial_number"));
    }

    [Theory]
    [InlineData("en", "1-gang DIN rail switch")]
    [InlineData("fr", "Interrupteur modulaire simple")]
    [InlineData("fr-FR", "Interrupteur modulaire simple")]
    [InlineData("de", "1-gang DIN rail switch")]
    public async Task AnnounceDevicesAsync_UICultureOption_LocalizesDeviceModel(string culture, string expected)
    {
        await using var harness = new WorkerHarness(options => options.HomeAssistantDiscoveryUICulture = CultureInfo.GetCultureInfo(culture));
        harness.AddDevice();

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal(expected, (string?) payload["device"]?["model"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_UICultureSetting_OverridesUICultureOption()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice(settings: CreateSettings((OpenNettySettings.HomeAssistantDiscoveryUICulture, "fr")));

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal("Interrupteur modulaire simple", (string?) payload["device"]?["model"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_SuggestedAreaSetting_DescribesSuggestedArea()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice(settings: CreateSettings((OpenNettySettings.HomeAssistantSuggestedArea, "Kitchen")));

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal("Kitchen", (string?) payload["device"]?["suggested_area"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_EmptySuggestedAreaSetting_DoesNotDescribeSuggestedArea()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice(settings: CreateSettings((OpenNettySettings.HomeAssistantSuggestedArea, string.Empty)));

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.False(payload["device"]!.AsObject().ContainsKey("suggested_area"));
    }

    [Fact]
    public async Task AnnounceDevicesAsync_DeviceConnectedThroughAnotherDevice_DescribesViaDevice()
    {
        await using var harness = new WorkerHarness();
        var gatewayDevice = harness.AddDevice(OpenNettyBrand.BTicino, "F454", "Main gateway");
        var gateway = OpenNettyGateway.Create(gatewayDevice, new IPEndPoint(IPAddress.Loopback, 20000));
        gatewayDevice.Gateway = gateway;
        harness.AddDevice(gateway: gateway);

        var messages = await harness.AnnounceAsync();

        Assert.False(ParsePayload(messages[0])["device"]!.AsObject().ContainsKey("via_device"));
        Assert.Equal("main_gateway", (string?) ParsePayload(messages[1])["device"]?["via_device"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_GatewayObjectIdSetting_IsUsedAsViaDevice()
    {
        await using var harness = new WorkerHarness();
        var gatewayDevice = harness.AddDevice(OpenNettyBrand.BTicino, "F454", "Main gateway",
            settings: CreateSettings((OpenNettySettings.HomeAssistantObjectId, "gateway_object")));
        var gateway = OpenNettyGateway.Create(gatewayDevice, new IPEndPoint(IPAddress.Loopback, 20000));
        gatewayDevice.Gateway = gateway;
        harness.AddDevice(gateway: gateway);

        var messages = await harness.AnnounceAsync();

        Assert.Equal("gateway_object", (string?) ParsePayload(messages[1])["device"]?["via_device"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_DeviceWithoutGateway_DoesNotDescribeViaDevice()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice();

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.False(payload["device"]!.AsObject().ContainsKey("via_device"));
    }

    [Fact]
    public async Task AnnounceDevicesAsync_EndpointOfFirstUnit_IsPublishedAsDeviceComponent()
    {
        await using var harness = new WorkerHarness();
        var device = harness.AddDevice();
        var endpoint = harness.AddUnitEndpoint(device, "Kitchen", [OpenNettyCapabilities.OnOffSwitchControl]);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        var components = payload["components"]!.AsObject();
        var component = Assert.Single(components);
        Assert.Equal($"entity_{ComputeUniqueId(endpoint, "feb44223-4814-4652-933c-53dbbaabac3f")}", component.Key);
        Assert.Equal("light", (string?) component.Value?["platform"]);
        Assert.Empty(payload["child_devices"]!.AsArray());
    }

    [Fact]
    public async Task AnnounceDevicesAsync_EndpointOfAnotherDevice_IsIgnored()
    {
        await using var harness = new WorkerHarness();
        harness.AddDevice();
        var other = harness.AddDevice(name: "Other module");
        harness.AddUnitEndpoint(other, "Bedroom", [OpenNettyCapabilities.OnOffSwitchControl]);

        var messages = await harness.AnnounceAsync();

        Assert.Empty(ParsePayload(messages[0])["components"]!.AsObject());
        Assert.Single(ParsePayload(messages[1])["components"]!.AsObject());
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("true", true)]
    [InlineData("invalid", true)]
    public async Task AnnounceDevicesAsync_EndpointDiscoverySetting_ControlsComponentPublication(string value, bool published)
    {
        await using var harness = new WorkerHarness();
        var device = harness.AddDevice();
        harness.AddUnitEndpoint(device, "Kitchen", [OpenNettyCapabilities.OnOffSwitchControl],
            CreateSettings((OpenNettySettings.HomeAssistantDiscovery, value)));

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal(published ? 1 : 0, payload["components"]!.AsObject().Count);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_FirstUnitWithDiscoveryDisabled_SkipsItsEndpoints()
    {
        await using var harness = new WorkerHarness();
        var device = harness.AddDevice();
        device.GetUnit(0).Settings = CreateSettings((OpenNettySettings.HomeAssistantDiscovery, "false"));
        harness.AddUnitEndpoint(device, "Kitchen", [OpenNettyCapabilities.OnOffSwitchControl]);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Empty(payload["components"]!.AsObject());
    }

    [Fact]
    public async Task AnnounceDevicesAsync_EndpointsOfOtherUnits_ArePublishedAsChildDevices()
    {
        await using var harness = new WorkerHarness();
        var device = harness.AddDevice(OpenNettyBrand.Legrand, "03600", "Hall");
        harness.AddUnitEndpoint(device, "Hall output 1", [OpenNettyCapabilities.OnOffSwitchControl], unit: 3);
        harness.AddUnitEndpoint(device, "Hall output 2", [OpenNettyCapabilities.OnOffSwitchControl], unit: 4);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Empty(payload["components"]!.AsObject());
        var children = payload["child_devices"]!.AsArray();
        Assert.Equal(2, children.Count);
        Assert.Equal("""["hall_unit_3"]""", children[0]?["identifiers"]?.ToJsonString());
        Assert.Equal("Output 1", (string?) children[0]?["name"]);
        Assert.Equal("""["hall_unit_4"]""", children[1]?["identifiers"]?.ToJsonString());
        Assert.Equal("Output 2", (string?) children[1]?["name"]);
        Assert.Contains(children[0]!["components"]!.AsObject(), static item => (string?) item.Value?["platform"] is "light");
        Assert.Contains(children[1]!["components"]!.AsObject(), static item => (string?) item.Value?["platform"] is "light");
    }

    [Theory]
    [InlineData("en", "Output 1")]
    [InlineData("fr", "Sortie 1")]
    public async Task AnnounceDevicesAsync_ChildDevice_UsesLocalizedUnitDescriptionAsName(string culture, string expected)
    {
        await using var harness = new WorkerHarness(options => options.HomeAssistantDiscoveryUICulture = CultureInfo.GetCultureInfo(culture));
        var device = harness.AddDevice(OpenNettyBrand.Legrand, "03600", "Hall");
        harness.AddUnitEndpoint(device, "Hall output 1", [OpenNettyCapabilities.OnOffSwitchControl], unit: 3);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal(expected, (string?) payload["child_devices"]?[0]?["name"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_ChildDeviceWithoutDescription_UsesUnitIdentifierAsName()
    {
        await using var harness = new WorkerHarness();
        var device = harness.AddDevice(OpenNettyBrand.Legrand, "03648", "Gateway module");
        harness.AddUnitEndpoint(device, "Doorbell", [OpenNettyCapabilities.ActionScenarioActivation], unit: 2);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal("Unit 2", (string?) payload["child_devices"]?[0]?["name"]);
        Assert.Equal("""["gateway_module_unit_2"]""", payload["child_devices"]?[0]?["identifiers"]?.ToJsonString());
    }

    [Fact]
    public async Task AnnounceDevicesAsync_ChildDeviceUnitSettings_OverrideIdentifierAndName()
    {
        await using var harness = new WorkerHarness();
        var device = harness.AddDevice(OpenNettyBrand.Legrand, "03600", "Hall");
        device.GetUnit(3).Settings = CreateSettings(
            (OpenNettySettings.HomeAssistantDeviceName, "Ceiling light"),
            (OpenNettySettings.HomeAssistantObjectId, "ceiling"));
        harness.AddUnitEndpoint(device, "Hall output 1", [OpenNettyCapabilities.OnOffSwitchControl], unit: 3);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal("""["ceiling_unit_3"]""", payload["child_devices"]?[0]?["identifiers"]?.ToJsonString());
        Assert.Equal("Ceiling light", (string?) payload["child_devices"]?[0]?["name"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_ChildDeviceUnitWithDiscoveryDisabled_IsSkipped()
    {
        await using var harness = new WorkerHarness();
        var device = harness.AddDevice(OpenNettyBrand.Legrand, "03600", "Hall");
        device.GetUnit(3).Settings = CreateSettings((OpenNettySettings.HomeAssistantDiscovery, "false"));
        harness.AddUnitEndpoint(device, "Hall output 1", [OpenNettyCapabilities.OnOffSwitchControl], unit: 3);
        harness.AddUnitEndpoint(device, "Hall output 2", [OpenNettyCapabilities.OnOffSwitchControl], unit: 4);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        var child = Assert.Single(payload["child_devices"]!.AsArray());
        Assert.Equal("""["hall_unit_4"]""", child?["identifiers"]?.ToJsonString());
    }

    [Fact]
    public async Task AnnounceDevicesAsync_ChildDeviceEndpointWithDiscoveryDisabled_IsSkipped()
    {
        await using var harness = new WorkerHarness();
        var device = harness.AddDevice(OpenNettyBrand.Legrand, "03600", "Hall");
        harness.AddUnitEndpoint(device, "Hall output 1", [OpenNettyCapabilities.OnOffSwitchControl],
            CreateSettings((OpenNettySettings.HomeAssistantDiscovery, "false")), unit: 3);
        harness.AddUnitEndpoint(device, "Hall output 2", [OpenNettyCapabilities.OnOffSwitchControl], unit: 4);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        var child = Assert.Single(payload["child_devices"]!.AsArray());
        Assert.Equal("""["hall_unit_4"]""", child?["identifiers"]?.ToJsonString());
    }

    [Fact]
    public async Task AnnounceDevicesAsync_ChildDeviceComponents_UseTheEndpointTopics()
    {
        await using var harness = new WorkerHarness();
        var device = harness.AddDevice(OpenNettyBrand.Legrand, "03600", "Hall");
        var endpoint = harness.AddUnitEndpoint(device, "Hall output 1", [OpenNettyCapabilities.OnOffSwitchControl],
            CreateSettings((OpenNettySettings.MqttTopic, "lights/hall")), unit: 3);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        var light = GetEntity(endpoint, payload["child_devices"]![0]!["components"]!.AsObject(), LightDiscriminator);
        Assert.Equal("opennetty/lights/hall/switch_state/set", (string?) light["command_topic"]);
        Assert.Equal("opennetty/lights/hall/availability", (string?) light["availability_topic"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_GatewayEndpointsWithoutUnit_ArePublishedAsVirtualChildDevices()
    {
        await using var harness = new WorkerHarness();
        var (_, gateway) = harness.AddGateway();
        harness.AddEndpoint("Corridor light", [OpenNettyCapabilities.OnOffSwitchControl], gateway: gateway);
        harness.AddEndpoint("Porch light", [OpenNettyCapabilities.OnOffSwitchControl], gateway: gateway);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        var children = payload["child_devices"]!.AsArray();
        Assert.Equal(2, children.Count);
        Assert.Equal("""["corridor_light"]""", children[0]?["identifiers"]?.ToJsonString());
        Assert.Equal("""["porch_light"]""", children[1]?["identifiers"]?.ToJsonString());
        Assert.All(children, static child => Assert.Single(child!["components"]!.AsObject()));
    }

    [Theory]
    [InlineData("nitoo", "ID8121")]
    [InlineData("zigbee", "ID8122")]
    [InlineData("scs_light_point", "ID8123")]
    [InlineData("scs_scenario_plus", "ID8124")]
    [InlineData("scs_dry_contact", "ID8120")]
    [InlineData("none", "ID8120")]
    public async Task AnnounceDevicesAsync_VirtualChildDevice_IsNamedAfterTheAddressType(string address, string resource)
    {
        await using var harness = new WorkerHarness();
        var (_, gateway) = harness.AddGateway();
        harness.AddEndpoint("Virtual", [OpenNettyCapabilities.OnOffSwitchControl], address: address switch
        {
            "nitoo"             => OpenNettyAddress.FromNitooAddress(1000, 1),
            "zigbee"            => OpenNettyAddress.FromZigbeeAddress(OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(0x1234), 1),
            "scs_light_point"   => OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, 2),
            "scs_scenario_plus" => OpenNettyAddress.FromScsScenarioPlusAddress(1),
            "scs_dry_contact"   => OpenNettyAddress.FromScsDryContactAddress(1),

            _ => null
        }, gateway: gateway);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal(GetLocalizedString(resource), (string?) payload["child_devices"]?[0]?["name"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_VirtualChildDeviceSettings_OverrideIdentifierAndName()
    {
        await using var harness = new WorkerHarness();
        var (_, gateway) = harness.AddGateway();
        harness.AddEndpoint("Virtual", [OpenNettyCapabilities.OnOffSwitchControl], CreateSettings(
            (OpenNettySettings.HomeAssistantDeviceName, "Garden light"),
            (OpenNettySettings.HomeAssistantObjectId, "garden")), gateway: gateway);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal("""["garden"]""", payload["child_devices"]?[0]?["identifiers"]?.ToJsonString());
        Assert.Equal("Garden light", (string?) payload["child_devices"]?[0]?["name"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_VirtualChildDeviceEndpointWithDiscoveryDisabled_IsSkipped()
    {
        await using var harness = new WorkerHarness();
        var (_, gateway) = harness.AddGateway();
        harness.AddEndpoint("Hidden", [OpenNettyCapabilities.OnOffSwitchControl],
            CreateSettings((OpenNettySettings.HomeAssistantDiscovery, "false")), gateway: gateway);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Empty(payload["child_devices"]!.AsArray());
    }

    [Fact]
    public async Task AnnounceDevicesAsync_EndpointsOfAnotherGateway_AreNotPublishedAsVirtualChildDevices()
    {
        await using var harness = new WorkerHarness();
        var (_, gateway) = harness.AddGateway();
        var (_, other) = harness.AddGateway("Other gateway", 20001);
        harness.AddEndpoint("Mine", [OpenNettyCapabilities.OnOffSwitchControl], gateway: gateway);
        harness.AddEndpoint("Theirs", [OpenNettyCapabilities.OnOffSwitchControl], gateway: other);

        var messages = await harness.AnnounceAsync();

        Assert.Equal(2, messages.Count);
        Assert.Equal("""["mine"]""", ParsePayload(messages[0])["child_devices"]?[0]?["identifiers"]?.ToJsonString());
        Assert.Equal("""["theirs"]""", ParsePayload(messages[1])["child_devices"]?[0]?["identifiers"]?.ToJsonString());
        Assert.Single(ParsePayload(messages[0])["child_devices"]!.AsArray());
    }

    [Fact]
    public async Task AnnounceDevicesAsync_EndpointsOfDevicesWithoutGatewayCapability_AreNotPublishedAsVirtualChildDevices()
    {
        await using var harness = new WorkerHarness();
        var (_, gateway) = harness.AddGateway();
        harness.AddDevice(gateway: gateway);
        harness.AddEndpoint("Orphan", [OpenNettyCapabilities.OnOffSwitchControl], gateway: gateway);

        var messages = await harness.AnnounceAsync();

        Assert.Empty(ParsePayload(messages[1])["child_devices"]!.AsArray());
        Assert.Single(ParsePayload(messages[0])["child_devices"]!.AsArray());
    }

    [Fact]
    public async Task AnnounceDevicesAsync_GatewayEndpointsAttachedToUnits_AreNotPublishedAsVirtualChildDevices()
    {
        await using var harness = new WorkerHarness();
        var (device, _) = harness.AddGateway();
        var endpoint = harness.AddUnitEndpoint(device, "Gateway endpoint", [OpenNettyCapabilities.OnOffSwitchControl]);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Empty(payload["child_devices"]!.AsArray());
        Assert.Contains($"entity_{ComputeUniqueId(endpoint, "feb44223-4814-4652-933c-53dbbaabac3f")}",
            payload["components"]!.AsObject().Select(static item => item.Key), StringComparer.Ordinal);
    }

    public static TheoryData<OpenNettyCapability[], string[]> EndpointCapabilities => new()
    {
        { [OpenNettyCapabilities.OnOffSwitchState], [] },
        {
            [OpenNettyCapabilities.OnOffSwitchControl],
            ["light;feb44223-4814-4652-933c-53dbbaabac3f;name=#ID8001;command_topic={topic}/switch_state/set;!state_topic;!icon;!brightness_command_topic;!brightness_state_topic"]
        },
        {
            [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.OnOffSwitchState],
            [
                "light;feb44223-4814-4652-933c-53dbbaabac3f;name=#ID8001;command_topic={topic}/switch_state/set;state_topic={topic}/switch_state",
                Button("3a10d925-c599-41a9-8a7e-30a04aefec86", "ID8002", "switch_state/get", properties: "entity_category=diagnostic")
            ]
        },
        {
            [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.BasicDimmingControl],
            ["light;feb44223-4814-4652-933c-53dbbaabac3f;command_topic={topic}/switch_state/set;brightness_command_topic={topic}/brightness/set;brightness_scale=100;on_command_type=last;!brightness_state_topic"]
        },
        {
            [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.AdvancedDimmingControl],
            ["light;feb44223-4814-4652-933c-53dbbaabac3f;command_topic={topic}/switch_state/set;brightness_command_topic={topic}/brightness/set;brightness_scale=100;on_command_type=last;!brightness_state_topic"]
        },
        {
            [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.BasicDimmingState],
            [
                "light;feb44223-4814-4652-933c-53dbbaabac3f;command_topic={topic}/switch_state/set;brightness_state_topic={topic}/brightness;!brightness_command_topic;!on_command_type",
                Button("f05ecfb8-70d5-4116-b0a8-2f6d9d02090f", "ID8003", "brightness/get", properties: "entity_category=diagnostic")
            ]
        },
        {
            [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.AdvancedDimmingState],
            [
                "light;feb44223-4814-4652-933c-53dbbaabac3f;command_topic={topic}/switch_state/set;brightness_state_topic={topic}/brightness;!brightness_command_topic",
                Button("f05ecfb8-70d5-4116-b0a8-2f6d9d02090f", "ID8003", "brightness/get", properties: "entity_category=diagnostic")
            ]
        },
        { [OpenNettyCapabilities.BasicDimmingState], [] },
        { [OpenNettyCapabilities.AdvancedDimmingState], [] },
        { [OpenNettyCapabilities.BasicDimmingControl], [] },
        { [OpenNettyCapabilities.AdvancedDimmingControl], [] },
        {
            [OpenNettyCapabilities.BasicShutterControl],
            ["cover;9b138d62-bb0d-49cb-8624-1d85f9e86a6e;device_class=shutter;name=#ID8004;command_topic={topic}/shutter_state/set;!state_topic;!set_position_topic;!position_topic"]
        },
        {
            [OpenNettyCapabilities.AdvancedShutterControl],
            ["cover;9b138d62-bb0d-49cb-8624-1d85f9e86a6e;command_topic={topic}/shutter_state/set;set_position_topic={topic}/shutter_position/set;!state_topic;!position_topic"]
        },
        {
            [OpenNettyCapabilities.BasicShutterControl, OpenNettyCapabilities.BasicShutterState],
            [
                "cover;9b138d62-bb0d-49cb-8624-1d85f9e86a6e;command_topic={topic}/shutter_state/set;state_topic={topic}/shutter_state;!position_topic",
                Button("3a760f9d-ca89-4c9e-9ad4-8ba40ad36f59", "ID8005", "shutter_state/get", properties: "entity_category=diagnostic")
            ]
        },
        {
            [OpenNettyCapabilities.AdvancedShutterControl, OpenNettyCapabilities.AdvancedShutterState],
            [
                "cover;9b138d62-bb0d-49cb-8624-1d85f9e86a6e;command_topic={topic}/shutter_state/set;set_position_topic={topic}/shutter_position/set;position_topic={topic}/shutter_position;!state_topic",
                Button("3a760f9d-ca89-4c9e-9ad4-8ba40ad36f59", "ID8005", "shutter_state/get", properties: "entity_category=diagnostic"),
                Button("5731274c-e498-4c7b-8671-6de8c448eb99", "ID8006", "shutter_position/get", properties: "entity_category=diagnostic")
            ]
        },
        {
            [OpenNettyCapabilities.BasicShutterControl, OpenNettyCapabilities.AdvancedShutterState],
            [
                "cover;9b138d62-bb0d-49cb-8624-1d85f9e86a6e;command_topic={topic}/shutter_state/set;position_topic={topic}/shutter_position;!set_position_topic",
                Button("3a760f9d-ca89-4c9e-9ad4-8ba40ad36f59", "ID8005", "shutter_state/get", properties: "entity_category=diagnostic"),
                Button("5731274c-e498-4c7b-8671-6de8c448eb99", "ID8006", "shutter_position/get", properties: "entity_category=diagnostic")
            ]
        },
        { [OpenNettyCapabilities.BasicShutterState], [] },
        { [OpenNettyCapabilities.AdvancedShutterState], [] },
        {
            [OpenNettyCapabilities.ActionScenarioActivation],
            [
                Button("63485e4c-a3bd-4fc9-831d-b96bacddade9", "ID8008", "scenario/set", "action"),
                Button("bca953c0-7598-4baa-91df-f14ddc30450f", "ID8025", "scenario/set", "stop_action")
            ]
        },
        {
            [OpenNettyCapabilities.ActuatorLocking],
            [
                Button("e301aa1c-a50e-45ff-b6d2-58d7525e4867", "ID8131", "actuator_lock/set", "lock", "entity_category=config", "icon=mdi:lock"),
                Button("b7092044-74e3-4e9a-b349-655a7caf1b2b", "ID8132", "actuator_lock/set", "unlock", "entity_category=config", "icon=mdi:lock-open")
            ]
        },
        {
            [OpenNettyCapabilities.DimmingScenarioActivation],
            [
                Button("f47365e3-86fa-449f-b84e-a06acc3484b1", "ID8111", "scenario/set", """{"event_type":"dimming","dimming_step":5}"""),
                Button("5c2db171-97b3-451e-a502-8800928b4335", "ID8112", "scenario/set", """{"event_type":"dimming","dimming_step":-5}""")
            ]
        },
        {
            [OpenNettyCapabilities.DryContactScenarioActivation],
            [
                Button("91510f47-559b-4a44-a4d2-a0f4d3a634c6", "ID8125", "scenario/set", """{"event_type":"contact_open"}"""),
                Button("e1b494f8-ab8b-4635-8f2c-81d354bc83dc", "ID8126", "scenario/set", """{"event_type":"contact_closed"}""")
            ]
        },
        {
            [OpenNettyCapabilities.DryContactState],
            [
                "binary_sensor;1aca2cbc-16cb-46df-be3a-ac6a2684910b;name=#ID8127;state_topic={topic}/dry_contact_state;json_attributes_topic={topic}/dry_contact_state;payload_on=closed;payload_off=open;value_template={{ value_json.state }};!device_class;!icon",
                Button("00244687-d0b8-4839-a11c-7dcbda9f2f3b", "ID8128", "dry_contact_state/get", properties: "entity_category=diagnostic")
            ]
        },
        {
            [OpenNettyCapabilities.OnOffScenarioActivation],
            [
                Button("f053a594-66fa-42a6-9237-64d570b2bd57", "ID8009", "scenario/set", "switch_on"),
                Button("d292636e-00d3-442e-b1db-73d60b4085ec", "ID8010", "scenario/set", "switch_off")
            ]
        },
        {
            [OpenNettyCapabilities.OutgoingCommunication],
            ["sensor;9563390d-24c4-48f0-a0de-61fef1e58eca;entity_category=diagnostic;device_class=timestamp;name=#ID8119;state_topic={topic}/last_communication_date"]
        },
        {
            [OpenNettyCapabilities.PressureScenarioPlusActivation],
            [
                Button("63a92ba4-bec3-453e-a44a-9219e4f4d478", "ID8022", "scenario/set", """{"event_type":"short_pressure","scenario_type":"plus"}"""),
                Button("e524f94b-9862-4da4-8f57-82c220e4560c", "ID8023", "scenario/set", """{"event_type":"start_of_extended_pressure","scenario_type":"plus"}"""),
                Button("bf90ede8-9078-4817-8ec4-ef762c3e2077", "ID8018", "scenario/set", """{"event_type":"extended_pressure","scenario_type":"plus"}"""),
                Button("73ff2263-9962-443a-89a1-f209fba81948", "ID8024", "scenario/set", """{"event_type":"end_of_extended_pressure","scenario_type":"plus"}""")
            ]
        },
        {
            [OpenNettyCapabilities.StopUpDownScenarioActivation],
            [
                Button("9065ccb4-d2c6-47f5-b118-e3d2ecbe20c3", "ID8026", "scenario/set", "shutter_up"),
                Button("4006cf9e-620c-49d4-81b3-ab060d376966", "ID8027", "scenario/set", "shutter_down"),
                Button("d13fdcd2-c974-490a-b544-436a91785ffa", "ID8028", "scenario/set", "shutter_stop")
            ]
        },
        {
            [OpenNettyCapabilities.BatteryAlert],
            [
                "binary_sensor;b7dd3824-ddc3-4cf3-b79e-168faa710e43;entity_category=diagnostic;device_class=battery;name=#ID8029;state_topic={topic}/battery_alert",
                Button("7f7625f0-2461-4804-9cae-829a1040cd93", "ID8030", "battery_alert/set", "OFF", "entity_category=diagnostic", "icon=mdi:battery-check")
            ]
        },
        {
            [OpenNettyCapabilities.BatteryLevel],
            ["sensor;6a8c7f1c-426a-47ab-97a1-a476acc603fd;entity_category=diagnostic;device_class=battery;unit_of_measurement=%;name=#ID8031;state_topic={topic}/battery_level"]
        },
        {
            [OpenNettyCapabilities.FirmwareVersion],
            [
                "sensor;3a92e77f-3910-4a20-9d19-caa1961dc33d;entity_category=diagnostic;name=#ID8032;state_topic={topic}/firmware_version",
                Button("e4fa32b3-9e9f-43b5-810a-cdb75acf44e5", "ID8033", "firmware_version/get", properties: ["entity_category=diagnostic", "icon=mdi:help"])
            ]
        },
        {
            [OpenNettyCapabilities.HardwareVersion],
            [
                "sensor;1091f326-0c22-4c59-af04-d0a6ee429a0c;entity_category=diagnostic;name=#ID8034;state_topic={topic}/hardware_version",
                Button("0371ccbb-52fd-4288-a943-b2f04a7b1e8b", "ID8035", "hardware_version/get", properties: ["entity_category=diagnostic", "icon=mdi:help"])
            ]
        },
        {
            [OpenNettyCapabilities.MacAddress],
            [
                "sensor;a8de45b2-0bb5-4375-b33b-0869623e40a7;entity_category=diagnostic;name=#ID8036;state_topic={topic}/mac_address",
                Button("aa1968e6-f232-4b96-a29f-0e64de093bb0", "ID8037", "mac_address/get", properties: ["entity_category=diagnostic", "icon=mdi:help"])
            ]
        },
        {
            [OpenNettyCapabilities.MemoryReading],
            [
                "sensor;a4c866e8-8388-4f1b-b300-f5adcbcc818b;entity_category=diagnostic;icon=mdi:memory;name=#ID8129;state_topic={topic}/memory_data;value_template={{ value_json | count }};json_attributes_topic={topic}/memory_data;json_attributes_template={{ {'entries': value_json} | tojson }}",
                Button("314e1f13-f490-459f-852d-cfb1020b534f", "ID8130", "memory_data/get", properties: ["entity_category=diagnostic", "icon=mdi:memory-arrow-down"])
            ]
        },
        {
            [OpenNettyCapabilities.PilotWireControl],
            [
                "select;205a01a1-ba4c-4e9b-a19a-c1589c445cbb;icon=mdi:radiator;name=#ID8039;command_topic={topic}/pilot_wire_setpoint_mode/set;state_topic={topic}/pilot_wire_setpoint_mode",
                Button("7a9130b9-675a-437d-b806-cbfe6f6e20a6", "ID8062", "pilot_wire_setpoint_mode/get", properties: "entity_category=diagnostic")
            ]
        },
        {
            [OpenNettyCapabilities.PilotWireDerogation],
            [
                "select;f5f57920-d758-4ca8-8161-2614d4abeef0;icon=mdi:radiator;name=#ID8045;command_topic={topic}/pilot_wire_derogation_mode/set;state_topic={topic}/pilot_wire_derogation_mode",
                Button("787582e8-0c5f-4c97-9277-0ad23dab4024", "ID8063", "pilot_wire_derogation_mode/get", properties: "entity_category=diagnostic")
            ]
        },
        {
            [OpenNettyCapabilities.PilotWireShutdown],
            [
                "switch;178d9f9b-e87a-4ebf-8db3-80e1e1a091df;icon=mdi:radiator-off;name=#ID8113;command_topic={topic}/pilot_wire_shutdown_mode/set;state_topic={topic}/pilot_wire_shutdown_mode",
                Button("ecc822a6-57ab-4352-8d2c-d85dc73df5da", "ID8114", "pilot_wire_shutdown_mode/get", properties: "entity_category=diagnostic")
            ]
        },
        {
            [OpenNettyCapabilities.SmartMeterIndexes],
            [
                EnergySensor("6c83787a-3537-49fa-b409-dc15d5c37b43", "ID8064", "smart_meter_base_index", "base_index"),
                EnergySensor("0a9d909b-8449-496e-b38c-4dc3e2653288", "ID8065", "smart_meter_blue_index", "base_index"),
                EnergySensor("ccf0ce01-7b31-4eb6-995f-94038c186d20", "ID8115", "smart_meter_blue_index", "off_peak_index"),
                EnergySensor("2661d8db-085a-41bb-bab6-a1627cbf91d0", "ID8066", "smart_meter_peak_off_peak_index", "base_index"),
                EnergySensor("b087f0f1-db08-4a51-897a-dd5427590ad8", "ID8116", "smart_meter_peak_off_peak_index", "off_peak_index"),
                EnergySensor("7dcf846c-fbdd-4457-9a17-9cbc0a7c072b", "ID8067", "smart_meter_red_index", "base_index"),
                EnergySensor("7efb6978-3112-4ef6-86f9-fe9127a4411d", "ID8117", "smart_meter_red_index", "off_peak_index"),
                EnergySensor("1de29bc5-70b6-4302-aa27-8ecbcce13ec9", "ID8068", "smart_meter_white_index", "base_index"),
                EnergySensor("89b5ff55-499a-45f0-948a-28e74e02bc6f", "ID8118", "smart_meter_white_index", "off_peak_index"),
                "sensor;e07f0687-6ca1-47d9-a5b7-b20c0e79775a;device_class=enum;icon=mdi:receipt-text-outline;name=#ID8069;state_topic={topic}/smart_meter_subscription_type",
                Button("2eddee8f-7c81-47ea-a775-785e9dfb5c26", "ID8073", "smart_meter_base_index/get")
            ]
        },
        {
            [OpenNettyCapabilities.SmartMeterInformation],
            [
                "sensor;31eda1f3-343f-4cd7-9f56-ea792fcaec7f;device_class=enum;icon=mdi:receipt-text-outline;name=#ID8074;state_topic={topic}/smart_meter_rate_type",
                "binary_sensor;280fd1d1-4220-42ec-bf70-69936b728eb2;device_class=running;icon=mdi:transmission-tower-off;name=#ID8077;state_topic={topic}/smart_meter_power_cut_mode",
                Button("91e32508-61fa-46e3-ba57-32166b2de116", "ID8078", "smart_meter_rate_type/get", properties: "entity_category=diagnostic"),
                Button("df24321d-01e8-4d1a-9cca-92ece18934b4", "ID8079", "smart_meter_power_cut_mode/get", properties: "entity_category=diagnostic")
            ]
        },
        {
            [OpenNettyCapabilities.Uptime],
            [
                "sensor;46c1f892-f9bf-46b6-8658-fed1d7eb177b;entity_category=diagnostic;device_class=timestamp;name=#ID8080;state_topic={topic}/startup_date",
                Button("5e9b5094-b028-492c-8be4-5b73139e2c57", "ID8081", "startup_date/get", properties: "entity_category=diagnostic")
            ]
        },
        {
            [OpenNettyCapabilities.WaterHeating],
            [
                "select;733d9bf6-fd89-4ce1-bd71-d12a1c6a846e;icon=mdi:water-boiler;name=#ID8109;command_topic={topic}/water_heater_setpoint_mode/set;state_topic={topic}/water_heater_setpoint_mode",
                "binary_sensor;344b6548-196d-42de-b627-22530cc28f07;device_class=running;icon=mdi:fire;name=#ID8085;state_topic={topic}/water_heater_state;payload_on=heating;payload_off=idle",
                Button("bb186d7c-f49d-4aa2-8770-a0fdcf9de123", "ID8086", "water_heater_state/get", properties: "entity_category=diagnostic")
            ]
        },
        {
            [OpenNettyCapabilities.WirelessBurglarAlarmState],
            ["sensor;2dd476d5-a35a-442a-a3f2-4c2621dcf375;device_class=enum;icon=mdi:shield-home-outline;name=#ID8087;state_topic={topic}/wireless_burglar_alarm_state"]
        },
        {
            [OpenNettyCapabilities.ZigbeeBinding],
            [
                "binary_sensor;ee8cc336-01cb-4485-a377-dc5603c64a13;entity_category=diagnostic;device_class=running;icon=mdi:link-box;name=#ID8107;state_topic={topic}/zigbee_binding",
                Button("d04dc07d-b614-4e3e-aeac-ccaead40d919", "ID8094", "zigbee_binding/set", "bind", "entity_category=config", "icon=mdi:link"),
                Button("55a25c45-cb7a-4b74-baa4-7f9b87465d1a", "ID8095", "zigbee_binding/set", "unbind", "entity_category=config", "icon=mdi:link-off")
            ]
        },
        {
            [OpenNettyCapabilities.ZigbeeNetworkManagement],
            [
                "binary_sensor;da843c15-49d4-4a14-a7cb-4806783cd8a0;entity_category=diagnostic;device_class=opening;icon=mdi:wifi-strength-lock-open-outline;name=#ID8108;state_topic={topic}/zigbee_network",
                "sensor;5ea35f24-9a6c-4d62-b000-85e6a9ef5380;entity_category=diagnostic;icon=mdi:sine-wave;name=#ID8096;state_topic={topic}/zigbee_channel",
                "sensor;abda41d7-1b4c-41cc-99b9-81b7bc5801d3;entity_category=diagnostic;icon=mdi:counter;name=#ID8105;state_topic={topic}/zigbee_devices_count",
                Button("b7355e3d-0137-41e7-90fa-8b81b9530466", "ID8097", "zigbee_channel/get", properties: ["entity_category=diagnostic", "icon=mdi:sine-wave"]),
                Button("6c550d13-cbd5-4a7a-b445-1c30e9b83c65", "ID8106", "zigbee_devices_count/get", properties: ["entity_category=diagnostic", "icon=mdi:counter"]),
                Button("f366a06c-6d7f-4741-a99a-490fedeabf9f", "ID8098", "zigbee_network/set", "create", "icon=mdi:new-box"),
                Button("1c56979a-3ad2-4b9b-9116-cd7321416b6a", "ID8099", "zigbee_network/set", "join", "icon=mdi:download-network"),
                Button("63096c5c-3fba-4ea7-a30d-3568b0680e18", "ID8100", "zigbee_network/set", "leave", "icon=mdi:upload-network", "enabled_by_default=false"),
                Button("09953b7d-0e18-4fc9-9b8c-2a11b52a15c5", "ID8101", "zigbee_network/set", "open", "icon=mdi:lock-open"),
                Button("7e344b36-199e-4a43-987f-59fccacc859b", "ID8102", "zigbee_network/set", "close", "icon=mdi:lock"),
                Button("f6a3d89f-a3a4-4776-a6b0-a0e3328183ee", "ID8103", "zigbee_supervision/set", "enable", "icon=mdi:eye-check"),
                Button("35f91e70-674d-4977-9475-ba231553051d", "ID8104", "zigbee_supervision/set", "disable", "icon=mdi:eye-remove")
            ]
        }
    };

    [Fact]
    public async Task AnnounceDevicesAsync_CapabilitiesWithoutEntity_DoNotPublishComponents()
    {
        await using var harness = new WorkerHarness();

        var (_, components) = await DiscoverEndpointAsync(harness,
        [
            OpenNettyCapabilities.ConfigurableFunctionType,
            OpenNettyCapabilities.ConfigurablePushButtonNumbers,
            OpenNettyCapabilities.DateTime,
            OpenNettyCapabilities.DeviceDescription,
            OpenNettyCapabilities.MemoryWriting,
            OpenNettyCapabilities.OpenWebNetCommandSession,
            OpenNettyCapabilities.OpenWebNetEventSession,
            OpenNettyCapabilities.OpenWebNetGateway,
            OpenNettyCapabilities.OpenWebNetGenericSession,
            OpenNettyCapabilities.UnitDescription
        ]);

        Assert.Empty(components);
    }

    [Theory]
    [MemberData(nameof(EndpointCapabilities))]
    public async Task AnnounceDevicesAsync_EndpointCapabilities_PublishesExpectedEntities(OpenNettyCapability[] capabilities, string[] entities)
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness, capabilities);

        AssertEntities(endpoint, components, "opennetty/kitchen", entities);
    }

    [Theory]
    [InlineData(OpenNettyBrand.BTicino, "F411U1", null, "last")]
    [InlineData(OpenNettyBrand.Legrand, "03600", null, "brightness")]
    [InlineData(OpenNettyBrand.BTicino, "F411U1", "brightness", "brightness")]
    [InlineData(OpenNettyBrand.Legrand, "03600", "last", "last")]
    [InlineData(OpenNettyBrand.BTicino, "F411U1", "first", "first")]
    [InlineData(OpenNettyBrand.BTicino, "3578", null, "last")]
    public async Task AnnounceDevicesAsync_DimmableLight_UsesProtocolSpecificOnCommandType(
        OpenNettyBrand brand, string model, string? setting, string expected)
    {
        await using var harness = new WorkerHarness();
        var device = harness.AddDevice(brand, model);
        var endpoint = harness.AddUnitEndpoint(device, "Kitchen",
            [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.BasicDimmingControl],
            setting is null ? null : CreateSettings((OpenNettySettings.HomeAssistantLightOnCommandType, setting)));

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal(expected, (string?) GetEntity(endpoint, payload["components"]!.AsObject(), LightDiscriminator)["on_command_type"]);
    }

    [Theory]
    [InlineData("light", null, "light")]
    [InlineData("switch", null, "switch")]
    [InlineData(null, "Push button", "switch")]
    [InlineData(null, "Default", "switch")]
    [InlineData("light", "Push button", "light")]
    [InlineData("", null, "light")]
    [InlineData("", "Push button", "switch")]
    public async Task AnnounceDevicesAsync_EntityTypeAndSwitchMode_SelectLightOrSwitchPlatform(string? entityType, string? switchMode, string expected)
    {
        await using var harness = new WorkerHarness();
        List<(OpenNettySetting, string)> settings = [];

        if (entityType is not null)
        {
            settings.Add((OpenNettySettings.HomeAssistantEntityType, entityType));
        }

        if (switchMode is not null)
        {
            settings.Add((OpenNettySettings.SwitchMode, switchMode));
        }

        var (endpoint, components) = await DiscoverEndpointAsync(harness,
            [OpenNettyCapabilities.OnOffSwitchControl], CreateSettings([.. settings]));

        Assert.Equal(expected, (string?) GetEntity(endpoint, components, LightDiscriminator)["platform"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_UnsupportedEntityType_ThrowsInvalidOperationException()
    {
        await using var harness = new WorkerHarness();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await DiscoverEndpointAsync(harness,
            [OpenNettyCapabilities.OnOffSwitchControl], CreateSettings((OpenNettySettings.HomeAssistantEntityType, "fan"))));

        Assert.Equal(SR.FormatID0120("fan"), exception.Message);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_LightEndpoint_UsesDefaultNameAndNoIcon()
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness, [OpenNettyCapabilities.OnOffSwitchControl]);

        var light = GetEntity(endpoint, components, LightDiscriminator);
        Assert.Equal(GetLocalizedString(SR.ID8001), (string?) light["name"]);
        Assert.False(light.ContainsKey("icon"));
        Assert.False(light.ContainsKey("device_class"));
    }

    [Fact]
    public async Task AnnounceDevicesAsync_LightSettings_OverrideNameAndIcon()
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness, [OpenNettyCapabilities.OnOffSwitchControl], CreateSettings(
            (OpenNettySettings.HomeAssistantLightName, "Ceiling light"),
            (OpenNettySettings.HomeAssistantLightIcon, "mdi:ceiling-light"),
            (OpenNettySettings.HomeAssistantSwitchName, "Ignored"),
            (OpenNettySettings.HomeAssistantSwitchIcon, "mdi:ignored")));

        var light = GetEntity(endpoint, components, LightDiscriminator);
        Assert.Equal("Ceiling light", (string?) light["name"]);
        Assert.Equal("mdi:ceiling-light", (string?) light["icon"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_SwitchEndpoint_UsesDefaultNameAndDeviceClass()
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness,
            [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.OnOffSwitchState],
            CreateSettings((OpenNettySettings.HomeAssistantEntityType, "switch")));

        var entity = GetEntity(endpoint, components, LightDiscriminator);
        Assert.Equal("switch", (string?) entity["platform"]);
        Assert.Equal(GetLocalizedString(SR.ID8000), (string?) entity["name"]);
        Assert.Equal("switch", (string?) entity["device_class"]);
        Assert.Equal("opennetty/kitchen/switch_state/set", (string?) entity["command_topic"]);
        Assert.Equal("opennetty/kitchen/switch_state", (string?) entity["state_topic"]);
        Assert.False(entity.ContainsKey("icon"));
    }

    [Fact]
    public async Task AnnounceDevicesAsync_SwitchSettings_OverrideNameIconAndDeviceClass()
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness, [OpenNettyCapabilities.OnOffSwitchControl], CreateSettings(
            (OpenNettySettings.HomeAssistantEntityType, "switch"),
            (OpenNettySettings.HomeAssistantSwitchName, "Coffee machine"),
            (OpenNettySettings.HomeAssistantSwitchIcon, "mdi:coffee"),
            (OpenNettySettings.HomeAssistantSwitchDeviceClass, OpenNettySettings.HomeAssistantDeviceClasses.Switches.Outlet),
            (OpenNettySettings.HomeAssistantLightName, "Ignored"),
            (OpenNettySettings.HomeAssistantLightIcon, "mdi:ignored")));

        var entity = GetEntity(endpoint, components, LightDiscriminator);
        Assert.Equal("Coffee machine", (string?) entity["name"]);
        Assert.Equal("mdi:coffee", (string?) entity["icon"]);
        Assert.Equal("outlet", (string?) entity["device_class"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_DimmableSwitch_DoesNotExposeBrightness()
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness,
            [
                OpenNettyCapabilities.OnOffSwitchControl,
                OpenNettyCapabilities.BasicDimmingControl,
                OpenNettyCapabilities.BasicDimmingState
            ],
            CreateSettings((OpenNettySettings.HomeAssistantEntityType, "switch")));

        var entity = GetEntity(endpoint, components, LightDiscriminator);
        Assert.False(entity.ContainsKey("brightness_command_topic"));
        Assert.False(entity.ContainsKey("brightness_state_topic"));
        Assert.False(entity.ContainsKey("brightness_scale"));
        Assert.False(entity.ContainsKey("on_command_type"));
    }

    [Fact]
    public async Task AnnounceDevicesAsync_DeviceLevelSettings_AreInheritedByEndpoints()
    {
        await using var harness = new WorkerHarness();
        var device = harness.AddDevice(settings: CreateSettings((OpenNettySettings.HomeAssistantLightName, "Inherited name")));
        var endpoint = harness.AddUnitEndpoint(device, "Kitchen", [OpenNettyCapabilities.OnOffSwitchControl]);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal("Inherited name", (string?) GetEntity(endpoint, payload["components"]!.AsObject(), LightDiscriminator)["name"]);
    }

    public static TheoryData<OpenNettyCapability[], string?, string[]> FunctionTypeResolutions => new()
    {
        { [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.BasicShutterControl], OpenNettySettings.FunctionTypes.LightActuator, ["light"] },
        { [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.BasicShutterControl], OpenNettySettings.FunctionTypes.AutomationActuator, ["cover"] },
        { [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.BasicShutterControl], OpenNettySettings.FunctionTypes.ContactState, [] },
        { [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.AdvancedShutterControl], OpenNettySettings.FunctionTypes.LightActuator, ["light"] },
        { [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.AdvancedShutterControl], OpenNettySettings.FunctionTypes.AutomationActuator, ["cover"] },
        { [OpenNettyCapabilities.BasicDimmingControl, OpenNettyCapabilities.BasicShutterControl], OpenNettySettings.FunctionTypes.AutomationActuator, ["cover"] },
        { [OpenNettyCapabilities.BasicDimmingControl, OpenNettyCapabilities.BasicShutterControl], OpenNettySettings.FunctionTypes.LightActuator, [] },
        { [OpenNettyCapabilities.AdvancedDimmingControl, OpenNettyCapabilities.AdvancedShutterControl], OpenNettySettings.FunctionTypes.AutomationActuator, ["cover"] },
        { [OpenNettyCapabilities.OnOffSwitchControl], null, ["light"] },
        { [OpenNettyCapabilities.BasicShutterControl], null, ["cover"] },
        { [OpenNettyCapabilities.AdvancedShutterControl], OpenNettySettings.FunctionTypes.LightActuator, ["cover"] }
    };

    [Theory]
    [MemberData(nameof(FunctionTypeResolutions))]
    public async Task AnnounceDevicesAsync_EndpointWithLightAndShutterCapabilities_UsesFunctionTypeToSelectEntity(
        OpenNettyCapability[] capabilities, string? functionType, string[] expected)
    {
        await using var harness = new WorkerHarness();

        var (_, components) = await DiscoverEndpointAsync(harness, capabilities,
            functionType is null ? null : CreateSettings((OpenNettySettings.FunctionType, functionType)));

        Assert.Equal(expected, components.Select(static item => (string?) item.Value?["platform"]), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AnnounceDevicesAsync_EndpointWithLightAndShutterCapabilitiesWithoutFunctionType_ThrowsInvalidOperationException(string? functionType)
    {
        await using var harness = new WorkerHarness();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await DiscoverEndpointAsync(harness,
            [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.BasicShutterControl],
            functionType is null ? null : CreateSettings((OpenNettySettings.FunctionType, functionType))));

        Assert.Equal(SR.GetResourceString(SR.ID0099), exception.Message);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_CoverEndpoint_UsesDefaultNameAndDeviceClass()
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness, [OpenNettyCapabilities.BasicShutterControl]);

        var cover = GetEntity(endpoint, components, CoverDiscriminator);
        Assert.Equal(GetLocalizedString(SR.ID8004), (string?) cover["name"]);
        Assert.Equal("shutter", (string?) cover["device_class"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_CoverSettings_OverrideNameAndDeviceClass()
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness, [OpenNettyCapabilities.BasicShutterControl], CreateSettings(
            (OpenNettySettings.HomeAssistantCoverName, "Bedroom blind"),
            (OpenNettySettings.HomeAssistantCoverDeviceClass, OpenNettySettings.HomeAssistantDeviceClasses.Covers.Blind)));

        var cover = GetEntity(endpoint, components, CoverDiscriminator);
        Assert.Equal("Bedroom blind", (string?) cover["name"]);
        Assert.Equal("blind", (string?) cover["device_class"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_CoverIconSetting_OverridesIcon()
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness, [OpenNettyCapabilities.BasicShutterControl],
            CreateSettings((OpenNettySettings.HomeAssistantCoverIcon, "mdi:blinds")));

        Assert.Equal("mdi:blinds", (string?) GetEntity(endpoint, components, CoverDiscriminator)["icon"]);
    }

    public static TheoryData<OpenNettyCapability[], string?, string?, string[]> ScenarioEventTypes => new()
    {
        { [OpenNettyCapabilities.ActionScenarioEvent], null, null, ["action", "stop_action"] },
        { [OpenNettyCapabilities.ActionScenarioEvent], null, "doorbell", ["ring", "action", "stop_action"] },
        { [OpenNettyCapabilities.ActionScenarioEvent], null, "button", ["action", "stop_action"] },
        { [OpenNettyCapabilities.DimmingScenarioEvent], null, null, ["dimming"] },
        { [OpenNettyCapabilities.DryContactScenarioEvent], null, null, ["contact_open", "contact_closed"] },
        { [OpenNettyCapabilities.DryContactScenarioEvent], OpenNettySettings.FunctionTypes.ContactState, null, ["contact_open", "contact_closed"] },
        { [OpenNettyCapabilities.OnOffScenarioEvent], null, null, ["switch_on", "switch_off"] },
        { [OpenNettyCapabilities.PressureScenarioEvent], null, null, ["pressure", "release_after_short_pressure", "release_after_extended_pressure", "extended_pressure"] },
        { [OpenNettyCapabilities.PressureScenarioEvent], OpenNettySettings.FunctionTypes.ScheduledScenario, "doorbell", ["ring", "pressure", "release_after_short_pressure", "release_after_extended_pressure", "extended_pressure"] },
        { [OpenNettyCapabilities.PressureScenarioPlusEvent], null, null, ["short_pressure", "start_of_extended_pressure", "extended_pressure", "end_of_extended_pressure"] },
        { [OpenNettyCapabilities.PressureScenarioPlusEvent], OpenNettySettings.FunctionTypes.ScheduledScenarioPlus, "doorbell", ["ring", "short_pressure", "start_of_extended_pressure", "extended_pressure", "end_of_extended_pressure"] },
        { [OpenNettyCapabilities.ProgressiveScenarioEvent], null, null, ["progressive_action"] },
        { [OpenNettyCapabilities.StopUpDownScenarioEvent], null, null, ["shutter_up", "shutter_down", "shutter_stop"] },
        { [OpenNettyCapabilities.TimedScenarioEvent], null, null, ["timed_action"] },
        { [OpenNettyCapabilities.ToggleScenarioEvent], null, null, ["switch_toggle"] },
        { [OpenNettyCapabilities.ActionScenarioEvent, OpenNettyCapabilities.OnOffScenarioEvent], null, null, ["action", "stop_action", "switch_on", "switch_off"] },
        {
            [OpenNettyCapabilities.PressureScenarioEvent, OpenNettyCapabilities.PressureScenarioPlusEvent], null, null,
            ["pressure", "release_after_short_pressure", "release_after_extended_pressure", "extended_pressure", "short_pressure", "start_of_extended_pressure", "end_of_extended_pressure"]
        }
    };

    [Theory]
    [MemberData(nameof(ScenarioEventTypes))]
    public async Task AnnounceDevicesAsync_ScenarioEventCapabilities_PublishesEventEntityWithExpectedTypes(
        OpenNettyCapability[] capabilities, string? functionType, string? deviceClass, string[] expected)
    {
        await using var harness = new WorkerHarness();
        List<(OpenNettySetting, string)> settings = [];

        if (functionType is not null)
        {
            settings.Add((OpenNettySettings.FunctionType, functionType));
        }

        if (deviceClass is not null)
        {
            settings.Add((OpenNettySettings.HomeAssistantScenarioDeviceClass, deviceClass));
        }

        var (endpoint, components) = await DiscoverEndpointAsync(harness, capabilities, CreateSettings([.. settings]));

        var entity = GetEntity(endpoint, components, EventDiscriminator);
        Assert.Single(components);
        Assert.Equal("event", (string?) entity["platform"]);
        Assert.Equal("opennetty/kitchen/scenario", (string?) entity["state_topic"]);
        Assert.Equal("opennetty/kitchen/scenario", (string?) entity["json_attributes_topic"]);
        Assert.Equal("opennetty/kitchen/availability", (string?) entity["availability_topic"]);
        Assert.Equal(expected, entity["event_types"]!.AsArray().Select(static type => (string?) type), StringComparer.Ordinal);
        Assert.Equal(deviceClass, (string?) entity["device_class"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_ScenarioEventEndpoint_UsesDefaultNameAndIcon()
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness, [OpenNettyCapabilities.OnOffScenarioEvent]);

        var entity = GetEntity(endpoint, components, EventDiscriminator);
        Assert.Equal(GetLocalizedString(SR.ID8007), (string?) entity["name"]);
        Assert.Equal("mdi:lightning-bolt", (string?) entity["icon"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_ScenarioSettings_OverrideNameAndIcon()
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness, [OpenNettyCapabilities.OnOffScenarioEvent], CreateSettings(
            (OpenNettySettings.HomeAssistantScenarioName, "Doorbell"),
            (OpenNettySettings.HomeAssistantScenarioIcon, "mdi:doorbell")));

        var entity = GetEntity(endpoint, components, EventDiscriminator);
        Assert.Equal("Doorbell", (string?) entity["name"]);
        Assert.Equal("mdi:doorbell", (string?) entity["icon"]);
    }

    [Theory]
    [InlineData("Dry contact scenario activation", null, 2)]
    [InlineData("Dry contact scenario activation", "Contact state", 2)]
    [InlineData("Dry contact scenario activation", "Scheduled scenario", 0)]
    [InlineData("Dry contact state", null, 2)]
    [InlineData("Dry contact state", "Contact state", 2)]
    [InlineData("Dry contact state", "Light actuator", 0)]
    [InlineData("Pressure scenario activation", null, 4)]
    [InlineData("Pressure scenario activation", "Scheduled scenario", 4)]
    [InlineData("Pressure scenario activation", "Scheduled scenario plus", 0)]
    [InlineData("Pressure scenario plus activation", null, 4)]
    [InlineData("Pressure scenario plus activation", "Scheduled scenario plus", 4)]
    [InlineData("Pressure scenario plus activation", "Scheduled scenario", 0)]
    public async Task AnnounceDevicesAsync_FunctionType_RestrictsScenarioAndContactEntities(string capability, string? functionType, int expected)
    {
        await using var harness = new WorkerHarness();

        var settings = functionType is null
            ? CreateSettings((OpenNettySettings.PushButtonNumbers, "1"))
            : CreateSettings((OpenNettySettings.PushButtonNumbers, "1"), (OpenNettySettings.FunctionType, functionType));

        var (_, components) = await DiscoverEndpointAsync(harness, [new OpenNettyCapability(capability)], settings);

        Assert.Equal(expected, components.Count);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_ScenarioEventExcludedByFunctionType_DoesNotPublishEventEntity()
    {
        await using var harness = new WorkerHarness();

        var (_, components) = await DiscoverEndpointAsync(harness, [OpenNettyCapabilities.DryContactScenarioEvent],
            CreateSettings((OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.ScheduledScenario)));

        Assert.Empty(components);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_DryContactSettings_OverrideDeviceClassAndIcon()
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness, [OpenNettyCapabilities.DryContactState], CreateSettings(
            (OpenNettySettings.HomeAssistantDryContactDeviceClass, "door"),
            (OpenNettySettings.HomeAssistantDryContactIcon, "mdi:door")));

        var entity = GetEntity(endpoint, components, "1aca2cbc-16cb-46df-be3a-ac6a2684910b");
        Assert.Equal("door", (string?) entity["device_class"]);
        Assert.Equal("mdi:door", (string?) entity["icon"]);
    }

    [Theory]
    [InlineData("1", new[] { 1 })]
    [InlineData("1,3", new[] { 1, 3 })]
    [InlineData("2, 4,6", new[] { 2, 4, 6 })]
    public async Task AnnounceDevicesAsync_PressureScenarioActivationWithPushButtons_PublishesButtonsForEveryPushButton(string numbers, int[] buttons)
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness,
            [OpenNettyCapabilities.PressureScenarioActivation], CreateSettings((OpenNettySettings.PushButtonNumbers, numbers)));

        Assert.Equal(buttons.Length * 4, components.Count);

        foreach (var button in buttons)
        {
            var suffix = button.ToString(CultureInfo.InvariantCulture);

            AssertScenarioButton(endpoint, components, "be2887c3-f4ae-4935-bad6-1ffb1227d28b", suffix, SR.ID8011, $$"""{"event_type":"pressure","scenario_type":"basic","button":{{suffix}}}""");
            AssertScenarioButton(endpoint, components, "81e3f75a-fab3-4842-bb5e-1531d20290dd", suffix, SR.ID8012, $$"""{"event_type":"release_after_short_pressure","scenario_type":"evolved","button":{{suffix}}}""");
            AssertScenarioButton(endpoint, components, "3f069e7f-7c8f-4730-b067-9a944f61700b", suffix, SR.ID8013, $$"""{"event_type":"release_after_extended_pressure","scenario_type":"evolved","button":{{suffix}}}""");
            AssertScenarioButton(endpoint, components, "23e04eeb-8b35-44c8-87da-aed3829ce07d", suffix, SR.ID8014, $$"""{"event_type":"extended_pressure","scenario_type":"evolved","button":{{suffix}}}""");
        }
    }

    [Theory]
    [InlineData("1", new[] { 1 })]
    [InlineData("5,7", new[] { 5, 7 })]
    public async Task AnnounceDevicesAsync_PressureScenarioPlusActivationWithPushButtons_PublishesButtonsForEveryPushButton(string numbers, int[] buttons)
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness,
            [OpenNettyCapabilities.PressureScenarioPlusActivation], CreateSettings((OpenNettySettings.PushButtonNumbers, numbers)));

        Assert.Equal(buttons.Length * 4, components.Count);

        foreach (var button in buttons)
        {
            var suffix = button.ToString(CultureInfo.InvariantCulture);

            AssertScenarioButton(endpoint, components, "63a92ba4-bec3-453e-a44a-9219e4f4d478", suffix, SR.ID8019, $$"""{"event_type":"short_pressure","scenario_type":"plus","button":{{suffix}}}""");
            AssertScenarioButton(endpoint, components, "e524f94b-9862-4da4-8f57-82c220e4560c", suffix, SR.ID8020, $$"""{"event_type":"start_of_extended_pressure","scenario_type":"plus","button":{{suffix}}}""");
            AssertScenarioButton(endpoint, components, "bf90ede8-9078-4817-8ec4-ef762c3e2077", suffix, SR.ID8014, $$"""{"event_type":"extended_pressure","scenario_type":"plus","button":{{suffix}}}""");
            AssertScenarioButton(endpoint, components, "73ff2263-9962-443a-89a1-f209fba81948", suffix, SR.ID8021, $$"""{"event_type":"end_of_extended_pressure","scenario_type":"plus","button":{{suffix}}}""");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(",")]
    public async Task AnnounceDevicesAsync_PressureScenarioActivationWithoutPushButtons_DoesNotPublishAnyButton(string numbers)
    {
        await using var harness = new WorkerHarness();

        var (_, components) = await DiscoverEndpointAsync(harness,
            [OpenNettyCapabilities.PressureScenarioActivation], CreateSettings((OpenNettySettings.PushButtonNumbers, numbers)));

        Assert.Empty(components);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",")]
    public async Task AnnounceDevicesAsync_PressureScenarioPlusActivationWithoutPushButtons_PublishesGenericButtons(string numbers)
    {
        await using var harness = new WorkerHarness();

        var (_, components) = await DiscoverEndpointAsync(harness,
            [OpenNettyCapabilities.PressureScenarioPlusActivation], CreateSettings((OpenNettySettings.PushButtonNumbers, numbers)));

        Assert.Equal(4, components.Count);
        Assert.All(components, static item => Assert.DoesNotContain("\"button\"", (string?) item.Value?["payload_press"], StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Pressure scenario activation")]
    [InlineData("Pressure scenario plus activation")]
    public async Task AnnounceDevicesAsync_InvalidPushButtonNumbers_ThrowsFormatException(string capability)
    {
        await using var harness = new WorkerHarness();

        await Assert.ThrowsAsync<FormatException>(async () => await DiscoverEndpointAsync(harness,
            [new OpenNettyCapability(capability)], CreateSettings((OpenNettySettings.PushButtonNumbers, "1,x"))));
    }

    [Fact]
    public async Task ProcessMessagesAsync_GenericPressureScenarioPlusButtonPayloads_DispatchScenarios()
    {
        await using var harness = new WorkerHarness().Start();
        var device = harness.AddDevice();
        harness.AddUnitEndpoint(device, "Kitchen", [OpenNettyCapabilities.PressureScenarioPlusActivation]);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        foreach (var component in payload["components"]!.AsObject())
        {
            await harness.ProcessAsync(CreateMessage("opennetty/kitchen/scenario/set", (string?) component.Value?["payload_press"]));
        }

        Assert.Empty(harness.Logger.Entries.Drain());
        Assert.Equal(4, harness.ControllerCalls.Count);
    }

    public static TheoryData<OpenNettyCapability[], string, string[], string[], bool> SelectEntities => new()
    {
        {
            [OpenNettyCapabilities.PilotWireControl], "205a01a1-ba4c-4e9b-a19a-c1589c445cbb",
            ["comfort", "comfort-1", "comfort-2", "eco", "frost_protection"],
            [SR.ID8040, SR.ID8041, SR.ID8042, SR.ID8043, SR.ID8044], true
        },
        {
            [OpenNettyCapabilities.PilotWireDerogation], "f5f57920-d758-4ca8-8161-2614d4abeef0",
            [
                "none", "comfort", "comfort:4h", "comfort:8h", "comfort-1", "comfort-1:4h", "comfort-1:8h", "comfort-2", "comfort-2:4h",
                "comfort-2:8h", "eco", "eco:4h", "eco:8h", "frost_protection", "frost_protection:4h", "frost_protection:8h"
            ],
            [
                SR.ID8046, SR.ID8047, SR.ID8048, SR.ID8049, SR.ID8050, SR.ID8051, SR.ID8052, SR.ID8053,
                SR.ID8054, SR.ID8055, SR.ID8056, SR.ID8057, SR.ID8058, SR.ID8059, SR.ID8060, SR.ID8061
            ], true
        },
        {
            [OpenNettyCapabilities.WaterHeating], "733d9bf6-fd89-4ce1-bd71-d12a1c6a846e",
            ["automatic", "forced_on", "forced_off"], [SR.ID8082, SR.ID8083, SR.ID8084], true
        },
        {
            [OpenNettyCapabilities.SmartMeterIndexes], "e07f0687-6ca1-47d9-a5b7-b20c0e79775a",
            ["base", "peak/off_peak", "tempo"], [SR.ID8070, SR.ID8071, SR.ID8072], false
        },
        {
            [OpenNettyCapabilities.SmartMeterInformation], "31eda1f3-343f-4cd7-9f56-ea792fcaec7f",
            ["peak", "off_peak"], [SR.ID8075, SR.ID8076], false
        },
        {
            [OpenNettyCapabilities.WirelessBurglarAlarmState], "2dd476d5-a35a-442a-a3f2-4c2621dcf375",
            ["disarmed", "armed", "partially_armed", "exit_delay_elapsed", "triggered", "event_detected"],
            [SR.ID8088, SR.ID8089, SR.ID8090, SR.ID8091, SR.ID8092, SR.ID8093], false
        }
    };

    [Theory]
    [MemberData(nameof(SelectEntities))]
    public async Task AnnounceDevicesAsync_EnumerationEntities_MapPayloadsToLocalizedOptions(
        OpenNettyCapability[] capabilities, string discriminator, string[] keys, string[] resources, bool hasCommandTemplate)
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness, capabilities);

        var entity = GetEntity(endpoint, components, discriminator);
        var names = resources.Select(static resource => GetLocalizedString(resource)).ToArray();

        Assert.Equal(names, entity["options"]!.AsArray().Select(static option => (string?) option), StringComparer.Ordinal);
        Assert.Equal(CreateMapTemplate(keys.Zip(names, static (key, name) => (key, name))), NormalizeLineEndings((string?) entity["value_template"]));

        if (hasCommandTemplate)
        {
            Assert.Equal(CreateMapTemplate(keys.Zip(names, static (key, name) => (name, key))), NormalizeLineEndings((string?) entity["command_template"]));
        }

        else
        {
            Assert.False(entity.ContainsKey("command_template"));
        }
    }

    [Fact]
    public async Task AnnounceDevicesAsync_UICultureOption_EscapesQuotesInLocalizedTemplateOptions()
    {
        await using var harness = new WorkerHarness(static options => options.HomeAssistantDiscoveryUICulture = CultureInfo.GetCultureInfo("fr"));

        var (endpoint, components) = await DiscoverEndpointAsync(harness, [OpenNettyCapabilities.PilotWireDerogation]);

        var entity = GetEntity(endpoint, components, "f5f57920-d758-4ca8-8161-2614d4abeef0");
        var name = GetLocalizedString(SR.ID8047, "fr");
        var options = entity["options"]!.AsArray().Select(static option => (string?) option).ToList();

        Assert.Contains("'", name, StringComparison.Ordinal);
        Assert.Contains(name, options, StringComparer.Ordinal);
        Assert.Contains($"'comfort': '{name.Replace("'", "\\'", StringComparison.Ordinal)}'", NormalizeLineEndings((string?) entity["value_template"]), StringComparison.Ordinal);
        Assert.Contains($"'{name.Replace("'", "\\'", StringComparison.Ordinal)}': 'comfort'", NormalizeLineEndings((string?) entity["command_template"]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_ZigbeeStateEntities_MapPayloadsToBinaryStates()
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness,
            [OpenNettyCapabilities.ZigbeeBinding, OpenNettyCapabilities.ZigbeeNetworkManagement]);

        Assert.Equal(
            "{% set map = {\n    'canceled': 'OFF',\n    'closed': 'OFF',\n    'open': 'ON'\n} %}\n{{ map[value] }}",
            NormalizeLineEndings((string?) GetEntity(endpoint, components, "ee8cc336-01cb-4485-a377-dc5603c64a13")["value_template"]));
        Assert.Equal(
            "{% set map = {\n    'closed': 'OFF',\n    'created': 'ON',\n    'joined': 'ON',\n    'left': 'OFF',\n    'open': 'ON'\n} %}\n{{ map[value] }}",
            NormalizeLineEndings((string?) GetEntity(endpoint, components, "da843c15-49d4-4a14-a7cb-4806783cd8a0")["value_template"]));
    }

    [Fact]
    public async Task AnnounceDevicesAsync_UICultureOption_LocalizesEntityNames()
    {
        await using var harness = new WorkerHarness(static options => options.HomeAssistantDiscoveryUICulture = CultureInfo.GetCultureInfo("fr"));

        var (endpoint, components) = await DiscoverEndpointAsync(harness,
            [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.OnOffSwitchState]);

        Assert.Equal(GetLocalizedString(SR.ID8001, "fr"), (string?) GetEntity(endpoint, components, LightDiscriminator)["name"]);
        Assert.Equal(GetLocalizedString(SR.ID8002, "fr"), (string?) GetEntity(endpoint, components, "3a10d925-c599-41a9-8a7e-30a04aefec86")["name"]);
        Assert.NotEqual(GetLocalizedString(SR.ID8001), GetLocalizedString(SR.ID8001, "fr"), StringComparer.Ordinal);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_UICultureSetting_LocalizesEntityNames()
    {
        await using var harness = new WorkerHarness();
        var device = harness.AddDevice(settings: CreateSettings((OpenNettySettings.HomeAssistantDiscoveryUICulture, "fr")));
        var endpoint = harness.AddUnitEndpoint(device, "Kitchen", [OpenNettyCapabilities.OnOffSwitchControl]);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        Assert.Equal(GetLocalizedString(SR.ID8001, "fr"), (string?) GetEntity(endpoint, payload["components"]!.AsObject(), LightDiscriminator)["name"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_MqttTopicSetting_IsUsedToBuildEntityTopics()
    {
        await using var harness = new WorkerHarness(static options => options.RootTopic = "home");

        var (endpoint, components) = await DiscoverEndpointAsync(harness,
            [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.OnOffSwitchState],
            CreateSettings((OpenNettySettings.MqttTopic, "lights/kitchen")));

        AssertEntities(endpoint, components, "home/lights/kitchen",
            "light;feb44223-4814-4652-933c-53dbbaabac3f;command_topic={topic}/switch_state/set;state_topic={topic}/switch_state",
            Button("3a10d925-c599-41a9-8a7e-30a04aefec86", "ID8002", "switch_state/get", properties: "entity_category=diagnostic"));
    }

    [Fact]
    public async Task AnnounceDevicesAsync_EndpointName_IsLowercasedToBuildDefaultEntityTopics()
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness, [OpenNettyCapabilities.OnOffSwitchControl], name: "Kitchen Island");

        Assert.Equal("opennetty/kitchen island/switch_state/set", (string?) GetEntity(endpoint, components, LightDiscriminator)["command_topic"]);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_Entities_AreKeyedByTheirUniqueIdentifier()
    {
        await using var harness = new WorkerHarness();
        var device = harness.AddDevice();
        var first = harness.AddUnitEndpoint(device, "First", [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.OnOffSwitchState]);
        var second = harness.AddUnitEndpoint(device, "Second", [OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.OnOffSwitchState]);

        var components = ParsePayload(Assert.Single(await harness.AnnounceAsync()))["components"]!.AsObject();

        Assert.Equal(4, components.Count);
        Assert.All(components, static item => Assert.Equal($"entity_{(string?) item.Value?["unique_id"]}", item.Key));
        Assert.Equal(4, components.Select(static item => (string?) item.Value?["unique_id"]).Distinct(StringComparer.Ordinal).Count());
        Assert.NotEqual(
            (string?) GetEntity(first, components, LightDiscriminator)["unique_id"],
            (string?) GetEntity(second, components, LightDiscriminator)["unique_id"],
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task AnnounceDevicesAsync_UniqueIdentifier_IsStableAcrossAnnouncements()
    {
        await using var harness = new WorkerHarness();
        var device = harness.AddDevice();
        harness.AddUnitEndpoint(device, "Kitchen", [OpenNettyCapabilities.OnOffSwitchControl]);

        var first = Assert.Single(await harness.AnnounceAsync());
        var second = Assert.Single(await harness.AnnounceAsync());

        Assert.Equal(first.ConvertPayloadToString(), second.ConvertPayloadToString());
    }

    [Fact]
    public async Task AnnounceDevicesAsync_UniqueIdentifier_UsesBase64UrlEncodedXxHash128()
    {
        await using var harness = new WorkerHarness();

        var (endpoint, components) = await DiscoverEndpointAsync(harness, [OpenNettyCapabilities.OnOffSwitchControl]);

        var identifier = (string?) GetEntity(endpoint, components, LightDiscriminator)["unique_id"];

        Assert.NotNull(identifier);
        Assert.Equal(22, identifier.Length);
        Assert.Matches("^[A-Za-z0-9_-]{22}$", identifier);
    }

    private static string CreateMapTemplate(IEnumerable<(string Key, string Value)> mappings)
        => $"{{% set map = {{\n{string.Join(",\n", mappings.Select(static mapping => $"    '{Escape(mapping.Key)}': '{Escape(mapping.Value)}'"))}\n}} %}}\n{{{{ map[value] }}}}";

    private static string Escape(string value) => value.Replace("'", "\\'", StringComparison.Ordinal);

    private static string NormalizeLineEndings(string? value) => value!.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static void AssertScenarioButton(
        OpenNettyEndpoint endpoint, JsonObject components, string discriminator, string suffix, string resource, string payload)
    {
        var button = GetEntity(endpoint, components, discriminator, suffix);

        Assert.Equal("button", (string?) button["platform"]);
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, GetLocalizedString(resource), suffix), (string?) button["name"]);
        Assert.Equal("opennetty/kitchen/scenario/set", (string?) button["command_topic"]);
        Assert.Equal(payload, (string?) button["payload_press"]);
    }

    private static string Button(string discriminator, string name, string command, string payload = "", params string[] properties)
        => string.Join(';',
        [
            "button",
            discriminator,
            $"name=#{name}",
            $"command_topic={{topic}}/{command}",
            $"payload_press={payload}",
            .. properties
        ]);

    private static string EnergySensor(string discriminator, string name, string attribute, string key)
        => string.Join(';',
        [
            "sensor",
            discriminator,
            "device_class=energy",
            "state_class=total_increasing",
            "unit_of_measurement=kWh",
            "suggested_display_precision=0",
            $"name=#{name}",
            $"state_topic={{topic}}/{attribute}",
            $"value_template={{{{ value_json.{key} }}}}"
        ]);

    private static async Task<(OpenNettyEndpoint Endpoint, JsonObject Components)> DiscoverEndpointAsync(
        WorkerHarness harness,
        IEnumerable<OpenNettyCapability> capabilities,
        ImmutableDictionary<OpenNettySetting, string>? settings = null,
        string name = "Kitchen")
    {
        var device = harness.AddDevice();
        var endpoint = harness.AddUnitEndpoint(device, name, capabilities, settings);

        var payload = ParsePayload(Assert.Single(await harness.AnnounceAsync()));

        return (endpoint, payload["components"]!.AsObject());
    }

    private static JsonObject GetEntity(OpenNettyEndpoint endpoint, JsonObject components, string discriminator, string? suffix = null)
        => Assert.IsType<JsonObject>(components[$"entity_{ComputeUniqueId(endpoint, discriminator, suffix)}"]);

    private static void AssertEntities(OpenNettyEndpoint endpoint, JsonObject components, string topic, params string[] expected)
    {
        Assert.Equal(expected.Length, components.Count);

        foreach (var definition in expected)
        {
            var parts = definition.Split(';');
            var component = GetEntity(endpoint, components, parts[1]);

            Assert.Equal(parts[0], (string?) component["platform"]);
            Assert.Equal(ComputeUniqueId(endpoint, parts[1]), (string?) component["unique_id"]);
            Assert.Equal($"{topic}/availability", (string?) component["availability_topic"]);

            foreach (var property in parts.Skip(2))
            {
                if (property.StartsWith('!'))
                {
                    Assert.False(component.ContainsKey(property[1..]), $"The property '{property[1..]}' must not be present.");
                    continue;
                }

                var separator = property.IndexOf('=', StringComparison.Ordinal);
                var key = property[..separator];
                var value = property[(separator + 1)..].Replace("{topic}", topic, StringComparison.Ordinal);

                if (value.StartsWith("#ID", StringComparison.Ordinal))
                {
                    value = GetLocalizedString(value[1..]);
                }

                Assert.True(component.ContainsKey(key), $"The property '{key}' must be present.");
                Assert.Equal(value, Render(component[key]));
            }
        }
    }

    private static string? Render(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value when value.TryGetValue(out string? text) => text,

        _ => node.ToJsonString()
    };

    private static JsonObject ParsePayload(MqttApplicationMessage message)
        => JsonNode.Parse(message.ConvertPayloadToString())!.AsObject();

    private static string GetLocalizedString(string name, string culture = "en")
        => SR.ResourceManager.GetString(name, CultureInfo.GetCultureInfo(culture))!;

    private static string ComputeUniqueId(OpenNettyEndpoint endpoint, string discriminator, string? suffix = null)
    {
        var hash = new XxHash128();
        hash.Append(MemoryMarshal.AsBytes<char>(endpoint.Name));
        hash.Append(Encoding.UTF8.GetBytes(discriminator + suffix));

        return Base64Url.EncodeToString(hash.GetCurrentHash());
    }

    private static void AssertCalls(WorkerHarness harness, params (string Method, object?[] Arguments)[] expected)
    {
        var invocations = harness.ControllerInvocations;

        Assert.Equal(expected.Length, invocations.Count);

        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].Method, invocations[index].Method);
            Assert.Equal<object?>(expected[index].Arguments, invocations[index].Arguments);
        }
    }

    private static void AssertSingleCall(WorkerHarness harness, OpenNettyEndpoint endpoint, string method, params object?[] arguments)
    {
        var invocation = Assert.Single(harness.ControllerInvocations);

        Assert.Equal(method, invocation.Method);
        Assert.Equal<object?>([endpoint, .. arguments], invocation.Arguments);
    }

    private static void AssertInvalidDataLogged(WorkerHarness harness)
    {
        var entry = Assert.Single(harness.Logger.Entries.Drain());

        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(6018, entry.EventId.Id);
        Assert.Equal(SR.GetResourceString(SR.ID0068), Assert.IsType<InvalidDataException>(entry.Exception).Message);
    }

    private static void AssertReport(MqttApplicationMessage message, string topic, string? payload, string? contentType = null)
    {
        Assert.Equal(topic, message.Topic);
        Assert.Equal(payload, message.ConvertPayloadToString());
        Assert.Equal(contentType, message.ContentType);
        Assert.True(message.Retain);
        Assert.Equal(MqttQualityOfServiceLevel.ExactlyOnce, message.QualityOfServiceLevel);
        Assert.Equal(MqttPayloadFormatIndicator.CharacterData, message.PayloadFormatIndicator);
    }

    private static MqttApplicationMessage CreateMessage(
        string topic, string? payload = null, string? responseTopic = null, byte[]? correlationData = null)
    {
        var builder = new MqttApplicationMessageBuilder().WithTopic(topic);

        if (payload is not null)
        {
            builder.WithPayload(payload);
        }

        if (responseTopic is not null)
        {
            builder.WithResponseTopic(responseTopic);
        }

        if (correlationData is not null)
        {
            builder.WithCorrelationData(correlationData);
        }

        return builder.Build();
    }

    private static ImmutableDictionary<OpenNettySetting, string> CreateSettings(
        params (OpenNettySetting Setting, string Value)[] settings)
        => settings.ToImmutableDictionary(static item => item.Setting, static item => item.Value);

    private sealed class WorkerHarness : IAsyncDisposable
    {
        private readonly Channel<int> _barriers = Channel.CreateUnbounded<int>();
        private readonly Channel<MqttApplicationMessage> _messages = Channel.CreateUnbounded<MqttApplicationMessage>();
        private readonly CancellationTokenSource _source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        private string _barrierMethod = nameof(OpenNettyController.GetWaterHeaterStateAsync);
        private int _barrierCounter;
        private Task? _run;

        public WorkerHarness(Action<OpenNettyMqttOptions>? configure = null)
        {
            MqttOptions = new OpenNettyMqttOptions
            {
                HomeAssistantDiscoveryRootTopic = "homeassistant",
                HomeAssistantDiscoveryUICulture = CultureInfo.GetCultureInfo("en"),
                RootTopic = "opennetty"
            };

            configure?.Invoke(MqttOptions);

            var monitor = Substitute.For<IOptionsMonitor<OpenNettyMqttOptions>>();
            monitor.CurrentValue.Returns(MqttOptions);

            Manager = new WorkerManager(Options);
            Controller = Substitute.For<OpenNettyController>(Manager, Substitute.For<IOpenNettyService>());
            Logger = new WorkerLogger();
            Client = Substitute.For<IManagedMqttClient>();

            Client.EnqueueAsync(Arg.Any<MqttApplicationMessage>()).Returns(call =>
            {
                Capture(call.Arg<MqttApplicationMessage>());
                return Task.CompletedTask;
            });

            Controller.EnumerateBrightnessAsync(Arg.Any<OpenNettyEndpoint>(), Arg.Any<CancellationToken>())
                .Returns(static _ => AsyncEnumerable.Empty<(OpenNettyEndpoint Endpoint, byte Level)>());
            Controller.EnumerateShutterPositionsAsync(Arg.Any<OpenNettyEndpoint>(), Arg.Any<CancellationToken>())
                .Returns(static _ => AsyncEnumerable.Empty<(OpenNettyEndpoint Endpoint, byte? Position)>());
            Controller.EnumerateShutterStatesAsync(Arg.Any<OpenNettyEndpoint>(), Arg.Any<CancellationToken>())
                .Returns(static _ => AsyncEnumerable.Empty<(OpenNettyEndpoint Endpoint, OpenNettyModels.Automation.ShutterState State)>());
            Controller.EnumerateSwitchStatesAsync(Arg.Any<OpenNettyEndpoint>(), Arg.Any<CancellationToken>())
                .Returns(static _ => AsyncEnumerable.Empty<(OpenNettyEndpoint Endpoint, OpenNettyModels.Lighting.SwitchState State)>());

            Worker = new OpenNettyMqttWorker(Controller, Logger, Manager, monitor);

            AddEndpoint(BarrierEndpointName);
        }

        public IManagedMqttClient Client { get; }

        public OpenNettyController Controller { get; }

        public WorkerLogger Logger { get; }

        public WorkerManager Manager { get; }

        public OpenNettyMqttOptions MqttOptions { get; }

        public OpenNettyOptions Options { get; } = new();

        public WorkerRecorder<MqttApplicationMessage> Published { get; } = new();

        public OpenNettyMqttWorker Worker { get; }

        public IReadOnlyList<(string Method, object?[] Arguments)> ControllerInvocations => [.. Controller.ReceivedCalls()
            .Where(call => !string.Equals(call.GetMethodInfo().Name, _barrierMethod, StringComparison.Ordinal))
            .Select(static call => (call.GetMethodInfo().Name, call.GetArguments().Where(static argument => argument is not CancellationToken).ToArray()))];

        public IReadOnlyList<string> ControllerCalls => [.. ControllerInvocations.Select(static invocation => invocation.Method)];

        public OpenNettyEndpoint AddEndpoint(
            string name,
            IEnumerable<OpenNettyCapability>? capabilities = null,
            ImmutableDictionary<OpenNettySetting, string>? settings = null,
            OpenNettyProtocol protocol = OpenNettyProtocol.Scs,
            OpenNettyAddress? address = null,
            OpenNettyUnit? unit = null,
            OpenNettyGateway? gateway = null)
        {
            var endpoint = new OpenNettyEndpoint
            {
                Address = address,
                Capabilities = [.. capabilities ?? []],
                Gateway = gateway,
                Name = name,
                Protocol = protocol,
                Settings = settings ?? [],
                Unit = unit
            };

            Options.Endpoints.Add(endpoint);

            return endpoint;
        }

        public OpenNettyDevice AddDevice(
            OpenNettyBrand brand = OpenNettyBrand.BTicino,
            string model = "F411U1",
            string name = "Kitchen module",
            OpenNettyDeviceIdentifier? identifier = null,
            ImmutableDictionary<OpenNettySetting, string>? settings = null,
            OpenNettyGateway? gateway = null)
        {
            var device = OpenNettyDevice.Create(brand, model, name, identifier);

            if (settings is not null)
            {
                device.Settings = settings;
            }

            if (gateway is not null)
            {
                device.Gateway = gateway;
            }

            Options.Devices.Add(device);

            return device;
        }

        public (OpenNettyDevice Device, OpenNettyGateway Gateway) AddGateway(string name = "Main gateway", int port = 20000)
        {
            var device = AddDevice(OpenNettyBrand.BTicino, "F454", name);
            var gateway = OpenNettyGateway.Create(device, new IPEndPoint(IPAddress.Loopback, port));
            device.Gateway = gateway;
            Options.Gateways.Add(gateway);

            return (device, gateway);
        }

        public OpenNettyEndpoint AddUnitEndpoint(
            OpenNettyDevice device,
            string name,
            IEnumerable<OpenNettyCapability>? capabilities = null,
            ImmutableDictionary<OpenNettySetting, string>? settings = null,
            byte unit = 0)
            => AddEndpoint(name, capabilities, settings, device.Definition.Protocol, unit: device.GetUnit(unit), gateway: device.Gateway);

        public bool IsCompleted => _run is { IsCompleted: true };

        public WorkerHarness Start(ChannelReader<MqttApplicationMessage>? reader = null)
        {
            _run = Worker.ProcessMessagesAsync(Client, reader ?? _messages.Reader, _source.Token);

            return this;
        }

        public void Post(MqttApplicationMessage message) => Assert.True(_messages.Writer.TryWrite(message));

        public void Complete() => _messages.Writer.Complete();

        public async Task ProcessAsync(
            MqttApplicationMessage message, string barrierAttribute = OpenNettyMqttAttributes.WaterHeaterState)
        {
            _barrierMethod = barrierAttribute switch
            {
                OpenNettyMqttAttributes.WaterHeaterState => nameof(OpenNettyController.GetWaterHeaterStateAsync),
                OpenNettyMqttAttributes.DryContactState  => nameof(OpenNettyController.GetDryContactStateAsync),

                _ => throw new ArgumentOutOfRangeException(nameof(barrierAttribute))
            };

            Post(message);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_source.Token);
            timeout.CancelAfter(WaitTimeout);

            var name = GetBarrierName(message.Topic);
            HashSet<int> pending = [];

            while (true)
            {
                var identifier = Interlocked.Increment(ref _barrierCounter);
                pending.Add(identifier);

                Post(CreateMessage($"{MqttOptions.RootTopic}/{name}/{barrierAttribute}/get",
                    responseTopic: BarrierTopic, correlationData: BitConverter.GetBytes(identifier)));

                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                attempt.CancelAfter(BarrierRetryInterval);

                try
                {
                    int received;

                    do
                    {
                        received = await _barriers.Reader.ReadAsync(attempt.Token);
                    }

                    while (!pending.Contains(received));

                    return;
                }

                catch (OperationCanceledException) when (!timeout.IsCancellationRequested)
                {
                }
            }

            string GetBarrierName(string topic) => topic.Split('/', StringSplitOptions.RemoveEmptyEntries) switch
            {
                [_, .. string[] names, _, "get" or "set"] when names.Length is > 0 => string.Join('/', names),

                _ => BarrierEndpointName
            };
        }

        public async Task StopAsync()
        {
            await _source.CancelAsync();

            if (_run is not null)
            {
                await _run.WaitAsync(WaitTimeout, TimeProvider.System, TestContext.Current.CancellationToken);
            }
        }

        public async Task<IReadOnlyList<MqttApplicationMessage>> AnnounceAsync()
        {
            await Worker.AnnounceDevicesAsync(Client, TestContext.Current.CancellationToken);

            return Published.Drain();
        }

        public void Setup<TResult>(Func<OpenNettyController, ValueTask<TResult>> call, TResult result)
        {
            // Note: NSubstitute requires the ValueTask-returning call to be used as the receiver of Returns().
#pragma warning disable CA2012
            call(Controller).Returns(new ValueTask<TResult>(result));
#pragma warning restore CA2012
        }

        public void Setup(Func<OpenNettyController, ValueTask> call, Func<CallInfo, ValueTask> implementation)
        {
            // Note: NSubstitute requires the ValueTask-returning call to be used as the receiver of Returns().
#pragma warning disable CA2012
            call(Controller).Returns(implementation);
#pragma warning restore CA2012
        }

        public void Setup(Func<OpenNettyController, ValueTask> call, Exception exception)
        {
            // Note: NSubstitute requires the ValueTask-returning call to be used as the receiver of Returns().
#pragma warning disable CA2012
            call(Controller).Returns(_ => ValueTask.FromException(exception));
#pragma warning restore CA2012
        }

        public async ValueTask DisposeAsync()
        {
            await _source.CancelAsync();

            if (_run is not null)
            {
                try
                {
                    await _run.WaitAsync(WaitTimeout, TimeProvider.System, CancellationToken.None);
                }

                catch (OperationCanceledException)
                {
                }
            }

            _source.Dispose();
        }

        private void Capture(MqttApplicationMessage message)
        {
            if (string.Equals(message.Topic, BarrierTopic, StringComparison.Ordinal))
            {
                _barriers.Writer.TryWrite(BitConverter.ToInt32(message.CorrelationData));

                return;
            }

            Published.Record(message);
        }
    }

    private sealed class WorkerClosedReader : ChannelReader<MqttApplicationMessage>
    {
        public override bool TryRead([MaybeNullWhen(false)] out MqttApplicationMessage item)
        {
            item = null;

            return false;
        }

        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromException<bool>(new ChannelClosedException());
    }

    private sealed class WorkerFailingReader : ChannelReader<MqttApplicationMessage>
    {
        private readonly TaskCompletionSource _retried = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public Task Retried => _retried.Task;

        public override bool TryRead([MaybeNullWhen(false)] out MqttApplicationMessage item)
        {
            item = null;

            return false;
        }

        public override async ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) is 1)
            {
                throw new InvalidOperationException();
            }

            var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var registration = cancellationToken.Register(static state => ((TaskCompletionSource) state!).SetResult(), canceled);

            _retried.TrySetResult();
            await canceled.Task;

            return false;
        }
    }

    private sealed class WorkerManager : OpenNettyManager
    {
        private readonly SemaphoreSlim _lookups = new(0);

        public WorkerManager(OpenNettyOptions options) : base(CreateMonitor(options))
        {
            Options = options;
        }

        public OpenNettyOptions Options { get; }

        public override async IAsyncEnumerable<OpenNettyEndpoint> EnumerateEndpointsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {            foreach (var endpoint in Options.Endpoints.ToArray())
            {
                yield return endpoint;
            }

            _lookups.Release();

            await Task.CompletedTask;
        }

        public async Task WaitForLookupAsync()
        {
            if (!await _lookups.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken))
            {
                throw new TimeoutException("The endpoint lookup was not performed in a timely manner.");
            }
        }

        private static IOptionsMonitor<OpenNettyOptions> CreateMonitor(OpenNettyOptions options)
        {
            var monitor = Substitute.For<IOptionsMonitor<OpenNettyOptions>>();
            monitor.CurrentValue.Returns(options);

            return monitor;
        }
    }

    private sealed record class WorkerLogEntry(LogLevel Level, EventId EventId, Exception? Exception);

    private sealed class WorkerLogger : ILogger<OpenNettyMqttWorker>
    {
        public WorkerRecorder<WorkerLogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Record(new WorkerLogEntry(logLevel, eventId, exception));
    }

    private sealed class WorkerRecorder<T>
    {
        private readonly Channel<T> _channel = Channel.CreateUnbounded<T>();

        public void Record(T item) => _channel.Writer.TryWrite(item);

        public IReadOnlyList<T> Drain()
        {
            List<T> items = [];

            while (_channel.Reader.TryRead(out var item))
            {
                items.Add(item);
            }

            return items;
        }

        public async Task<IReadOnlyList<T>> WaitAsync(int count)
        {
            List<T> items = [];

            while (items.Count < count)
            {
                items.Add(await ReadAsync(_channel.Reader));
            }

            return items;
        }
    }

    private static async Task<T> ReadAsync<T>(ChannelReader<T> reader)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        source.CancelAfter(WaitTimeout);

        try
        {
            return await reader.ReadAsync(source.Token);
        }

        catch (OperationCanceledException exception) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The expected item was not received in a timely manner.", exception);
        }
    }
}
