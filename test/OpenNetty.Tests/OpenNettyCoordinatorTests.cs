/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Globalization;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyCoordinatorTests
{
    private const int PipelineSubscriptionCount = 10;

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData("controller")]
    [InlineData("events")]
    [InlineData("logger")]
    [InlineData("manager")]
    [InlineData("pipeline")]
    public void Constructor_NullDependency_ThrowsArgumentNullException(string parameterName)
    {
        var manager = new CoordinatorManager();
        var lifetime = Substitute.For<IHostApplicationLifetime>();

        var controller = parameterName is "controller" ? null : Substitute.For<OpenNettyController>(manager, Substitute.For<IOpenNettyService>());
        var events = parameterName is "events" ? null : new OpenNettyEvents(lifetime);
        var logger = parameterName is "logger" ? null : new CoordinatorLogger();
        var pipeline = parameterName is "pipeline" ? null : new CoordinatorPipeline();

        var exception = Assert.Throws<ArgumentNullException>(() => new OpenNettyCoordinator(
            controller!, events!, logger!, parameterName is "manager" ? null! : manager, pipeline!));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void Constructor_ValidDependencies_CreatesHandler()
    {
        var manager = new CoordinatorManager();

        var coordinator = new OpenNettyCoordinator(
            Substitute.For<OpenNettyController>(manager, Substitute.For<IOpenNettyService>()),
            new OpenNettyEvents(Substitute.For<IHostApplicationLifetime>()),
            new CoordinatorLogger(),
            manager,
            new CoordinatorPipeline());

        Assert.IsAssignableFrom<IOpenNettyHandler>(coordinator);
    }

    [Fact]
    public async Task SubscribeAsync_Handler_SubscribesEveryHandlerToThePipeline()
    {
        await using var harness = await CoordinatorHarness.StartAsync();

        Assert.Equal(PipelineSubscriptionCount, harness.Pipeline.ObserverCount);
    }

    [Fact]
    public async Task SubscribeAsync_DisposedSubscription_UnsubscribesEveryHandlerFromThePipeline()
    {
        await using var harness = await CoordinatorHarness.StartAsync();

        await harness.Subscription.DisposeAsync();

        Assert.Equal(0, harness.Pipeline.ObserverCount);
    }

    [Fact]
    public async Task SubscribeAsync_DisposedSubscription_StopsReportingEvents()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);
        await harness.Subscription.DisposeAsync();

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress));

        Assert.Empty(events);
    }

    public static TheoryData<string> UnsupportedMessageKinds => ["StatusRequest", "DimensionRequest", "Acknowledgement"];

    [Theory]
    [MemberData(nameof(UnsupportedMessageKinds))]
    public async Task ReceivedMessage_UnsupportedMessageType_IsIgnored(string kind)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, GetAllCapabilities());

        var events = await harness.ReceiveAsync(CreateUnsupportedMessage(kind));

        Assert.Empty(events);
    }

    [Fact]
    public async Task SentMessage_NitooDimensionRead_IsIgnored()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint(GetAllCapabilities());

        var events = await harness.SendAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["129", "129"]));

        Assert.Empty(events);
    }

    [Fact]
    public async Task SentMessage_ScsDimensionSet_IsIgnored()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, GetAllCapabilities());

        var events = await harness.SendAsync(CreateDimensionSet(
            OpenNettyProtocol.Scs, OpenNettyDimensions.Lighting.DimmerLevelSpeed, ScsPointAddress, values: "150"));

        Assert.Empty(events);
    }

    [Fact]
    public async Task ReceivedMessage_UnsupportedUnitDescription_IsIgnored()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint(GetAllCapabilities());

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["999", "1"]));

        Assert.Empty(events);
    }

    [Fact]
    public async Task ReceivedMessage_CommandWithoutAddress_IsIgnored()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Zigbee, null, GetAllCapabilities());

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Zigbee, OpenNettyCommands.Lighting.On, null));

        Assert.Empty(events);
    }

    [Fact]
    public async Task ReceivedMessages_PublishedSequentially_ReportEventsInOrder()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);

        await harness.PostReceivedAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress));
        await harness.PostReceivedAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.Off, ScsPointAddress));
        await harness.PostReceivedAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress));

        Assert.Equal<OpenNettyEvents.EventArgs>(
        [
            new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On),
            new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.Off),
            new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On)
        ], await harness.CollectEventsAsync());
    }

    [Fact]
    public async Task MessageErrorNotification_IsIgnored()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, GetAllCapabilities());

        var events = await harness.PublishAsync(new OpenNettyNotifications.MessageError
        {
            Exception = new OpenNettyException(OpenNettyErrorCode.ConnectionClosed, "error"),
            Gateway = harness.Gateway,
            Message = CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress),
            Session = harness.CreateSession(),
            Transaction = OpenNettyTransaction.Create()
        });

        Assert.Empty(events);
    }

    [Fact]
    public async Task MessageReadyNotification_IsIgnored()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, GetAllCapabilities());

        var events = await harness.PublishAsync(new OpenNettyNotifications.MessageReady
        {
            Gateway = harness.Gateway,
            Message = CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress),
            Options = harness.Gateway.Options.DefaultTransmissionOptions,
            Transaction = OpenNettyTransaction.Create()
        });

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs, true)]
    [InlineData(OpenNettyProtocol.Scs, false)]
    [InlineData(OpenNettyProtocol.Zigbee, true)]
    [InlineData(OpenNettyProtocol.Zigbee, false)]
    public async Task OnOffCommand_ReceivedByScsOrZigbeeEndpoint_ReportsSwitchState(OpenNettyProtocol protocol, bool on)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var address = CreateAddress(protocol);
        var endpoint = harness.AddEndpoint(protocol, address, [OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateCommand(protocol, GetOnOffCommand(on), address));

        AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, on ? OpenNettyModels.Lighting.SwitchState.On : OpenNettyModels.Lighting.SwitchState.Off)], events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnOffCommand_SentToZigbeeEndpoint_ReportsSwitchState(bool on)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var address = CreateZigbeeAddress();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Zigbee, address, [OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.SendAsync(CreateCommand(OpenNettyProtocol.Zigbee, GetOnOffCommand(on), address));

                AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, on ? OpenNettyModels.Lighting.SwitchState.On : OpenNettyModels.Lighting.SwitchState.Off)], events);
    }

    [Fact]
    public async Task OnOffCommand_SentToScsEndpoint_IsIgnored()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.SendAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(OpenNettyMode.Unicast, true)]
    [InlineData(OpenNettyMode.Unicast, false)]
    [InlineData(OpenNettyMode.Broadcast, true)]
    [InlineData(OpenNettyMode.Broadcast, false)]
    [InlineData(OpenNettyMode.Multicast, true)]
    [InlineData(OpenNettyMode.Multicast, false)]
    public async Task OnOffCommand_ReceivedByNitooEndpoint_ReportsSwitchStateRegardlessOfTransmissionMode(OpenNettyMode mode, bool on)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, GetOnOffCommand(on), CreateNitooAddress(), mode));

                AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, on ? OpenNettyModels.Lighting.SwitchState.On : OpenNettyModels.Lighting.SwitchState.Off)], events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnOffCommand_SentToNitooEndpointInUnicast_ReportsSwitchState(bool on)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.SendAsync(CreateCommand(OpenNettyProtocol.Nitoo, GetOnOffCommand(on), CreateNitooAddress(), OpenNettyMode.Unicast));

                AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, on ? OpenNettyModels.Lighting.SwitchState.On : OpenNettyModels.Lighting.SwitchState.Off)], events);
    }

    [Theory]
    [InlineData(OpenNettyMode.Broadcast)]
    [InlineData(OpenNettyMode.Multicast)]
    public async Task OnOffCommand_SentToNitooEndpointInBroadcastOrMulticast_DoesNotReportSwitchState(OpenNettyMode mode)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.SendAsync(CreateCommand(OpenNettyProtocol.Nitoo, OpenNettyCommands.Lighting.On, CreateNitooAddress(), mode));

        Assert.Empty(events);
    }

    [Fact]
    public async Task OnCommand_ParameterizedCommand_ReportsOnState()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);
        var command = OpenNettyCommands.Lighting.On.WithParameters(["1"]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, command, ScsPointAddress));

                AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On)], events);
    }

    [Fact]
    public async Task OffCommand_ParameterizedCommand_ReportsOffState()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);
        var command = OpenNettyCommands.Lighting.Off.WithParameters(["1"]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, command, ScsPointAddress));

                AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.Off)], events);
    }

    [Fact]
    public async Task OnOffCommand_EndpointWithoutSwitchStateCapability_DoesNotReportSwitchState()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.BasicShutterState]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress));

        Assert.Empty(events);
    }

    [Fact]
    public async Task OnOffCommand_NoEndpointMatchesAddress_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsAreaAddress));

        Assert.Empty(events);
    }

    [Fact]
    public async Task OnOffCommand_MultipleEndpointsMatchAddress_ReportsSwitchStateForEachEndpoint()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var first = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsAreaAddress, [OpenNettyCapabilities.OnOffSwitchState]);
        var second = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsAreaAddress, [OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.Off, ScsAreaAddress));

        AssertEvents(
        [
            new OpenNettyEvents.SwitchStateReportedEventArgs(first, OpenNettyModels.Lighting.SwitchState.Off),
            new OpenNettyEvents.SwitchStateReportedEventArgs(second, OpenNettyModels.Lighting.SwitchState.Off)
        ], events);
    }

    [Fact]
    public async Task OnOffCommand_ReceivedInCommandSession_IsIgnored()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(
            CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress), OpenNettySessionType.Command);

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(OpenNettySessionType.Event)]
    [InlineData(OpenNettySessionType.Generic)]
    public async Task OnOffCommand_ReceivedInNonCommandSession_ReportsSwitchState(OpenNettySessionType sessionType)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(
            CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress), sessionType);

                    AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On)], events);
    }

    [Fact]
    public async Task OnOffCommand_SentInCommandSession_IsIgnored()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.SendAsync(
            CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress), OpenNettySessionType.Command);

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(true, OpenNettyModels.Lighting.SwitchState.On)]
    [InlineData(false, OpenNettyModels.Lighting.SwitchState.Off)]
    public async Task OnOffCommand_AreaOrGeneralAddressAndDefaultSwitchMode_ReportsSwitchState(bool general, OpenNettyModels.Lighting.SwitchState state)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var address = general ? ScsGeneralAddress : ScsAreaAddress;
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, address, [OpenNettyCapabilities.OnOffSwitchState],
            CreateSettings(OpenNettySettings.SwitchMode, OpenNettySettings.SwitchModes.Default));

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, GetOnOffCommand(state is OpenNettyModels.Lighting.SwitchState.On), address));

        AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, state)], events);
    }

    [Theory]
    [InlineData("On")]
    [InlineData("Off")]
    [InlineData("On50")]
    [InlineData("TimedOn1Minute")]
    [InlineData("DimmerLevelSpeed")]
    [InlineData("DimmerStatus")]
    public async Task LightingMessage_AreaAddressAndPushButtonSwitchMode_IsIgnored(string kind)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsAreaAddress,
            [OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.BasicDimmingState],
            CreateSettings(OpenNettySettings.SwitchMode, OpenNettySettings.SwitchModes.PushButton));

        var events = await harness.ReceiveAsync(CreateScsLightingMessage(kind, ScsAreaAddress));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData("On")]
    [InlineData("Off")]
    [InlineData("On50")]
    [InlineData("TimedOn1Minute")]
    [InlineData("DimmerLevelSpeed")]
    [InlineData("DimmerStatus")]
    public async Task LightingMessage_GeneralAddressAndPushButtonSwitchMode_IsIgnored(string kind)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsGeneralAddress,
            [OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.BasicDimmingState],
            CreateSettings(OpenNettySettings.SwitchMode, OpenNettySettings.SwitchModes.PushButton));

        var events = await harness.ReceiveAsync(CreateScsLightingMessage(kind, ScsGeneralAddress));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData("On")]
    [InlineData("Off")]
    [InlineData("On50")]
    [InlineData("TimedOn1Minute")]
    [InlineData("DimmerLevelSpeed")]
    [InlineData("DimmerStatus")]
    public async Task LightingMessage_PointAddressAndPushButtonSwitchMode_ReportsState(string kind)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress,
            [OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.BasicDimmingState],
            CreateSettings(OpenNettySettings.SwitchMode, OpenNettySettings.SwitchModes.PushButton));

        var events = await harness.ReceiveAsync(CreateScsLightingMessage(kind, ScsPointAddress));

        Assert.NotEmpty(events);
    }

    [Fact]
    public async Task OnCommand_NitooEndpointInPushButtonMode_ReportsOnThenOff()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState],
            settings: CreateSettings(OpenNettySettings.SwitchMode, OpenNettySettings.SwitchModes.PushButton));

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, OpenNettyCommands.Lighting.On, CreateNitooAddress()));

        Assert.Equal<OpenNettyEvents.EventArgs>(
        [
            new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On),
            new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.Off)
        ], events);
    }

    [Fact]
    public async Task OffCommand_NitooEndpointInPushButtonMode_ReportsOffOnly()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState],
            settings: CreateSettings(OpenNettySettings.SwitchMode, OpenNettySettings.SwitchModes.PushButton));

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, OpenNettyCommands.Lighting.Off, CreateNitooAddress()));

                    AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.Off)], events);
    }

    [Fact]
    public async Task OnCommand_ScsEndpointInPushButtonMode_ReportsOnOnly()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState],
            CreateSettings(OpenNettySettings.SwitchMode, OpenNettySettings.SwitchModes.PushButton));

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress));

                    AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On)], events);
    }

    [Theory]
    [InlineData(nameof(OpenNettyCapabilities.BasicDimmingState))]
    [InlineData(nameof(OpenNettyCapabilities.AdvancedDimmingState))]
    public async Task OnCommand_NitooEndpointWithDimming_ReportsFullBrightness(string capability)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState, GetCapability(capability)]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, OpenNettyCommands.Lighting.On, CreateNitooAddress()));

        AssertEvents(
        [
            new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On),
            new OpenNettyEvents.BrightnessReportedEventArgs(endpoint, 100)
        ], events);
    }

    [Fact]
    public async Task OffCommand_NitooEndpointWithDimming_DoesNotReportBrightness()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.BasicDimmingState]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, OpenNettyCommands.Lighting.Off, CreateNitooAddress()));

        AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.Off)], events);
    }

    [Fact]
    public async Task OnCommand_ScsEndpointWithDimming_DoesNotReportBrightness()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress,
            [OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.BasicDimmingState]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress));

        AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On)], events);
    }

    [Fact]
    public async Task OnOffCommand_BroadcastToNitooEndpointWithAssociatedUnit_ReportsSwitchStateOnAssociatedEndpoint()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([], unit: 1, associatedUnit: 2);
        var associated = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState], unit: 2);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.Lighting.On, CreateNitooAddress(unit: 1), OpenNettyMode.Broadcast));

        AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(associated, OpenNettyModels.Lighting.SwitchState.On)], events);
    }

    [Fact]
    public async Task OnOffCommand_BroadcastToNitooEndpointWithAssociatedUnit_IgnoresAssociatedEndpointWithoutSwitchState()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([], unit: 1, associatedUnit: 2);
        harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState], unit: 2);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.Lighting.On, CreateNitooAddress(unit: 1), OpenNettyMode.Broadcast));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(OpenNettyMode.Unicast)]
    [InlineData(OpenNettyMode.Multicast)]
    public async Task OnOffCommand_NonBroadcastToNitooEndpointWithAssociatedUnit_DoesNotReportAssociatedEndpoint(OpenNettyMode mode)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([], unit: 1, associatedUnit: 2);
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState], unit: 2);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.Lighting.On, CreateNitooAddress(unit: 1), mode));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnOffCommand_ReceivedByZigbeeEndpointWithScenarioEvent_ReportsOnOffScenario(bool on)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var address = CreateZigbeeAddress();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Zigbee, address, [OpenNettyCapabilities.OnOffScenarioEvent]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Zigbee, GetOnOffCommand(on), address));

                AssertEvents([new OpenNettyEvents.OnOffScenarioReportedEventArgs(endpoint,
            on ? OpenNettyModels.Lighting.OnOffScenarioType.On : OpenNettyModels.Lighting.OnOffScenarioType.Off)], events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnOffCommand_BroadcastToNitooEndpointWithScenarioEvent_ReportsOnOffScenario(bool on)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffScenarioEvent]);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, GetOnOffCommand(on), CreateNitooAddress(), OpenNettyMode.Broadcast));

                    AssertEvents([new OpenNettyEvents.OnOffScenarioReportedEventArgs(endpoint,
            on ? OpenNettyModels.Lighting.OnOffScenarioType.On : OpenNettyModels.Lighting.OnOffScenarioType.Off)], events);
    }

    [Theory]
    [InlineData(OpenNettyMode.Unicast)]
    [InlineData(OpenNettyMode.Multicast)]
    public async Task OnOffCommand_NonBroadcastToNitooEndpointWithScenarioEvent_DoesNotReportOnOffScenario(OpenNettyMode mode)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffScenarioEvent]);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.Lighting.On, CreateNitooAddress(), mode));

        Assert.Empty(events);
    }

    [Fact]
    public async Task OnOffCommand_ScsEndpointWithScenarioEvent_DoesNotReportOnOffScenario()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffScenarioEvent]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(OpenNettyMode.Broadcast)]
    [InlineData(OpenNettyMode.Multicast)]
    public async Task OnOffCommand_NitooEndpointWithScenarios_ReportsSwitchStateOnLinkedEndpointsOnly(OpenNettyMode mode)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffScenarioEvent], unit: 1, scenarios:
        [
            new OpenNettyScenario { EndpointName = "linked", FunctionCode = 100 },
            new OpenNettyScenario { EndpointName = "too-high-function-code", FunctionCode = 105 },
            new OpenNettyScenario { EndpointName = "without-switch-state", FunctionCode = 100 },
            new OpenNettyScenario { EndpointName = "unknown", FunctionCode = 100 }
        ]);
        var linked = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState], unit: 2, name: "linked");
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState], unit: 3, name: "too-high-function-code");
        harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState], unit: 4, name: "without-switch-state");

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.Lighting.Off, CreateNitooAddress(unit: 1), mode));

        var switchStates = events.OfType<OpenNettyEvents.SwitchStateReportedEventArgs>();
        Assert.Equal([new OpenNettyEvents.SwitchStateReportedEventArgs(linked, OpenNettyModels.Lighting.SwitchState.Off)], switchStates);
    }

    [Fact]
    public async Task OnOffCommand_UnicastToNitooEndpointWithScenarios_DoesNotReportLinkedEndpoints()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffScenarioEvent], unit: 1, scenarios:
            [new OpenNettyScenario { EndpointName = "linked", FunctionCode = 100 }]);
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState], unit: 2, name: "linked");

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.Lighting.On, CreateNitooAddress(unit: 1), OpenNettyMode.Unicast));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(40)]
    [InlineData(50)]
    [InlineData(60)]
    [InlineData(70)]
    [InlineData(80)]
    [InlineData(90)]
    [InlineData(100)]
    public async Task OnPercentCommand_EndpointWithBasicDimming_ReportsOnStateAndBrightness(int level)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress,
            [OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.BasicDimmingState]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, GetOnPercentCommand(level), ScsPointAddress));

        AssertEvents(
        [
            new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On),
            new OpenNettyEvents.BrightnessReportedEventArgs(endpoint, (byte) level)
        ], events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnPercentCommand_EndpointWithAdvancedDimming_ReportsOnStateOnly(bool alsoBasic)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        List<OpenNettyCapability> capabilities = [OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.AdvancedDimmingState];
        if (alsoBasic)
        {
            capabilities.Add(OpenNettyCapabilities.BasicDimmingState);
        }

        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, capabilities);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On30, ScsPointAddress));

                AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On)], events);
    }

    [Fact]
    public async Task OnPercentCommand_EndpointWithoutDimming_ReportsOnStateOnly()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On50, ScsPointAddress));

                AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On)], events);
    }

    [Fact]
    public async Task OnPercentCommand_EndpointWithoutSwitchState_ReportsBrightnessOnly()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.BasicDimmingState]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On70, ScsPointAddress));

        AssertEvents([new OpenNettyEvents.BrightnessReportedEventArgs(endpoint, 70)], events);
    }

    [Theory]
    [InlineData(nameof(OpenNettyCommands.Lighting.TimedOn1Minute))]
    [InlineData(nameof(OpenNettyCommands.Lighting.TimedOn2Minutes))]
    [InlineData(nameof(OpenNettyCommands.Lighting.TimedOn3Minutes))]
    [InlineData(nameof(OpenNettyCommands.Lighting.TimedOn4Minutes))]
    [InlineData(nameof(OpenNettyCommands.Lighting.TimedOn5Minutes))]
    [InlineData(nameof(OpenNettyCommands.Lighting.TimedOn15Minutes))]
    [InlineData(nameof(OpenNettyCommands.Lighting.TimedOn30Seconds))]
    [InlineData(nameof(OpenNettyCommands.Lighting.TimedOn500Milliseconds))]
    public async Task TimedOnCommand_EndpointWithSwitchState_ReportsOnState(string name)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, GetTimedOnCommand(name), ScsPointAddress));

                AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On)], events);
    }

    [Fact]
    public async Task TimedOnCommand_EndpointWithoutSwitchState_DoesNotReportSwitchState()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.BasicDimmingState]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.TimedOn1Minute, ScsPointAddress));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(false, "150", OpenNettyModels.Lighting.SwitchState.On, 50)]
    [InlineData(false, "200", OpenNettyModels.Lighting.SwitchState.On, 100)]
    [InlineData(false, "101", OpenNettyModels.Lighting.SwitchState.On, 1)]
    [InlineData(false, "100", OpenNettyModels.Lighting.SwitchState.Off, null)]
    [InlineData(true, "150", OpenNettyModels.Lighting.SwitchState.On, 50)]
    [InlineData(true, "100", OpenNettyModels.Lighting.SwitchState.Off, null)]
    public async Task DimmerRead_EndpointWithDimming_ReportsSwitchStateAndBrightness(
        bool status, string value, OpenNettyModels.Lighting.SwitchState state, int? brightness)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress,
            [OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.BasicDimmingState]);
        var dimension = status ? OpenNettyDimensions.Lighting.DimmerStatus : OpenNettyDimensions.Lighting.DimmerLevelSpeed;

        var events = await harness.ReceiveAsync(CreateDimensionRead(OpenNettyProtocol.Scs, dimension, ScsPointAddress, values: value));

                List<OpenNettyEvents.EventArgs> expected = [new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, state)];
        if (brightness is int level)
        {
            expected.Add(new OpenNettyEvents.BrightnessReportedEventArgs(endpoint, (byte) level));
        }

        AssertEvents(expected, events);
    }

    [Fact]
    public async Task DimmerRead_EndpointWithAdvancedDimmingOnly_ReportsBrightness()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.AdvancedDimmingState]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Scs, OpenNettyDimensions.Lighting.DimmerLevelSpeed, ScsPointAddress, values: "180"));

        AssertEvents([new OpenNettyEvents.BrightnessReportedEventArgs(endpoint, 80)], events);
    }

    [Fact]
    public async Task DimmerRead_EndpointWithoutDimmingOrSwitchState_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.BasicShutterState]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Scs, OpenNettyDimensions.Lighting.DimmerLevelSpeed, ScsPointAddress, values: "180"));

        Assert.Empty(events);
    }

    [Fact]
    public async Task DimmerRead_EmptyValue_IsIgnored()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Scs, OpenNettyDimensions.Lighting.DimmerLevelSpeed, ScsPointAddress, values: ""));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Zigbee, null, "150", OpenNettyModels.Lighting.SwitchState.On, 50)]
    [InlineData(OpenNettyProtocol.Zigbee, OpenNettyMode.Broadcast, "150", OpenNettyModels.Lighting.SwitchState.On, 50)]
    [InlineData(OpenNettyProtocol.Zigbee, OpenNettyMode.Multicast, "180", OpenNettyModels.Lighting.SwitchState.On, 80)]
    [InlineData(OpenNettyProtocol.Zigbee, null, "100", OpenNettyModels.Lighting.SwitchState.Off, null)]
    [InlineData(OpenNettyProtocol.Nitoo, OpenNettyMode.Unicast, "50", OpenNettyModels.Lighting.SwitchState.On, 50)]
    [InlineData(OpenNettyProtocol.Nitoo, OpenNettyMode.Unicast, "100", OpenNettyModels.Lighting.SwitchState.On, 100)]
    [InlineData(OpenNettyProtocol.Nitoo, OpenNettyMode.Unicast, "0", OpenNettyModels.Lighting.SwitchState.Off, null)]
    public async Task DimmerSet_ReceivedByEndpointWithDimming_ReportsSwitchStateAndBrightness(
        OpenNettyProtocol protocol, OpenNettyMode? mode, string value, OpenNettyModels.Lighting.SwitchState state, int? brightness)
            {
                await using var harness = await CoordinatorHarness.StartAsync();
                var address = CreateAddress(protocol);
                var endpoint = harness.AddEndpoint(protocol, address,
            [OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.BasicDimmingState]);

        var events = await harness.ReceiveAsync(CreateDimensionSet(
            protocol, OpenNettyDimensions.Lighting.DimmerLevelSpeed, address, mode, value));

        List<OpenNettyEvents.EventArgs> expected = [new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, state)];
        if (brightness is int level)
        {
            expected.Add(new OpenNettyEvents.BrightnessReportedEventArgs(endpoint, (byte) level));
        }

        AssertEvents(expected, events);
    }

    [Fact]
    public async Task DimmerSet_SentToNitooEndpointInUnicast_ReportsSwitchStateAndBrightness()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.AdvancedDimmingState]);

        var events = await harness.SendAsync(CreateDimensionSet(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Lighting.DimmerLevelSpeed, CreateNitooAddress(), OpenNettyMode.Unicast, "75"));

        AssertEvents(
        [
            new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On),
            new OpenNettyEvents.BrightnessReportedEventArgs(endpoint, 75)
        ], events);
    }

    [Theory]
    [InlineData(OpenNettyMode.Broadcast)]
    [InlineData(OpenNettyMode.Multicast)]
    public async Task DimmerSet_NitooEndpointInBroadcastOrMulticast_DoesNotReportAnything(OpenNettyMode mode)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.BasicDimmingState]);

        var events = await harness.SendAsync(CreateDimensionSet(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Lighting.DimmerLevelSpeed, CreateNitooAddress(), mode, "50"));

        Assert.Empty(events);
    }

    [Fact]
    public async Task DimmerSet_ScsEndpoint_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress,
            [OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.BasicDimmingState]);

        var events = await harness.ReceiveAsync(CreateDimensionSet(
            OpenNettyProtocol.Scs, OpenNettyDimensions.Lighting.DimmerLevelSpeed, ScsPointAddress, values: "150"));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData("128")]
    [InlineData("129")]
    [InlineData("130")]
    public async Task UnitDescription129_OnValue_ReportsOnState(string value)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["129", value]));

                    AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On)], events);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("127")]
    [InlineData("131")]
    public async Task UnitDescription129_OtherValue_ReportsOffState(string value)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["129", value]));

                    AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.Off)], events);
    }

    [Fact]
    public async Task UnitDescription129_EndpointWithoutSwitchState_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["129", "129"]));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData("75", OpenNettyModels.Lighting.SwitchState.On, 75)]
    [InlineData("100", OpenNettyModels.Lighting.SwitchState.On, 100)]
    [InlineData("1", OpenNettyModels.Lighting.SwitchState.On, 1)]
    [InlineData("0", OpenNettyModels.Lighting.SwitchState.Off, null)]
    public async Task UnitDescription143_EndpointWithDimming_ReportsSwitchStateAndBrightness(string value, OpenNettyModels.Lighting.SwitchState state, int? brightness)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.BasicDimmingState]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["143", value, "0"]));

                    List<OpenNettyEvents.EventArgs> expected = [new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, state)];
        if (brightness is int level)
        {
            expected.Add(new OpenNettyEvents.BrightnessReportedEventArgs(endpoint, (byte) level));
        }

        AssertEvents(expected, events);
    }

    [Fact]
    public async Task UnitDescription143_EndpointWithoutCapabilities_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["143", "50", "0"]));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs, "Stop", OpenNettyModels.Automation.ShutterState.Stopped)]
    [InlineData(OpenNettyProtocol.Scs, "Up", OpenNettyModels.Automation.ShutterState.Opening)]
    [InlineData(OpenNettyProtocol.Scs, "Down", OpenNettyModels.Automation.ShutterState.Closing)]
    [InlineData(OpenNettyProtocol.Zigbee, "Stop", OpenNettyModels.Automation.ShutterState.Stopped)]
    [InlineData(OpenNettyProtocol.Zigbee, "Up", OpenNettyModels.Automation.ShutterState.Opening)]
    [InlineData(OpenNettyProtocol.Zigbee, "Down", OpenNettyModels.Automation.ShutterState.Closing)]
    public async Task ShutterCommand_ReceivedByScsOrZigbeeEndpoint_ReportsShutterState(
        OpenNettyProtocol protocol, string command, OpenNettyModels.Automation.ShutterState state)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var address = CreateAddress(protocol);
        var endpoint = harness.AddEndpoint(protocol, address, [OpenNettyCapabilities.BasicShutterState]);

        var events = await harness.ReceiveAsync(CreateCommand(protocol, GetShutterCommand(command), address));

                AssertEvents([new OpenNettyEvents.ShutterStateReportedEventArgs(endpoint, state)], events);
    }

    [Theory]
    [InlineData("Stop", OpenNettyModels.Automation.ShutterState.Stopped)]
    [InlineData("Up", OpenNettyModels.Automation.ShutterState.Opening)]
    [InlineData("Down", OpenNettyModels.Automation.ShutterState.Closing)]
    public async Task ShutterCommand_SentToZigbeeEndpoint_ReportsShutterState(string command, OpenNettyModels.Automation.ShutterState state)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var address = CreateZigbeeAddress();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Zigbee, address, [OpenNettyCapabilities.BasicShutterState]);

        var events = await harness.SendAsync(CreateCommand(OpenNettyProtocol.Zigbee, GetShutterCommand(command), address));

                AssertEvents([new OpenNettyEvents.ShutterStateReportedEventArgs(endpoint, state)], events);
    }

    [Fact]
    public async Task ShutterCommand_SentToScsEndpoint_IsIgnored()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.BasicShutterState]);

        var events = await harness.SendAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Automation.Up, ScsPointAddress));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(OpenNettyMode.Unicast, "Up", OpenNettyModels.Automation.ShutterState.Opening)]
    [InlineData(OpenNettyMode.Broadcast, "Down", OpenNettyModels.Automation.ShutterState.Closing)]
    [InlineData(OpenNettyMode.Multicast, "Stop", OpenNettyModels.Automation.ShutterState.Stopped)]
    public async Task ShutterCommand_ReceivedByNitooEndpoint_ReportsShutterStateRegardlessOfTransmissionMode(
        OpenNettyMode mode, string command, OpenNettyModels.Automation.ShutterState state)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, GetShutterCommand(command), CreateNitooAddress(), mode));

                AssertEvents([new OpenNettyEvents.ShutterStateReportedEventArgs(endpoint, state)], events);
    }

    [Fact]
    public async Task ShutterCommand_SentToNitooEndpointInUnicast_ReportsShutterState()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState]);

        var events = await harness.SendAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.Automation.Up, CreateNitooAddress(), OpenNettyMode.Unicast));

        AssertEvents([new OpenNettyEvents.ShutterStateReportedEventArgs(endpoint, OpenNettyModels.Automation.ShutterState.Opening)], events);
    }

    [Theory]
    [InlineData(OpenNettyMode.Broadcast)]
    [InlineData(OpenNettyMode.Multicast)]
    public async Task ShutterCommand_SentToNitooEndpointInBroadcastOrMulticast_DoesNotReportShutterState(OpenNettyMode mode)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState]);

        var events = await harness.SendAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.Automation.Up, CreateNitooAddress(), mode));

        Assert.Empty(events);
    }

    [Fact]
    public async Task ShutterCommand_EndpointWithoutShutterState_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Automation.Down, ScsPointAddress));

        Assert.Empty(events);
    }

    [Fact]
    public async Task ShutterCommand_AreaAddressAndPushButtonSwitchMode_IsIgnored()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsAreaAddress, [OpenNettyCapabilities.BasicShutterState],
            CreateSettings(OpenNettySettings.SwitchMode, OpenNettySettings.SwitchModes.PushButton));

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Automation.Up, ScsAreaAddress));

        Assert.Empty(events);
    }

    [Fact]
    public async Task ShutterCommand_BroadcastToNitooEndpointWithAssociatedUnit_ReportsShutterStateOnAssociatedEndpoint()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([], unit: 1, associatedUnit: 2);
        var associated = harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState], unit: 2);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.Automation.Down, CreateNitooAddress(unit: 1), OpenNettyMode.Broadcast));

        AssertEvents([new OpenNettyEvents.ShutterStateReportedEventArgs(associated, OpenNettyModels.Automation.ShutterState.Closing)], events);
    }

    [Theory]
    [InlineData("Stop", OpenNettyModels.Automation.StopUpDownScenarioType.Stop)]
    [InlineData("Up", OpenNettyModels.Automation.StopUpDownScenarioType.Up)]
    [InlineData("Down", OpenNettyModels.Automation.StopUpDownScenarioType.Down)]
    public async Task ShutterCommand_ReceivedByZigbeeEndpointWithScenarioEvent_ReportsScenario(string command, OpenNettyModels.Automation.StopUpDownScenarioType type)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var address = CreateZigbeeAddress();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Zigbee, address, [OpenNettyCapabilities.StopUpDownScenarioEvent]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Zigbee, GetShutterCommand(command), address));

                AssertEvents([new OpenNettyEvents.StopUpDownScenarioReportedEventArgs(endpoint, type)], events);
    }

    [Theory]
    [InlineData("Stop", OpenNettyModels.Automation.StopUpDownScenarioType.Stop)]
    [InlineData("Up", OpenNettyModels.Automation.StopUpDownScenarioType.Up)]
    [InlineData("Down", OpenNettyModels.Automation.StopUpDownScenarioType.Down)]
    public async Task ShutterCommand_BroadcastToNitooEndpointWithScenarioEvent_ReportsScenario(string command, OpenNettyModels.Automation.StopUpDownScenarioType type)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.StopUpDownScenarioEvent]);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, GetShutterCommand(command), CreateNitooAddress(), OpenNettyMode.Broadcast));

                    AssertEvents([new OpenNettyEvents.StopUpDownScenarioReportedEventArgs(endpoint, type)], events);
    }

    [Theory]
    [InlineData(OpenNettyMode.Unicast)]
    [InlineData(OpenNettyMode.Multicast)]
    public async Task ShutterCommand_NonBroadcastToNitooEndpointWithScenarioEvent_DoesNotReportScenario(OpenNettyMode mode)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.StopUpDownScenarioEvent]);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.Automation.Up, CreateNitooAddress(), mode));

        Assert.Empty(events);
    }

    [Fact]
    public async Task ShutterCommand_ScsEndpointWithScenarioEvent_DoesNotReportScenario()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.StopUpDownScenarioEvent]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Automation.Up, ScsPointAddress));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(OpenNettyMode.Broadcast)]
    [InlineData(OpenNettyMode.Multicast)]
    public async Task ShutterCommand_NitooEndpointWithScenarios_ReportsShutterStateOnLinkedEndpointsOnly(OpenNettyMode mode)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.StopUpDownScenarioEvent], unit: 1, scenarios:
        [
            new OpenNettyScenario { EndpointName = "linked-109", FunctionCode = 109 },
            new OpenNettyScenario { EndpointName = "excluded-110", FunctionCode = 110 },
            new OpenNettyScenario { EndpointName = "linked-111", FunctionCode = 111 },
            new OpenNettyScenario { EndpointName = "linked-112", FunctionCode = 112 },
            new OpenNettyScenario { EndpointName = "excluded-113", FunctionCode = 113 },
            new OpenNettyScenario { EndpointName = "without-shutter-state", FunctionCode = 109 }
        ]);
        var linked109 = harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState], unit: 2, name: "linked-109");
        harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState], unit: 3, name: "excluded-110");
        var linked111 = harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState], unit: 4, name: "linked-111");
        var linked112 = harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState], unit: 5, name: "linked-112");
        harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState], unit: 6, name: "excluded-113");
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState], unit: 7, name: "without-shutter-state");

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.Automation.Stop, CreateNitooAddress(unit: 1), mode));

        AssertEvents(
        [
            new OpenNettyEvents.ShutterStateReportedEventArgs(linked109, OpenNettyModels.Automation.ShutterState.Stopped),
            new OpenNettyEvents.ShutterStateReportedEventArgs(linked111, OpenNettyModels.Automation.ShutterState.Stopped),
            new OpenNettyEvents.ShutterStateReportedEventArgs(linked112, OpenNettyModels.Automation.ShutterState.Stopped)
        ], events.OfType<OpenNettyEvents.ShutterStateReportedEventArgs>());
    }

    [Theory]
    [InlineData("10", "0", OpenNettyModels.Automation.ShutterState.Closed, 0)]
    [InlineData("10", "50", OpenNettyModels.Automation.ShutterState.Open, 50)]
    [InlineData("10", "100", OpenNettyModels.Automation.ShutterState.Open, 100)]
    [InlineData("10", "255", OpenNettyModels.Automation.ShutterState.Stopped, null)]
    [InlineData("11", "50", OpenNettyModels.Automation.ShutterState.Opening, 50)]
    [InlineData("13", "50", OpenNettyModels.Automation.ShutterState.Opening, 50)]
    [InlineData("12", "50", OpenNettyModels.Automation.ShutterState.Closing, 50)]
    [InlineData("14", "50", OpenNettyModels.Automation.ShutterState.Closing, 50)]
    public async Task ShutterStatusRead_EndpointWithAdvancedShutterState_ReportsShutterStateAndPosition(
        string status, string position, OpenNettyModels.Automation.ShutterState state, int? expectedPosition)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Zigbee, CreateZigbeeAddress(), [OpenNettyCapabilities.AdvancedShutterState],
            CreateSettings(OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.AutomationActuator));

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Zigbee, OpenNettyDimensions.Automation.ShutterStatus, CreateZigbeeAddress(), values: [status, position]));

        AssertEvents(
        [
            new OpenNettyEvents.ShutterStateReportedEventArgs(endpoint, state),
            new OpenNettyEvents.ShutterPositionReportedEventArgs(endpoint, (byte?) expectedPosition)
        ], events);
    }

    [Fact]
    public async Task ShutterStatusRead_EndpointWithoutFunctionType_ReportsShutterStateAndPosition()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Zigbee, CreateZigbeeAddress(), [OpenNettyCapabilities.AdvancedShutterState]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Zigbee, OpenNettyDimensions.Automation.ShutterStatus, CreateZigbeeAddress(), values: ["11", "50"]));

        AssertEvents(
        [
            new OpenNettyEvents.ShutterStateReportedEventArgs(endpoint, OpenNettyModels.Automation.ShutterState.Opening),
            new OpenNettyEvents.ShutterPositionReportedEventArgs(endpoint, 50)
        ], events);
    }

    [Fact]
    public async Task ShutterStatusRead_EndpointWithAnotherFunctionType_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Zigbee, CreateZigbeeAddress(), [OpenNettyCapabilities.AdvancedShutterState],
            CreateSettings(OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.LightActuator));

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Zigbee, OpenNettyDimensions.Automation.ShutterStatus, CreateZigbeeAddress(), values: ["11", "50"]));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData("100", OpenNettyModels.Automation.ShutterState.Opening)]
    [InlineData("102", OpenNettyModels.Automation.ShutterState.Opening)]
    [InlineData("0", OpenNettyModels.Automation.ShutterState.Closing)]
    [InlineData("103", OpenNettyModels.Automation.ShutterState.Closing)]
    [InlineData("101", OpenNettyModels.Automation.ShutterState.Stopped)]
    [InlineData("50", OpenNettyModels.Automation.ShutterState.Stopped)]
    public async Task UnitDescription139_EndpointWithShutterState_ReportsShutterState(string value, OpenNettyModels.Automation.ShutterState state)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["139", value]));

        AssertEvents([new OpenNettyEvents.ShutterStateReportedEventArgs(endpoint, state)], events);
    }

    [Fact]
    public async Task UnitDescription139_EndpointWithoutShutterState_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["139", "100"]));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData("2", OpenNettyModels.TemperatureControl.SmartMeterRateType.OffPeak)]
    [InlineData("3", OpenNettyModels.TemperatureControl.SmartMeterRateType.Peak)]
    public async Task SmartMeterRateTypeRead_EndpointWithSmartMeterInformation_ReportsRateType(string value, OpenNettyModels.TemperatureControl.SmartMeterRateType type)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.SmartMeterInformation]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.TemperatureControl.SmartMeterRateType, CreateNitooAddress(), values: value));

        AssertEvents([new OpenNettyEvents.SmartMeterRateTypeReportedEventArgs(endpoint, type)], events);
    }

    [Fact]
    public async Task SmartMeterRateTypeRead_EndpointWithoutSmartMeterInformation_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.TemperatureControl.SmartMeterRateType, CreateNitooAddress(), values: "2"));

        Assert.Empty(events);
    }

    [Fact]
    public async Task SmartMeterRateTypeRead_UnknownValue_LogsWarning()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.SmartMeterInformation]);

        await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.TemperatureControl.SmartMeterRateType, CreateNitooAddress(), values: "1"));

        var entry = Assert.Single(await harness.Logger.Entries.WaitAsync(1));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<InvalidDataException>(entry.Exception);
    }

    [Theory]
    [InlineData("32", OpenNettyModels.TemperatureControl.SmartMeterRateType.OffPeak, false)]
    [InlineData("33", OpenNettyModels.TemperatureControl.SmartMeterRateType.OffPeak, true)]
    [InlineData("48", OpenNettyModels.TemperatureControl.SmartMeterRateType.Peak, false)]
    [InlineData("49", OpenNettyModels.TemperatureControl.SmartMeterRateType.Peak, true)]
    public async Task UnitDescription7_EndpointWithSmartMeterInformation_ReportsRateTypeAndPowerCutMode(
        string value, OpenNettyModels.TemperatureControl.SmartMeterRateType type, bool powerCut)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.SmartMeterInformation]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["7", value]));

        AssertEvents(
        [
            new OpenNettyEvents.SmartMeterRateTypeReportedEventArgs(endpoint, type),
            new OpenNettyEvents.SmartMeterPowerCutModeReportedEventArgs(endpoint, powerCut)
        ], events);
    }

    [Fact]
    public async Task UnitDescription7_EndpointWithoutSmartMeterInformation_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["7", "32"]));

        Assert.Empty(events);
    }

    [Fact]
    public async Task UnitDescription7_UnknownValue_LogsWarning()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.SmartMeterInformation]);

        await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["7", "34"]));

        var entry = Assert.Single(await harness.Logger.Entries.WaitAsync(1));
        Assert.IsType<InvalidDataException>(entry.Exception);
    }

    [Fact]
    public async Task UnitDescription133_IdleAutomaticValue_ReportsIdleStateOnly()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["133", "0"]));

        AssertEvents([new OpenNettyEvents.WaterHeaterStateReportedEventArgs(endpoint, OpenNettyModels.TemperatureControl.WaterHeaterState.Idle)], events);
    }

    [Theory]
    [InlineData("32", OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic, OpenNettyModels.TemperatureControl.WaterHeaterState.Idle)]
    [InlineData("1", OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic, OpenNettyModels.TemperatureControl.WaterHeaterState.Heating)]
    [InlineData("33", OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic, OpenNettyModels.TemperatureControl.WaterHeaterState.Heating)]
    [InlineData("17", OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn, OpenNettyModels.TemperatureControl.WaterHeaterState.Heating)]
    public async Task UnitDescription133_EndpointWithWaterHeating_ReportsSetpointModeAndState(
        string value, OpenNettyModels.TemperatureControl.WaterHeaterMode mode, OpenNettyModels.TemperatureControl.WaterHeaterState state)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["133", value]));

        AssertEvents(
        [
            new OpenNettyEvents.WaterHeaterSetpointModeReportedEventArgs(endpoint, mode),
            new OpenNettyEvents.WaterHeaterStateReportedEventArgs(endpoint, state)
        ], events);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("17")]
    public async Task UnitDescription133_EndpointWithoutWaterHeating_DoesNotReportAnything(string value)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.SmartMeterInformation]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["133", value]));

        Assert.Empty(events);
    }

    [Fact]
    public async Task UnitDescription133_UnknownValue_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["133", "99"]));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData("0", OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOff)]
    [InlineData("1", OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn)]
    [InlineData("2", OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic)]
    public async Task WaterHeatingModeSet_EndpointWithWaterHeating_ReportsSetpointMode(string value, OpenNettyModels.TemperatureControl.WaterHeaterMode mode)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating]);

        var events = await harness.ReceiveAsync(CreateDimensionSet(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.TemperatureControl.WaterHeatingMode, CreateNitooAddress(), values: value));

        AssertEvents([new OpenNettyEvents.WaterHeaterSetpointModeReportedEventArgs(endpoint, mode)], events);
    }

    [Fact]
    public async Task WaterHeatingModeSet_SentMessage_ReportsSetpointMode()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating]);

        var events = await harness.SendAsync(CreateDimensionSet(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.TemperatureControl.WaterHeatingMode, CreateNitooAddress(), values: "1"));

        AssertEvents([new OpenNettyEvents.WaterHeaterSetpointModeReportedEventArgs(endpoint, OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn)], events);
    }

    [Fact]
    public async Task WaterHeatingModeSet_UnknownValue_LogsWarning()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating]);

        await harness.ReceiveAsync(CreateDimensionSet(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.TemperatureControl.WaterHeatingMode, CreateNitooAddress(), values: "3"));

        var entry = Assert.Single(await harness.Logger.Entries.WaitAsync(1));
        Assert.IsType<InvalidDataException>(entry.Exception);
    }

    [Theory]
    [InlineData("6", "0", OpenNettyModels.TemperatureControl.PilotWireMode.Comfort, false, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, false)]
    [InlineData("132", "3", OpenNettyModels.TemperatureControl.PilotWireMode.Eco, false, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, false)]
    [InlineData("6", "2", OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo, false, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, false)]
    [InlineData("6", "16", OpenNettyModels.TemperatureControl.PilotWireMode.Comfort, false, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, true)]
    [InlineData("6", "9", OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne, true, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, false)]
    [InlineData("132", "73", OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne, true, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, false)]
    [InlineData("6", "140", OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection, true, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, false)]
    [InlineData("6", "24", OpenNettyModels.TemperatureControl.PilotWireMode.Comfort, true, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, true)]
    public async Task UnitDescription6Or132_EndpointWithPilotWireControl_ReportsPilotWireConfiguration(
        string functionCode, string value, OpenNettyModels.TemperatureControl.PilotWireMode mode, bool derogationActive,
        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration duration, bool shutdownActive)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.PilotWireControl]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: [functionCode, value]));

        List<OpenNettyEvents.EventArgs> expected = derogationActive
            ? [new OpenNettyEvents.PilotWireDerogationModeReportedEventArgs(endpoint, mode, duration)]
            :
            [
                new OpenNettyEvents.PilotWireSetpointModeReportedEventArgs(endpoint, mode),
                new OpenNettyEvents.PilotWireDerogationModeReportedEventArgs(endpoint, null, null)
            ];

        expected.Add(new OpenNettyEvents.PilotWireShutdownModeReportedEventArgs(endpoint, shutdownActive));
        AssertEvents(expected, events);
    }

    [Fact]
    public async Task UnitDescription6Or132_EndpointWithoutPilotWireControl_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.PilotWireShutdown]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(), values: ["6", "0"]));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData("0", OpenNettyModels.TemperatureControl.PilotWireMode.Comfort)]
    [InlineData("1", OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne)]
    [InlineData("2", OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo)]
    [InlineData("3", OpenNettyModels.TemperatureControl.PilotWireMode.Eco)]
    [InlineData("4", OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection)]
    [InlineData("11", OpenNettyModels.TemperatureControl.PilotWireMode.Eco)]
    public async Task PilotWireSetpointModeCommand_EndpointWithPilotWireControl_ReportsSetpointMode(string value, OpenNettyModels.TemperatureControl.PilotWireMode mode)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.PilotWireControl]);
        var command = OpenNettyCommands.TemperatureControl.WirePilotSetpointMode.WithParameters([value]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress()));

        AssertEvents([new OpenNettyEvents.PilotWireSetpointModeReportedEventArgs(endpoint, mode)], events);
    }

    [Fact]
    public async Task PilotWireSetpointModeCommand_UnknownMode_LogsWarning()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.PilotWireControl]);
        var command = OpenNettyCommands.TemperatureControl.WirePilotSetpointMode.WithParameters(["5"]);

        await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress()));

        var entry = Assert.Single(await harness.Logger.Entries.WaitAsync(1));
        Assert.IsType<InvalidDataException>(entry.Exception);
    }

    [Theory]
    [InlineData("3", OpenNettyModels.TemperatureControl.PilotWireMode.Eco, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None)]
    [InlineData("4", OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None)]
    [InlineData("35", OpenNettyModels.TemperatureControl.PilotWireMode.Eco, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours)]
    [InlineData("128", OpenNettyModels.TemperatureControl.PilotWireMode.Comfort, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours)]
    [InlineData("130", OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours)]
    public async Task PilotWireDerogationModeCommand_EndpointWithPilotWireDerogation_ReportsDerogationMode(
        string value, OpenNettyModels.TemperatureControl.PilotWireMode mode, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration duration)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.PilotWireDerogation]);
        var command = OpenNettyCommands.TemperatureControl.WirePilotDerogationMode.WithParameters([value]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress()));

        AssertEvents([new OpenNettyEvents.PilotWireDerogationModeReportedEventArgs(endpoint, mode, duration)], events);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("160")]
    public async Task PilotWireDerogationModeCommand_UnknownModeOrDuration_LogsWarning(string value)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.PilotWireDerogation]);
        var command = OpenNettyCommands.TemperatureControl.WirePilotDerogationMode.WithParameters([value]);

        await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress()));

        var entry = Assert.Single(await harness.Logger.Entries.WaitAsync(1));
        Assert.IsType<InvalidDataException>(entry.Exception);
    }

    [Fact]
    public async Task PilotWireDerogationModeCommand_CommandWithoutParameters_IsIgnored()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.PilotWireDerogation]);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.TemperatureControl.WirePilotDerogationMode, CreateNitooAddress()));

        Assert.Empty(events);
    }

    [Fact]
    public async Task CancelPilotWireDerogationCommand_EndpointWithPilotWireDerogation_ReportsNoDerogation()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.PilotWireDerogation]);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.TemperatureControl.CancelWirePilotDerogationMode, CreateNitooAddress()));

        AssertEvents([new OpenNettyEvents.PilotWireDerogationModeReportedEventArgs(endpoint, null, null)], events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PilotWireShutdownCommand_EndpointWithPilotWireShutdown_ReportsShutdownMode(bool active)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.PilotWireShutdown]);
        var command = active
            ? OpenNettyCommands.TemperatureControl.WirePilotShutdownMode
            : OpenNettyCommands.TemperatureControl.CancelWirePilotShutdownMode;

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress()));

        AssertEvents([new OpenNettyEvents.PilotWireShutdownModeReportedEventArgs(endpoint, active)], events);
    }

    public static TheoryData<string, OpenNettyMode> LinkedEndpointKindsAndGroupModes
    {
        get
        {
            TheoryData<string, OpenNettyMode> data = [];

            foreach (var kind in LinkedEndpointKinds)
            {
                data.Add(kind, OpenNettyMode.Broadcast);
                data.Add(kind, OpenNettyMode.Multicast);
            }

            return data;
        }
    }

    public static TheoryData<string> LinkedEndpointKindNames => [.. LinkedEndpointKinds];

    private static readonly string[] LinkedEndpointKinds =
    [
        "WaterHeatingMode",
        "PilotWireSetpointMode",
        "PilotWireDerogationMode",
        "CancelPilotWireDerogationMode",
        "PilotWireShutdownMode",
        "CancelPilotWireShutdownMode"
    ];

    [Theory]
    [MemberData(nameof(LinkedEndpointKindNames))]
    public async Task LinkedEndpointMessage_EndpointWithCapability_ReportsEvent(string kind)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var scenario = CreateLinkedEndpointScenario(kind, OpenNettyMode.Unicast);
        var endpoint = harness.AddNitooEndpoint([scenario.Capability]);

        var events = await harness.ReceiveAsync(scenario.Message);

        AssertEvents([scenario.CreateEvent(endpoint)], events);
    }

    [Theory]
    [MemberData(nameof(LinkedEndpointKindNames))]
    public async Task LinkedEndpointMessage_EndpointWithoutCapability_DoesNotReportAnything(string kind)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var scenario = CreateLinkedEndpointScenario(kind, OpenNettyMode.Unicast);
        harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState]);

        var events = await harness.ReceiveAsync(scenario.Message);

        Assert.Empty(events);
    }

    [Theory]
    [MemberData(nameof(LinkedEndpointKindNames))]
    public async Task LinkedEndpointMessage_EndpointWithAssociatedUnit_ReportsEventOnAssociatedEndpoint(string kind)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var scenario = CreateLinkedEndpointScenario(kind, OpenNettyMode.Unicast);
        harness.AddNitooEndpoint([], unit: 1, associatedUnit: 2);
        var associated = harness.AddNitooEndpoint([scenario.Capability], unit: 2);
        harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState], unit: 3);

        var events = await harness.ReceiveAsync(scenario.Message);

        AssertEvents([scenario.CreateEvent(associated)], events);
    }

    [Theory]
    [MemberData(nameof(LinkedEndpointKindsAndGroupModes))]
    public async Task LinkedEndpointMessage_GroupTransmissionToEndpointWithScenarios_ReportsEventOnEndpointsLinkedWithFunctionCode255(
        string kind, OpenNettyMode mode)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var scenario = CreateLinkedEndpointScenario(kind, mode);
        harness.AddNitooEndpoint([], unit: 1, scenarios:
        [
            new OpenNettyScenario { EndpointName = "linked", FunctionCode = 255 },
            new OpenNettyScenario { EndpointName = "other-function-code", FunctionCode = 100 },
            new OpenNettyScenario { EndpointName = "without-capability", FunctionCode = 255 }
        ]);
        var linked = harness.AddNitooEndpoint([scenario.Capability], unit: 2, name: "linked");
        harness.AddNitooEndpoint([scenario.Capability], unit: 3, name: "other-function-code");
        harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState], unit: 4, name: "without-capability");

        var events = await harness.ReceiveAsync(scenario.Message);

        AssertEvents([scenario.CreateEvent(linked)], events);
    }

    [Theory]
    [MemberData(nameof(LinkedEndpointKindNames))]
    public async Task LinkedEndpointMessage_UnicastToEndpointWithScenarios_DoesNotReportLinkedEndpoints(string kind)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var scenario = CreateLinkedEndpointScenario(kind, OpenNettyMode.Unicast);
        harness.AddNitooEndpoint([], unit: 1, scenarios: [new OpenNettyScenario { EndpointName = "linked", FunctionCode = 255 }]);
        harness.AddNitooEndpoint([scenario.Capability], unit: 2, name: "linked");

        var events = await harness.ReceiveAsync(scenario.Message);

        Assert.Empty(events);
    }

    [Theory]
    [InlineData("0", 5)]
    [InlineData("1", 33)]
    [InlineData("2", 66)]
    [InlineData("3", 100)]
    public async Task BatteryInformationRead_EndpointWithBatteryLevel_ReportsBatteryLevel(string value, int level)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Zigbee, CreateZigbeeAddress(unit: 0), [OpenNettyCapabilities.BatteryLevel]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Zigbee, OpenNettyDimensions.Management.BatteryInformation, CreateZigbeeAddress(unit: 1), values: value));

        AssertEvents([new OpenNettyEvents.BatteryLevelReportedEventArgs(endpoint, (byte) level)], events);
    }

    [Fact]
    public async Task BatteryInformationRead_UnitSpecificEndpoint_IsNotReported()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Zigbee, CreateZigbeeAddress(unit: 1), [OpenNettyCapabilities.BatteryLevel]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Zigbee, OpenNettyDimensions.Management.BatteryInformation, CreateZigbeeAddress(unit: 1), values: "3"));

        Assert.Empty(events);
    }

    [Fact]
    public async Task BatteryInformationRead_EndpointWithoutBatteryLevel_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Zigbee, CreateZigbeeAddress(unit: 0), [OpenNettyCapabilities.BatteryAlert]);

        var events = await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Zigbee, OpenNettyDimensions.Management.BatteryInformation, CreateZigbeeAddress(unit: 1), values: "3"));

        Assert.Empty(events);
    }

    [Fact]
    public async Task BatteryInformationRead_UnknownValue_LogsWarning()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Zigbee, CreateZigbeeAddress(unit: 0), [OpenNettyCapabilities.BatteryLevel]);

        await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Zigbee, OpenNettyDimensions.Management.BatteryInformation, CreateZigbeeAddress(unit: 1), values: "4"));

        var entry = Assert.Single(await harness.Logger.Entries.WaitAsync(1));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<InvalidDataException>(entry.Exception);
    }

    [Fact]
    public async Task BatteryWeakCommand_EndpointWithBatteryAlert_ReportsBatteryAlert()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var address = CreateZigbeeAddress();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Zigbee, address, [OpenNettyCapabilities.BatteryAlert]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Zigbee, OpenNettyCommands.Management.BatteryWeak, address));

        AssertEvents([new OpenNettyEvents.BatteryAlertReportedEventArgs(endpoint)], events);
    }

    [Fact]
    public async Task BatteryWeakCommand_EndpointWithoutBatteryAlert_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var address = CreateZigbeeAddress();
        harness.AddEndpoint(OpenNettyProtocol.Zigbee, address, [OpenNettyCapabilities.BatteryLevel]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Zigbee, OpenNettyCommands.Management.BatteryWeak, address));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData("CreateZigbeeNetwork", OpenNettyModels.Management.ZigbeeNetworkEventType.Created)]
    [InlineData("CloseZigbeeNetwork", OpenNettyModels.Management.ZigbeeNetworkEventType.Closed)]
    [InlineData("OpenZigbeeNetwork", OpenNettyModels.Management.ZigbeeNetworkEventType.Open)]
    [InlineData("JoinZigbeeNetwork", OpenNettyModels.Management.ZigbeeNetworkEventType.Joined)]
    [InlineData("LeaveZigbeeNetwork", OpenNettyModels.Management.ZigbeeNetworkEventType.Left)]
    public async Task ZigbeeNetworkCommand_WithoutAddress_ReportsEventOnGatewayEndpoints(string name, OpenNettyModels.Management.ZigbeeNetworkEventType type)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddGatewayEndpoint([OpenNettyCapabilities.ZigbeeNetworkManagement]);
        harness.AddGatewayEndpoint([OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Zigbee, GetZigbeeNetworkCommand(name), null));

                AssertEvents([new OpenNettyEvents.ZigbeeNetworkEventReportedEventArgs(endpoint, type)], events);
    }

    [Theory]
    [InlineData("CreateZigbeeNetwork", OpenNettyModels.Management.ZigbeeNetworkEventType.Created)]
    [InlineData("LeaveZigbeeNetwork", OpenNettyModels.Management.ZigbeeNetworkEventType.Left)]
    public async Task ZigbeeNetworkCommand_WithAddress_ReportsEventOnEndpointsMatchingAddress(string name, OpenNettyModels.Management.ZigbeeNetworkEventType type)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var address = CreateZigbeeAddress();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Zigbee, address, [OpenNettyCapabilities.ZigbeeNetworkManagement]);
        harness.AddEndpoint(OpenNettyProtocol.Zigbee, address, [OpenNettyCapabilities.OnOffSwitchState]);
        harness.AddGatewayEndpoint([OpenNettyCapabilities.ZigbeeNetworkManagement]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Zigbee, GetZigbeeNetworkCommand(name), address));

                AssertEvents([new OpenNettyEvents.ZigbeeNetworkEventReportedEventArgs(endpoint, type)], events);
    }

    [Theory]
    [InlineData("OpenBinding", OpenNettyModels.ScenariosPlus.ZigbeeBindingEventType.Open)]
    [InlineData("CloseBinding", OpenNettyModels.ScenariosPlus.ZigbeeBindingEventType.Closed)]
    [InlineData("CancelBinding", OpenNettyModels.ScenariosPlus.ZigbeeBindingEventType.Canceled)]
    public async Task ZigbeeBindingCommand_EndpointWithZigbeeBinding_ReportsBindingEvent(string name, OpenNettyModels.ScenariosPlus.ZigbeeBindingEventType type)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var address = CreateZigbeeAddress();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Zigbee, address, [OpenNettyCapabilities.ZigbeeBinding]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Zigbee, GetZigbeeBindingCommand(name), address));

        AssertEvents([new OpenNettyEvents.ZigbeeBindingEventReportedEventArgs(endpoint, type)], events);
    }

    [Fact]
    public async Task ZigbeeBindingCommand_EndpointWithoutZigbeeBinding_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var address = CreateZigbeeAddress();
        harness.AddEndpoint(OpenNettyProtocol.Zigbee, address, [OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Zigbee, OpenNettyCommands.ScenariosPlus.OpenBinding, address));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData("127", 127)]
    [InlineData("251", -5)]
    [InlineData("128", -128)]
    [InlineData("255", -1)]
    public async Task DimmerStep_BroadcastToEndpointWithDimmingScenario_ReportsSignedStep(string value, int step)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.DimmingScenarioEvent]);

        var events = await harness.ReceiveAsync(CreateDimensionSet(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Lighting.DimmerStep, CreateNitooAddress(), OpenNettyMode.Broadcast, value));

        AssertEvents([new OpenNettyEvents.DimmingScenarioReportedEventArgs(endpoint, (short) step)], events);
    }

    [Theory]
    [InlineData(OpenNettyMode.Unicast)]
    [InlineData(OpenNettyMode.Multicast)]
    public async Task DimmerStep_NonBroadcast_IsIgnored(OpenNettyMode mode)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.DimmingScenarioEvent]);

        var events = await harness.ReceiveAsync(CreateDimensionSet(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Lighting.DimmerStep, CreateNitooAddress(), mode, "5"));

        Assert.Empty(events);
    }

    [Fact]
    public async Task DimmerStep_EndpointWithoutDimmingScenario_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateDimensionSet(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Lighting.DimmerStep, CreateNitooAddress(), OpenNettyMode.Broadcast, "5"));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task ToggleCommand_EndpointWithToggleScenario_ReportsToggleScenario(OpenNettyProtocol protocol)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var address = CreateAddress(protocol);
        var endpoint = harness.AddEndpoint(protocol, address, [OpenNettyCapabilities.ToggleScenarioEvent]);

        var events = await harness.ReceiveAsync(CreateCommand(protocol, OpenNettyCommands.Lighting.Toggle, address));

        AssertEvents([new OpenNettyEvents.ToggleScenarioReportedEventArgs(endpoint)], events);
    }

    [Fact]
    public async Task ToggleCommand_EndpointWithoutToggleScenario_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.Toggle, ScsPointAddress));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(new string[0], OpenNettyModels.Scenarios.PressureScenarioType.Pressure)]
    [InlineData(new[] { "1" }, OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterShortPressure)]
    [InlineData(new[] { "2" }, OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterExtendedPressure)]
    [InlineData(new[] { "3" }, OpenNettyModels.Scenarios.PressureScenarioType.ExtendedPressure)]
    public async Task PressureCommand_EndpointWithPressureScenarioEvent_ReportsPressureScenario(string[] parameters, OpenNettyModels.Scenarios.PressureScenarioType type)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.PressureScenarioEvent]);
        var command = new OpenNettyCommand(OpenNettyCategories.Scenarios, "5", [.. parameters]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, command, ScsPointAddress));

        AssertEvents([new OpenNettyEvents.PressureScenarioReportedEventArgs(endpoint, type, 5)], events);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("31", 31)]
    public async Task PressureCommand_ButtonWithinRange_ReportsButtonNumber(string button, int expected)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.PressureScenarioEvent]);
        var command = new OpenNettyCommand(OpenNettyCategories.Scenarios, button);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, command, ScsPointAddress));

        AssertEvents([new OpenNettyEvents.PressureScenarioReportedEventArgs(endpoint, OpenNettyModels.Scenarios.PressureScenarioType.Pressure, (byte) expected)], events);
    }

    [Fact]
    public async Task PressureCommand_ButtonOutOfRange_IsIgnored()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.PressureScenarioEvent]);
        var command = new OpenNettyCommand(OpenNettyCategories.Scenarios, "32");

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, command, ScsPointAddress));

        Assert.Empty(events);
    }

    [Fact]
    public async Task PressureCommand_EndpointWithoutPressureScenarioEvent_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);
        var command = new OpenNettyCommand(OpenNettyCategories.Scenarios, "5");

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, command, ScsPointAddress));

        Assert.Empty(events);
    }

    [Fact]
    public async Task PressureCommand_UnknownPressureType_LogsWarning()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.PressureScenarioEvent]);
        var command = new OpenNettyCommand(OpenNettyCategories.Scenarios, "5", ["9"]);

        await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, command, ScsPointAddress));

        var entry = Assert.Single(await harness.Logger.Entries.WaitAsync(1));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<InvalidDataException>(entry.Exception);
    }

    [Theory]
    [InlineData("ShortPressure", OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure)]
    [InlineData("StartOfExtendedPressure", OpenNettyModels.ScenariosPlus.PressureScenarioType.StartOfExtendedPressure)]
    [InlineData("ExtendedPressure", OpenNettyModels.ScenariosPlus.PressureScenarioType.ExtendedPressure)]
    [InlineData("EndOfExtendedPressure", OpenNettyModels.ScenariosPlus.PressureScenarioType.EndOfExtendedPressure)]
    public async Task PressurePlusCommand_EndpointWithPressureScenarioPlusEvent_ReportsPressureScenarioPlus(
        string name, OpenNettyModels.ScenariosPlus.PressureScenarioType type)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsScenarioPlusAddress, [OpenNettyCapabilities.PressureScenarioPlusEvent]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, GetPressurePlusCommand(name), ScsScenarioPlusAddress));

        AssertEvents([new OpenNettyEvents.PressureScenarioPlusReportedEventArgs(endpoint, type, null)], events);
    }

    [Fact]
    public async Task PressurePlusCommand_ParameterizedCommand_ReportsButtonNumber()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsScenarioPlusAddress, [OpenNettyCapabilities.PressureScenarioPlusEvent]);
        var command = OpenNettyCommands.ScenariosPlus.ShortPressure.WithParameters(["7"]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, command, ScsScenarioPlusAddress));

        AssertEvents([new OpenNettyEvents.PressureScenarioPlusReportedEventArgs(endpoint, OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure, 7)], events);
    }

    [Fact]
    public async Task PressurePlusCommand_EndpointWithoutPressureScenarioPlusEvent_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsScenarioPlusAddress, [OpenNettyCapabilities.PressureScenarioEvent]);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Scs, OpenNettyCommands.ScenariosPlus.ShortPressure, ScsScenarioPlusAddress));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(true, "0", OpenNettyModels.ScenariosPlus.DryContactScenarioType.Open, OpenNettyModels.ScenariosPlus.DryContactState.Open, OpenNettyModels.ScenariosPlus.DryContactStateOrigin.StateRequest)]
    [InlineData(true, "1", OpenNettyModels.ScenariosPlus.DryContactScenarioType.Open, OpenNettyModels.ScenariosPlus.DryContactState.Open, OpenNettyModels.ScenariosPlus.DryContactStateOrigin.SystemEvent)]
    [InlineData(false, "0", OpenNettyModels.ScenariosPlus.DryContactScenarioType.Closed, OpenNettyModels.ScenariosPlus.DryContactState.Closed, OpenNettyModels.ScenariosPlus.DryContactStateOrigin.StateRequest)]
    [InlineData(false, "1", OpenNettyModels.ScenariosPlus.DryContactScenarioType.Closed, OpenNettyModels.ScenariosPlus.DryContactState.Closed, OpenNettyModels.ScenariosPlus.DryContactStateOrigin.SystemEvent)]
    public async Task DryContactCommand_EndpointWithScenarioAndState_ReportsScenarioAndState(
        bool on, string origin, OpenNettyModels.ScenariosPlus.DryContactScenarioType scenario, OpenNettyModels.ScenariosPlus.DryContactState state, OpenNettyModels.ScenariosPlus.DryContactStateOrigin expectedOrigin)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsDryContactAddress,
            [OpenNettyCapabilities.DryContactScenarioEvent, OpenNettyCapabilities.DryContactState]);
        var command = (on ? OpenNettyCommands.ScenariosPlus.DryContactOn : OpenNettyCommands.ScenariosPlus.DryContactOff).WithParameters([origin]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, command, ScsDryContactAddress));

        AssertEvents(
        [
            new OpenNettyEvents.DryContactScenarioReportedEventArgs(endpoint, scenario),
            new OpenNettyEvents.DryContactStateReportedEventArgs(endpoint, state, expectedOrigin)
        ], events);
    }

    [Fact]
    public async Task DryContactCommand_EndpointWithScenarioOnly_ReportsScenarioOnly()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsDryContactAddress, [OpenNettyCapabilities.DryContactScenarioEvent]);
        var command = OpenNettyCommands.ScenariosPlus.DryContactOn.WithParameters(["9"]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, command, ScsDryContactAddress));

        AssertEvents([new OpenNettyEvents.DryContactScenarioReportedEventArgs(endpoint, OpenNettyModels.ScenariosPlus.DryContactScenarioType.Open)], events);
    }

    [Fact]
    public async Task DryContactCommand_EndpointWithStateOnly_ReportsStateOnly()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsDryContactAddress, [OpenNettyCapabilities.DryContactState]);
        var command = OpenNettyCommands.ScenariosPlus.DryContactOff.WithParameters(["1"]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, command, ScsDryContactAddress));

        AssertEvents([new OpenNettyEvents.DryContactStateReportedEventArgs(endpoint, OpenNettyModels.ScenariosPlus.DryContactState.Closed, OpenNettyModels.ScenariosPlus.DryContactStateOrigin.SystemEvent)], events);
    }

    [Fact]
    public async Task DryContactCommand_UnknownStateOrigin_LogsWarning()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsDryContactAddress, [OpenNettyCapabilities.DryContactState]);
        var command = OpenNettyCommands.ScenariosPlus.DryContactOn.WithParameters(["9"]);

        await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, command, ScsDryContactAddress));

        var entry = Assert.Single(await harness.Logger.Entries.WaitAsync(1));
        Assert.IsType<InvalidDataException>(entry.Exception);
    }

    [Fact]
    public async Task DryContactCommand_EndpointWithoutDryContactCapabilities_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsDryContactAddress, [OpenNettyCapabilities.OnOffSwitchState]);
        var command = OpenNettyCommands.ScenariosPlus.DryContactOn.WithParameters(["0"]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, command, ScsDryContactAddress));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(OpenNettyMode.Broadcast, false, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action)]
    [InlineData(OpenNettyMode.Multicast, false, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action)]
    [InlineData(OpenNettyMode.Broadcast, true, OpenNettyModels.ScenariosPlus.ActionScenarioType.StopAction)]
    [InlineData(OpenNettyMode.Multicast, true, OpenNettyModels.ScenariosPlus.ActionScenarioType.StopAction)]
    public async Task ActionCommand_GroupTransmissionToEndpointWithActionScenarioEvent_ReportsActionScenario(
        OpenNettyMode mode, bool stop, OpenNettyModels.ScenariosPlus.ActionScenarioType type)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.ActionScenarioEvent]);
        var command = stop ? OpenNettyCommands.ScenariosPlus.StopAction : OpenNettyCommands.ScenariosPlus.Action;

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress(), mode));

        AssertEvents([new OpenNettyEvents.ActionScenarioReportedEventArgs(endpoint, type)], events);
    }

    [Fact]
    public async Task ActionCommand_UnicastTransmission_IsIgnored()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.ActionScenarioEvent]);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.Action, CreateNitooAddress(), OpenNettyMode.Unicast));

        Assert.Empty(events);
    }

    [Fact]
    public async Task ActionCommand_EndpointWithoutActionScenarioEvent_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.Action, CreateNitooAddress(), OpenNettyMode.Broadcast));

        Assert.Empty(events);
    }

    [Fact]
    public async Task ActionCommand_EndpointWithoutDimming_ReportsActionScenarioAndOnState()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.ActionScenarioEvent, OpenNettyCapabilities.OnOffSwitchState]);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.Action, CreateNitooAddress(), OpenNettyMode.Broadcast));

        AssertEvents(
        [
            new OpenNettyEvents.ActionScenarioReportedEventArgs(endpoint, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action),
            new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On)
        ], events);
    }

    [Theory]
    [InlineData(nameof(OpenNettyCapabilities.BasicDimmingState))]
    [InlineData(nameof(OpenNettyCapabilities.AdvancedDimmingState))]
    public async Task ActionCommand_EndpointWithDimming_ReportsActionScenarioOnly(string capability)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint(
            [OpenNettyCapabilities.ActionScenarioEvent, OpenNettyCapabilities.OnOffSwitchState, GetCapability(capability)]);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.Action, CreateNitooAddress(), OpenNettyMode.Broadcast));

        AssertEvents([new OpenNettyEvents.ActionScenarioReportedEventArgs(endpoint, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action)], events);
    }

    [Fact]
    public async Task ActionCommand_NitooEndpointInPushButtonMode_ReportsOnThenOff()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.ActionScenarioEvent, OpenNettyCapabilities.OnOffSwitchState],
            settings: CreateSettings(OpenNettySettings.SwitchMode, OpenNettySettings.SwitchModes.PushButton));

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.Action, CreateNitooAddress(), OpenNettyMode.Broadcast));

        AssertEvents(
        [
            new OpenNettyEvents.ActionScenarioReportedEventArgs(endpoint, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action),
            new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On),
            new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.Off)
        ], events);
    }

    [Fact]
    public async Task ActionCommand_EndpointWithAssociatedUnit_ReportsOnStateOnAssociatedEndpointsWithoutDimming()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var emitter = harness.AddNitooEndpoint([OpenNettyCapabilities.ActionScenarioEvent], unit: 1, associatedUnit: 2);
        var associated = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState], unit: 2);
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState], unit: 3);

        harness.AddNitooEndpoint([OpenNettyCapabilities.BasicShutterState], unit: 4);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.Action, CreateNitooAddress(unit: 1), OpenNettyMode.Broadcast));

        AssertEvents(
        [
            new OpenNettyEvents.ActionScenarioReportedEventArgs(emitter, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action),
            new OpenNettyEvents.SwitchStateReportedEventArgs(associated, OpenNettyModels.Lighting.SwitchState.On)
        ], events);
    }

    [Fact]
    public async Task ActionCommand_AssociatedEndpointWithDimming_DoesNotReportOnState()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var emitter = harness.AddNitooEndpoint([OpenNettyCapabilities.ActionScenarioEvent], unit: 1, associatedUnit: 2);
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.BasicDimmingState], unit: 2);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.Action, CreateNitooAddress(unit: 1), OpenNettyMode.Broadcast));

                    AssertEvents([new OpenNettyEvents.ActionScenarioReportedEventArgs(emitter, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action)], events);
    }

    [Theory]
    [InlineData(4, OpenNettyModels.Alarm.WirelessBurglarAlarmState.Armed)]
    [InlineData(5, OpenNettyModels.Alarm.WirelessBurglarAlarmState.Disarmed)]
    [InlineData(6, OpenNettyModels.Alarm.WirelessBurglarAlarmState.PartiallyArmed)]
    [InlineData(7, OpenNettyModels.Alarm.WirelessBurglarAlarmState.Triggered)]
    [InlineData(8, OpenNettyModels.Alarm.WirelessBurglarAlarmState.ExitDelayElapsed)]
    [InlineData(9, OpenNettyModels.Alarm.WirelessBurglarAlarmState.EventDetected)]
    public async Task ActionCommand_EmittedByAlarmUnit_ReportsWirelessBurglarAlarmState(byte unit, OpenNettyModels.Alarm.WirelessBurglarAlarmState state)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var emitter = harness.AddNitooEndpoint([OpenNettyCapabilities.ActionScenarioEvent], unit: unit);
        var alarm = harness.AddNitooEndpoint([OpenNettyCapabilities.WirelessBurglarAlarmState], unit: 0);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.Action, CreateNitooAddress(unit: unit), OpenNettyMode.Broadcast));

        AssertEvents(
        [
            new OpenNettyEvents.ActionScenarioReportedEventArgs(emitter, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action),
            new OpenNettyEvents.WirelessBurglarAlarmStateReportedEventArgs(alarm, state)
        ], events);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(10)]
    public async Task ActionCommand_EmittedByNonAlarmUnit_DoesNotReportWirelessBurglarAlarmState(byte unit)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var emitter = harness.AddNitooEndpoint([OpenNettyCapabilities.ActionScenarioEvent], unit: unit);
        harness.AddNitooEndpoint([OpenNettyCapabilities.WirelessBurglarAlarmState], unit: 0);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.Action, CreateNitooAddress(unit: unit), OpenNettyMode.Broadcast));

                    AssertEvents([new OpenNettyEvents.ActionScenarioReportedEventArgs(emitter, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action)], events);
    }

    [Fact]
    public async Task ActionCommand_AlarmUnitEndpointWithoutAlarmStateCapability_DoesNotReportWirelessBurglarAlarmState()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var emitter = harness.AddNitooEndpoint([OpenNettyCapabilities.ActionScenarioEvent], unit: 4);
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState], unit: 0);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.Action, CreateNitooAddress(unit: 4), OpenNettyMode.Broadcast));

                    AssertEvents([new OpenNettyEvents.ActionScenarioReportedEventArgs(emitter, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action)], events);
    }

    [Fact]
    public async Task StopActionCommand_EmittedByAlarmUnit_DoesNotReportWirelessBurglarAlarmState()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var emitter = harness.AddNitooEndpoint([OpenNettyCapabilities.ActionScenarioEvent], unit: 4);
        harness.AddNitooEndpoint([OpenNettyCapabilities.WirelessBurglarAlarmState], unit: 0);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.StopAction, CreateNitooAddress(unit: 4), OpenNettyMode.Broadcast));

        AssertEvents([new OpenNettyEvents.ActionScenarioReportedEventArgs(emitter, OpenNettyModels.ScenariosPlus.ActionScenarioType.StopAction)], events);
    }

    [Theory]
    [InlineData(101, true, true, OpenNettyModels.Lighting.SwitchState.On, 100)]
    [InlineData(103, true, true, OpenNettyModels.Lighting.SwitchState.On, 100)]
    [InlineData(100, true, true, OpenNettyModels.Lighting.SwitchState.On, 100)]
    [InlineData(50, true, true, OpenNettyModels.Lighting.SwitchState.On, 50)]
    [InlineData(1, true, true, OpenNettyModels.Lighting.SwitchState.On, 1)]
    [InlineData(0, true, true, OpenNettyModels.Lighting.SwitchState.On, null)]
    [InlineData(102, true, true, OpenNettyModels.Lighting.SwitchState.Off, null)]
    [InlineData(104, true, true, OpenNettyModels.Lighting.SwitchState.Off, null)]
    [InlineData(105, true, true, null, null)]
    [InlineData(200, true, true, null, null)]
    [InlineData(101, true, false, OpenNettyModels.Lighting.SwitchState.On, null)]
    [InlineData(50, true, false, OpenNettyModels.Lighting.SwitchState.On, null)]
    [InlineData(102, true, false, OpenNettyModels.Lighting.SwitchState.Off, null)]
    [InlineData(101, false, true, null, 100)]
    [InlineData(50, false, true, null, 50)]
    [InlineData(102, false, true, null, null)]
    public async Task ActionCommand_EmitterWithScenarios_ReportsStateOfLinkedEndpoint(
        byte functionCode, bool switchState, bool dimming, OpenNettyModels.Lighting.SwitchState? state, int? brightness)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var emitter = harness.AddNitooEndpoint([OpenNettyCapabilities.ActionScenarioEvent], unit: 1, scenarios:
        [
            new OpenNettyScenario { EndpointName = "linked", FunctionCode = functionCode },
            new OpenNettyScenario { EndpointName = "unknown", FunctionCode = functionCode }
        ]);

        List<OpenNettyCapability> capabilities = [];
        if (switchState)
        {
            capabilities.Add(OpenNettyCapabilities.OnOffSwitchState);
        }

        if (dimming)
        {
            capabilities.Add(OpenNettyCapabilities.BasicDimmingState);
        }

        var linked = harness.AddNitooEndpoint(capabilities, unit: 2, name: "linked");

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.Action, CreateNitooAddress(unit: 1), OpenNettyMode.Broadcast));

        List<OpenNettyEvents.EventArgs> expected = [new OpenNettyEvents.ActionScenarioReportedEventArgs(emitter, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action)];
        if (state is OpenNettyModels.Lighting.SwitchState expectedState)
        {
            expected.Add(new OpenNettyEvents.SwitchStateReportedEventArgs(linked, expectedState));
        }

        if (brightness is int level)
        {
            expected.Add(new OpenNettyEvents.BrightnessReportedEventArgs(linked, (byte) level));
        }

        AssertEvents(expected, events);
    }

    [Fact]
    public async Task ActionCommand_LinkedEndpointInPushButtonMode_ReportsOnThenOff()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var emitter = harness.AddNitooEndpoint([OpenNettyCapabilities.ActionScenarioEvent], unit: 1, scenarios:
            [new OpenNettyScenario { EndpointName = "linked", FunctionCode = 100 }]);
        var linked = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState], unit: 2, name: "linked",
            settings: CreateSettings(OpenNettySettings.SwitchMode, OpenNettySettings.SwitchModes.PushButton));

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.Action, CreateNitooAddress(unit: 1), OpenNettyMode.Multicast));

        AssertEvents(
        [
            new OpenNettyEvents.ActionScenarioReportedEventArgs(emitter, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action),
            new OpenNettyEvents.SwitchStateReportedEventArgs(linked, OpenNettyModels.Lighting.SwitchState.On),
            new OpenNettyEvents.SwitchStateReportedEventArgs(linked, OpenNettyModels.Lighting.SwitchState.Off)
        ], events);
    }

    [Theory]
    [InlineData("10", 2)]
    [InlineData("12", 2)]
    [InlineData("13", 3)]
    [InlineData("5", 1)]
    [InlineData("7", 1)]
    [InlineData("8", 2)]
    public async Task ActionForTimeCommand_EndpointWithTimedScenarioEvent_ReportsRoundedDuration(string value, int seconds)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.TimedScenarioEvent]);
        var command = OpenNettyCommands.ScenariosPlus.ActionForTime.WithParameters([value]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress(), OpenNettyMode.Broadcast));

        AssertEvents([new OpenNettyEvents.TimedScenarioReportedEventArgs(endpoint, TimeSpan.FromSeconds(seconds))], events);
    }

    [Fact]
    public async Task ActionForTimeCommand_EndpointWithoutTimedScenarioEvent_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.ActionScenarioEvent, OpenNettyCapabilities.OnOffSwitchState]);
        var command = OpenNettyCommands.ScenariosPlus.ActionForTime.WithParameters(["10"]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress(), OpenNettyMode.Broadcast));

        Assert.Empty(events);
    }

    [Fact]
    public async Task ActionForTimeCommand_UnicastTransmission_IsIgnored()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.TimedScenarioEvent]);
        var command = OpenNettyCommands.ScenariosPlus.ActionForTime.WithParameters(["10"]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress(), OpenNettyMode.Unicast));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData("10", 2)]
    [InlineData("13", 3)]
    [InlineData("2", 0)]
    public async Task ActionInTimeCommand_EndpointWithProgressiveScenarioEvent_ReportsRoundedDuration(string value, int seconds)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.ProgressiveScenarioEvent]);
        var command = OpenNettyCommands.ScenariosPlus.ActionInTime.WithParameters([value]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress(), OpenNettyMode.Broadcast));

        AssertEvents([new OpenNettyEvents.ProgressiveScenarioReportedEventArgs(endpoint, TimeSpan.FromSeconds(seconds))], events);
    }

    [Fact]
    public async Task ActionInTimeCommand_EndpointWithoutProgressiveScenarioEvent_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.TimedScenarioEvent]);
        var command = OpenNettyCommands.ScenariosPlus.ActionInTime.WithParameters(["10"]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress(), OpenNettyMode.Broadcast));

        Assert.Empty(events);
    }

    [Fact]
    public async Task MainHandler_InvalidMessage_LogsWarningAndKeepsProcessingMessages()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);

        await harness.ReceiveAsync(CreateDimensionRead(
            OpenNettyProtocol.Scs, OpenNettyDimensions.Lighting.DimmerLevelSpeed, ScsPointAddress, values: "300"));

        await AssertWarningLoggedAndResubscribedAsync<OverflowException>(harness);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress));
        AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.On)], events);
    }

    [Fact]
    public async Task MainHandler_EndpointLookupFailure_LogsWarningAndKeepsProcessingMessages()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OnOffSwitchState]);
        harness.Manager.AddressLookupFailure = new InvalidOperationException();

        await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress));

        await AssertWarningLoggedAndResubscribedAsync<InvalidOperationException>(harness);

        harness.Manager.AddressLookupFailure = null;
        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.Off, ScsPointAddress));
        AssertEvents([new OpenNettyEvents.SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.Off)], events);
    }

    public static TheoryData<string> BrightnessTriggerKinds => ["Action", "ActionForTime", "ActionInTime", "DimmerStep"];

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task OnPercentCommand_EndpointWithBasicAndAdvancedDimming_RetrievesBrightness(OpenNettyProtocol protocol)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var address = CreateAddress(protocol);
        var endpoint = harness.AddEndpoint(protocol, address,
            [OpenNettyCapabilities.BasicDimmingState, OpenNettyCapabilities.AdvancedDimmingState]);

        await harness.PostReceivedAsync(CreateCommand(protocol, OpenNettyCommands.Lighting.On30, address));

        Assert.Equal([endpoint], await harness.BrightnessRequests.WaitAsync(1));
    }

    [Fact]
    public async Task OnPercentCommand_EndpointWithBasicDimmingOnly_DoesNotRetrieveBrightness()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.BasicDimmingState]);
        var advanced = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress,
            [OpenNettyCapabilities.BasicDimmingState, OpenNettyCapabilities.AdvancedDimmingState]);

        await harness.PostReceivedAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On50, ScsPointAddress));

        Assert.Equal([advanced], await harness.BrightnessRequests.WaitAsync(1));
        Assert.Empty(harness.BrightnessRequests.Drain());
    }

    [Fact]
    public async Task OnPercentCommand_SentMessage_DoesNotRetrieveBrightness()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Zigbee, CreateZigbeeAddress(),
            [OpenNettyCapabilities.BasicDimmingState, OpenNettyCapabilities.AdvancedDimmingState]);
        var received = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress,
            [OpenNettyCapabilities.BasicDimmingState, OpenNettyCapabilities.AdvancedDimmingState]);

        await harness.PostSentAsync(CreateCommand(OpenNettyProtocol.Zigbee, OpenNettyCommands.Lighting.On50, CreateZigbeeAddress()));
        await harness.PostReceivedAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On50, ScsPointAddress));

        Assert.Equal([received], await harness.BrightnessRequests.WaitAsync(1));
        Assert.Empty(harness.BrightnessRequests.Drain());
    }

    [Theory]
    [MemberData(nameof(BrightnessTriggerKinds))]
    public async Task ScenarioMessage_NitooEndpointWithScenarioEventAndDimming_RetrievesBrightness(string kind)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var trigger = CreateBrightnessTrigger(kind);
        var endpoint = harness.AddNitooEndpoint([trigger.Capability, OpenNettyCapabilities.BasicDimmingState]);

        await harness.PostReceivedAsync(trigger.Message);

        Assert.Equal([endpoint], await harness.BrightnessRequests.WaitAsync(1));
    }

    [Theory]
    [MemberData(nameof(BrightnessTriggerKinds))]
    public async Task ScenarioMessage_NitooEndpointWithoutScenarioEvent_DoesNotRetrieveBrightness(string kind)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var trigger = CreateBrightnessTrigger(kind);
        harness.AddNitooEndpoint([OpenNettyCapabilities.AdvancedDimmingState]);
        var withScenarioEvent = harness.AddNitooEndpoint([trigger.Capability, OpenNettyCapabilities.AdvancedDimmingState]);

        await harness.PostReceivedAsync(trigger.Message);

        Assert.Equal([withScenarioEvent], await harness.BrightnessRequests.WaitAsync(1));
        Assert.Empty(harness.BrightnessRequests.Drain());
    }

    [Theory]
    [MemberData(nameof(BrightnessTriggerKinds))]
    public async Task ScenarioMessage_NitooEndpointWithScenarioEventButWithoutDimming_DoesNotRetrieveBrightness(string kind)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var trigger = CreateBrightnessTrigger(kind);
        harness.AddNitooEndpoint([trigger.Capability, OpenNettyCapabilities.OnOffSwitchState], unit: 1);
        var dimmable = harness.AddNitooEndpoint([trigger.Capability, OpenNettyCapabilities.BasicDimmingState], unit: 1);

        await harness.PostReceivedAsync(trigger.Message);

        Assert.Equal([dimmable], await harness.BrightnessRequests.WaitAsync(1));
        Assert.Empty(harness.BrightnessRequests.Drain());
    }

    [Fact]
    public async Task ActionCommand_EndpointWithAssociatedUnit_RetrievesBrightnessOfAssociatedEndpointsWithDimming()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.ActionScenarioEvent], unit: 1, associatedUnit: 2);
        var associated = harness.AddNitooEndpoint([OpenNettyCapabilities.BasicDimmingState], unit: 2);
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState], unit: 3);

        await harness.PostReceivedAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.Action, CreateNitooAddress(unit: 1), OpenNettyMode.Broadcast));

        Assert.Equal([associated], await harness.BrightnessRequests.WaitAsync(1));
        Assert.Empty(harness.BrightnessRequests.Drain());
    }

    [Fact]
    public async Task DimmerStep_EndpointWithScenarios_RetrievesBrightnessOfLinkedEndpointsWithDimming()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.DimmingScenarioEvent], unit: 1, scenarios:
        [
            new OpenNettyScenario { EndpointName = "linked", FunctionCode = 100 },
            new OpenNettyScenario { EndpointName = "too-high-function-code", FunctionCode = 105 },
            new OpenNettyScenario { EndpointName = "without-dimming", FunctionCode = 100 }
        ]);
        var linked = harness.AddNitooEndpoint([OpenNettyCapabilities.AdvancedDimmingState], unit: 2, name: "linked");
        harness.AddNitooEndpoint([OpenNettyCapabilities.AdvancedDimmingState], unit: 3, name: "too-high-function-code");
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState], unit: 4, name: "without-dimming");

        await harness.PostReceivedAsync(CreateDimensionSet(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Lighting.DimmerStep, CreateNitooAddress(unit: 1), OpenNettyMode.Broadcast, "5"));

        Assert.Equal([linked], await harness.BrightnessRequests.WaitAsync(1));
        Assert.Empty(harness.BrightnessRequests.Drain());
    }

    [Fact]
    public async Task ScenarioMessage_ReceivedInCommandSession_DoesNotRetrieveBrightness()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.ActionScenarioEvent, OpenNettyCapabilities.BasicDimmingState], unit: 1);
        var other = harness.AddNitooEndpoint([OpenNettyCapabilities.ActionScenarioEvent, OpenNettyCapabilities.BasicDimmingState], unit: 2);

        await harness.PostReceivedAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.Action, CreateNitooAddress(unit: 1), OpenNettyMode.Broadcast), OpenNettySessionType.Command);
        await harness.PostReceivedAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.Action, CreateNitooAddress(unit: 2), OpenNettyMode.Broadcast));

        Assert.Equal([other], await harness.BrightnessRequests.WaitAsync(1));
        Assert.Empty(harness.BrightnessRequests.Drain());
    }

    [Fact]
    public async Task ActionForTimeCommand_ElapsedDuration_ReportsOffStateOnEndpointsInvolvedInTheScenario()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var emitter = harness.AddNitooEndpoint([OpenNettyCapabilities.TimedScenarioEvent], unit: 1, associatedUnit: 2, scenarios:
            [new OpenNettyScenario { EndpointName = "linked", FunctionCode = 100 }]);
        var associated = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState], unit: 2);
        var linked = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState], unit: 3, name: "linked");
        var command = OpenNettyCommands.ScenariosPlus.ActionForTime.WithParameters(["0"]);

        await harness.PostReceivedAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress(unit: 1), OpenNettyMode.Broadcast));

        AssertEvents(
        [
            new OpenNettyEvents.TimedScenarioReportedEventArgs(emitter, TimeSpan.Zero),
            new OpenNettyEvents.SwitchStateReportedEventArgs(associated, OpenNettyModels.Lighting.SwitchState.On),
            new OpenNettyEvents.SwitchStateReportedEventArgs(linked, OpenNettyModels.Lighting.SwitchState.On),
            new OpenNettyEvents.SwitchStateReportedEventArgs(associated, OpenNettyModels.Lighting.SwitchState.Off),
            new OpenNettyEvents.SwitchStateReportedEventArgs(linked, OpenNettyModels.Lighting.SwitchState.Off)
        ], await harness.WaitForEventsAsync(5));
    }

    [Fact]
    public async Task ActionForTimeCommand_ElapsedDuration_ReportsOffStateOnEmitterWithSwitchState()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var emitter = harness.AddNitooEndpoint([OpenNettyCapabilities.TimedScenarioEvent, OpenNettyCapabilities.OnOffSwitchState]);
        var command = OpenNettyCommands.ScenariosPlus.ActionForTime.WithParameters(["0"]);

        await harness.PostReceivedAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress(), OpenNettyMode.Broadcast));

        AssertEvents(
        [
            new OpenNettyEvents.TimedScenarioReportedEventArgs(emitter, TimeSpan.Zero),
            new OpenNettyEvents.SwitchStateReportedEventArgs(emitter, OpenNettyModels.Lighting.SwitchState.On),
            new OpenNettyEvents.SwitchStateReportedEventArgs(emitter, OpenNettyModels.Lighting.SwitchState.Off)
        ], await harness.WaitForEventsAsync(3));
    }

    public static TheoryData<string> PilotWireConfigurationTriggerKinds =>
    [
        "ReceivedSetpointMode",
        "ReceivedCancelDerogationMode",
        "SentSetpointMode",
        "SentCancelDerogationMode"
    ];

    [Theory]
    [MemberData(nameof(PilotWireConfigurationTriggerKinds))]
    public async Task PilotWireModeChange_EndpointWithPilotWireControl_RetrievesPilotWireConfiguration(string kind)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.PilotWireControl]);

        await PostPilotWireModeChangeAsync(harness, kind, CreateNitooAddress());

        Assert.Equal([endpoint], await harness.PilotWireConfigurationRequests.WaitAsync(1));
    }

    [Theory]
    [InlineData(OpenNettyMode.Unicast)]
    [InlineData(OpenNettyMode.Multicast)]
    public async Task PilotWireModeChange_NonBroadcastMessageReceived_DoesNotRetrievePilotWireConfiguration(OpenNettyMode mode)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.PilotWireControl], unit: 1);
        var sent = harness.AddNitooEndpoint([OpenNettyCapabilities.PilotWireControl], unit: 2);
        var command = OpenNettyCommands.TemperatureControl.WirePilotSetpointMode.WithParameters(["3"]);

        await harness.PostReceivedAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress(unit: 1), mode));
        await harness.PostSentAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress(unit: 2)));

        Assert.Equal([sent], await harness.PilotWireConfigurationRequests.WaitAsync(1));
        Assert.Empty(harness.PilotWireConfigurationRequests.Drain());
    }

    [Fact]
    public async Task PilotWireModeChange_EndpointWithoutPilotWireControl_DoesNotRetrievePilotWireConfiguration()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.PilotWireShutdown], unit: 1);
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.PilotWireControl], unit: 2);
        var command = OpenNettyCommands.TemperatureControl.WirePilotSetpointMode.WithParameters(["3"]);

        await harness.PostSentAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress(unit: 1)));
        await harness.PostSentAsync(CreateCommand(OpenNettyProtocol.Nitoo, command, CreateNitooAddress(unit: 2)));

        Assert.Equal([endpoint], await harness.PilotWireConfigurationRequests.WaitAsync(1));
        Assert.Empty(harness.PilotWireConfigurationRequests.Drain());
    }

    [Fact]
    public async Task SmartMeterRateTypeRead_OffPeakRate_ReportsSetpointModeOfWaterHeatersSharingTheDeviceIdentifier()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.SmartMeterInformation], identifier: 1000, unit: 1);
        var forcedOff = harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating], identifier: 1000, unit: 2);
        var automatic = harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating], identifier: 1000, unit: 3);
        var forcedOn = harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating], identifier: 1000, unit: 4);
        var forcedOffWhenIdle = harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating], identifier: 1000, unit: 5);
        var unknown = harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating], identifier: 1000, unit: 6);
        var otherDevice = harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating], identifier: 2000, unit: 2);
        var withoutWaterHeating = harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState], identifier: 1000, unit: 7);
        harness.AddNitooEndpoint([OpenNettyCapabilities.SmartMeterInformation], identifier: 3000, unit: 1);
        var peakRateWaterHeater = harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating], identifier: 3000, unit: 2);

        harness.UnitDescriptionProvider = endpoint => new OpenNettyModels.Diagnostics.UnitDescription
        {
            FunctionCode = 133,
            Values =
            [
                endpoint.Equals(forcedOff) ? "0" :
                endpoint.Equals(automatic) ? "33" :
                endpoint.Equals(forcedOn) ? "17" :
                endpoint.Equals(forcedOffWhenIdle) ? "32" : "5"
            ]
        };

        await harness.PostReceivedAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.TemperatureControl.SmartMeterRateType,
            CreateNitooAddress(1000, 1), OpenNettyMode.Broadcast, "2"));
        await harness.PostReceivedAsync(CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.TemperatureControl.SmartMeterRateType,
            CreateNitooAddress(3000, 1), OpenNettyMode.Broadcast, "3"));

        var events = await harness.WaitForEventsAsync(6);

        Assert.Contains(new OpenNettyEvents.WaterHeaterSetpointModeReportedEventArgs(forcedOff, OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOff), events);
        Assert.Contains(new OpenNettyEvents.WaterHeaterSetpointModeReportedEventArgs(automatic, OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic), events);
        Assert.Contains(new OpenNettyEvents.WaterHeaterSetpointModeReportedEventArgs(forcedOn, OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn), events);
        Assert.Contains(new OpenNettyEvents.WaterHeaterSetpointModeReportedEventArgs(forcedOffWhenIdle, OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOff), events);
        Assert.DoesNotContain(events, arguments => arguments is OpenNettyEvents.WaterHeaterSetpointModeReportedEventArgs { Endpoint: var endpoint } &&
            (endpoint.Equals(unknown) || endpoint.Equals(otherDevice) || endpoint.Equals(withoutWaterHeating) || endpoint.Equals(peakRateWaterHeater)));

        var requested = harness.UnitDescriptionRequests.Drain();
        Assert.DoesNotContain(otherDevice, requested);
        Assert.DoesNotContain(withoutWaterHeating, requested);
    }

    [Fact]
    public async Task WaterHeatingModeSet_SentMessage_RetrievesWaterHeaterState()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating]);

        await harness.PostSentAsync(CreateDimensionSet(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.TemperatureControl.WaterHeatingMode, CreateNitooAddress(), values: "1"));

        Assert.Equal([endpoint], await harness.WaterHeaterStateRequests.WaitAsync(1));
    }

    [Fact]
    public async Task WaterHeatingModeSet_ReceivedMessageOrEndpointWithoutWaterHeating_DoesNotRetrieveWaterHeaterState()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating], unit: 1);
        harness.AddNitooEndpoint([OpenNettyCapabilities.SmartMeterInformation], unit: 2);
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.WaterHeating], unit: 3);

        await harness.PostReceivedAsync(CreateDimensionSet(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.TemperatureControl.WaterHeatingMode, CreateNitooAddress(unit: 1), values: "1"));
        await harness.PostSentAsync(CreateDimensionSet(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.TemperatureControl.WaterHeatingMode, CreateNitooAddress(unit: 2), values: "1"));
        await harness.PostSentAsync(CreateDimensionSet(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.TemperatureControl.WaterHeatingMode, CreateNitooAddress(unit: 3), values: "1"));

        Assert.Equal([endpoint], await harness.WaterHeaterStateRequests.WaitAsync(1));
        Assert.Empty(harness.WaterHeaterStateRequests.Drain());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SessionNotification_NonCommandSession_ReportsAvailabilityOfEveryGatewayEndpoint(bool open)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var first = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, []);
        var second = harness.AddNitooEndpoint([]);
        var availability = open ? OpenNettyModels.Diagnostics.Availability.Online : OpenNettyModels.Diagnostics.Availability.Offline;

        var events = await harness.PublishAsync(CreateSessionNotification(harness, open, OpenNettySessionType.Event));

        AssertEvents(
        [
            new OpenNettyEvents.AvailabilityReportedEventArgs(first, availability),
            new OpenNettyEvents.AvailabilityReportedEventArgs(second, availability)
        ], events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SessionNotification_CommandSession_IsIgnored(bool open)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, []);

        var events = await harness.PublishAsync(CreateSessionNotification(harness, open, OpenNettySessionType.Command));

        Assert.Empty(events);
    }

    [Fact]
    public async Task AvailabilityHandler_EndpointLookupFailure_LogsWarningAndKeepsProcessingNotifications()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, []);
        harness.Manager.GatewayLookupFailure = new InvalidOperationException();

        await harness.PublishAsync(new OpenNettyNotifications.SessionOpen { Gateway = harness.Gateway, Session = harness.CreateSession() });

        await AssertWarningLoggedAndResubscribedAsync<InvalidOperationException>(harness);

        harness.Manager.GatewayLookupFailure = null;
        var events = await harness.PublishAsync(new OpenNettyNotifications.SessionOpen { Gateway = harness.Gateway, Session = harness.CreateSession() });
        AssertEvents([new OpenNettyEvents.AvailabilityReportedEventArgs(endpoint, OpenNettyModels.Diagnostics.Availability.Online)], events);
    }

    [Fact]
    public async Task IncomingMessage_EndpointWithRawIncomingMessages_ReportsIncomingMessage()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddGatewayEndpoint([], CreateSettings(OpenNettySettings.RawIncomingMessages, "true"));
        var notification = harness.CreateReceivedNotification(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress));

        var events = await harness.PublishAsync(notification);

        AssertEvents([new OpenNettyEvents.IncomingMessageReportedEventArgs(endpoint, notification.Message, notification.Session)], events);
    }

    [Fact]
    public async Task IncomingMessage_CommandSession_IsStillReported()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddGatewayEndpoint([], CreateSettings(OpenNettySettings.RawIncomingMessages, "true"));
        var notification = harness.CreateReceivedNotification(
            CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress), OpenNettySessionType.Command);

        var events = await harness.PublishAsync(notification);

        AssertEvents([new OpenNettyEvents.IncomingMessageReportedEventArgs(endpoint, notification.Message, notification.Session)], events);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("invalid")]
    public async Task IncomingMessage_EndpointWithoutRawIncomingMessages_DoesNotReportAnything(string value)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddGatewayEndpoint([], CreateSettings(OpenNettySettings.RawIncomingMessages, value));
        harness.AddGatewayEndpoint([]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress));

        Assert.Empty(events);
    }

    [Fact]
    public async Task IncomingMessage_EndpointWithRawOutgoingMessagesOnly_DoesNotReportIncomingMessage()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddGatewayEndpoint([], CreateSettings(OpenNettySettings.RawOutgoingMessages, "true"));

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress));

        Assert.Empty(events);
    }

    [Fact]
    public async Task OutgoingMessage_EndpointWithRawOutgoingMessages_ReportsOutgoingMessage()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddGatewayEndpoint([], CreateSettings(OpenNettySettings.RawOutgoingMessages, "true"));
        var notification = harness.CreateSentNotification(
            CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress), OpenNettySessionType.Command);

        var events = await harness.PublishAsync(notification);

        AssertEvents([new OpenNettyEvents.OutgoingMessageReportedEventArgs(endpoint, notification.Message, notification.Session)], events);
    }

    [Fact]
    public async Task OutgoingMessage_EndpointWithoutRawOutgoingMessages_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddGatewayEndpoint([], CreateSettings(OpenNettySettings.RawIncomingMessages, "true"));

        var events = await harness.SendAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RawMessageHandlers_EndpointLookupFailure_LogsWarningAndKeepsProcessingNotifications(bool incoming)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddGatewayEndpoint([], CreateSettings(
            incoming ? OpenNettySettings.RawIncomingMessages : OpenNettySettings.RawOutgoingMessages, "true"));
        var message = CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, ScsPointAddress);
        harness.Manager.DeviceLookupFailure = new InvalidOperationException();

        await harness.PublishAsync(incoming ? harness.CreateReceivedNotification(message) : harness.CreateSentNotification(message));

        await AssertWarningLoggedAndResubscribedAsync<InvalidOperationException>(harness);

        harness.Manager.DeviceLookupFailure = null;
        var events = await harness.PublishAsync(incoming ? harness.CreateReceivedNotification(message) : harness.CreateSentNotification(message));
        var reported = Assert.Single(events);
        Assert.Equal(endpoint, reported.Endpoint);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task DeviceCommunication_MessageReceivedFromUnit_ReportsCommunicationOnDeviceEndpoint(OpenNettyProtocol protocol)
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var deviceAddress = protocol is OpenNettyProtocol.Nitoo ? CreateNitooAddress(unit: 0) : CreateZigbeeAddress(unit: 0);
        var endpoint = harness.AddEndpoint(protocol, deviceAddress, [OpenNettyCapabilities.OutgoingCommunication]);
        harness.AddEndpoint(protocol, deviceAddress, [OpenNettyCapabilities.OnOffSwitchState]);
        var message = CreateCommand(protocol, OpenNettyCommands.Lighting.On, CreateAddress(protocol));

        var events = await harness.ReceiveAsync(message);

        Assert.Equal(new OpenNettyEvents.DeviceCommunicationReportedEventArgs(endpoint, message), Assert.Single(events.OfType<OpenNettyEvents.DeviceCommunicationReportedEventArgs>()));
    }

    [Fact]
    public async Task DeviceCommunication_ScsMessage_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Scs, ScsPointAddress, [OpenNettyCapabilities.OutgoingCommunication]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.Toggle, ScsPointAddress));

        Assert.Empty(events);
    }

    [Fact]
    public async Task DeviceCommunication_MessageWithoutAddress_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddEndpoint(OpenNettyProtocol.Zigbee, null, [OpenNettyCapabilities.OutgoingCommunication]);

        var events = await harness.ReceiveAsync(CreateCommand(OpenNettyProtocol.Zigbee, OpenNettyCommands.Management.CreateZigbeeNetwork, null));

        Assert.Empty(events);
    }

    [Fact]
    public async Task DeviceCommunication_MessageSentToDevice_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.OutgoingCommunication], unit: 0);

        var events = await harness.SendAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.Lighting.On, CreateNitooAddress(unit: 1), OpenNettyMode.Unicast));

        Assert.Empty(events);
    }

    [Fact]
    public async Task DeviceCommunication_EndpointWithoutOutgoingCommunication_DoesNotReportAnything()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        harness.AddNitooEndpoint([OpenNettyCapabilities.OnOffSwitchState], unit: 0);

        var events = await harness.ReceiveAsync(CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.Lighting.Toggle, CreateNitooAddress(unit: 1)));

        Assert.Empty(events);
    }

    [Fact]
    public async Task DeviceCommunicationHandler_EndpointLookupFailure_LogsWarningAndKeepsProcessingNotifications()
    {
        await using var harness = await CoordinatorHarness.StartAsync();
        var endpoint = harness.AddNitooEndpoint([OpenNettyCapabilities.OutgoingCommunication], unit: 0);
        var message = CreateDimensionRead(
            OpenNettyProtocol.Nitoo, OpenNettyDimensions.Diagnostics.UnitDescription, CreateNitooAddress(unit: 1), values: ["999", "1"]);
        harness.Manager.AddressLookupFailure = new InvalidOperationException();

        await harness.ReceiveAsync(message);

        await AssertWarningLoggedAndResubscribedAsync<InvalidOperationException>(harness);

        harness.Manager.AddressLookupFailure = null;
        var events = await harness.ReceiveAsync(message);
        AssertEvents([new OpenNettyEvents.DeviceCommunicationReportedEventArgs(endpoint, message)], events);
    }

    private static readonly OpenNettyAddress ScsPointAddress = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, 2);
    private static readonly OpenNettyAddress ScsAreaAddress = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, null);
    private static readonly OpenNettyAddress ScsGeneralAddress = OpenNettyAddress.FromScsLightPointAddress(0, true, null, null, null);

    private static readonly OpenNettyAddress ScsDryContactAddress = OpenNettyAddress.FromScsDryContactAddress(10);
    private static readonly OpenNettyAddress ScsScenarioPlusAddress = OpenNettyAddress.FromScsScenarioPlusAddress(5);

    private static OpenNettyAddress CreateNitooAddress(uint identifier = 1000, byte unit = 1)
        => OpenNettyAddress.FromNitooAddress(identifier, unit);

    private static OpenNettyAddress CreateZigbeeAddress(uint identifier = 1000, byte unit = 1)
        => OpenNettyAddress.FromDecimalZigbeeAddress(identifier, unit);

    private static OpenNettyMessage CreateCommand(
        OpenNettyProtocol protocol, OpenNettyCommand command,
        OpenNettyAddress? address, OpenNettyMode? mode = null)
        => OpenNettyMessage.CreateCommand(protocol, command, address, mode: mode);

    private static OpenNettyMessage CreateDimensionRead(
        OpenNettyProtocol protocol, OpenNettyDimension dimension,
        OpenNettyAddress? address, OpenNettyMode? mode = null, params string[] values)
        => OpenNettyMessage.CreateDimensionRead(protocol, dimension, [.. values], address, mode: mode);

    private static OpenNettyMessage CreateDimensionSet(
        OpenNettyProtocol protocol, OpenNettyDimension dimension,
        OpenNettyAddress? address, OpenNettyMode? mode = null, params string[] values)
        => OpenNettyMessage.CreateDimensionSet(protocol, dimension, [.. values], address, mode: mode);

    private static void AssertEvents(
        IEnumerable<OpenNettyEvents.EventArgs> expected, IEnumerable<OpenNettyEvents.EventArgs> actual)
        => Assert.Equal(
            expected.OrderBy(static arguments => arguments.ToString(), StringComparer.Ordinal),
            actual.OrderBy(static arguments => arguments.ToString(), StringComparer.Ordinal));

    private static OpenNettyAddress CreateAddress(OpenNettyProtocol protocol) => protocol switch
    {
        OpenNettyProtocol.Scs    => ScsPointAddress,
        OpenNettyProtocol.Nitoo  => CreateNitooAddress(),
        OpenNettyProtocol.Zigbee => CreateZigbeeAddress(),

        _ => throw new ArgumentOutOfRangeException(nameof(protocol))
    };

    private static ImmutableDictionary<OpenNettySetting, string> CreateSettings(OpenNettySetting setting, string value)
        => ImmutableDictionary<OpenNettySetting, string>.Empty.Add(setting, value);

    private static OpenNettyCapability GetCapability(string name) => name switch
    {
        nameof(OpenNettyCapabilities.BasicDimmingState)    => OpenNettyCapabilities.BasicDimmingState,
        nameof(OpenNettyCapabilities.AdvancedDimmingState) => OpenNettyCapabilities.AdvancedDimmingState,

        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static OpenNettyCommand GetOnOffCommand(bool on)
        => on ? OpenNettyCommands.Lighting.On : OpenNettyCommands.Lighting.Off;

    private static OpenNettyCommand GetOnPercentCommand(int level) => level switch
    {
        20  => OpenNettyCommands.Lighting.On20,
        30  => OpenNettyCommands.Lighting.On30,
        40  => OpenNettyCommands.Lighting.On40,
        50  => OpenNettyCommands.Lighting.On50,
        60  => OpenNettyCommands.Lighting.On60,
        70  => OpenNettyCommands.Lighting.On70,
        80  => OpenNettyCommands.Lighting.On80,
        90  => OpenNettyCommands.Lighting.On90,
        100 => OpenNettyCommands.Lighting.On100,

        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };

    private static OpenNettyCommand GetTimedOnCommand(string name) => name switch
    {
        nameof(OpenNettyCommands.Lighting.TimedOn1Minute)         => OpenNettyCommands.Lighting.TimedOn1Minute,
        nameof(OpenNettyCommands.Lighting.TimedOn2Minutes)        => OpenNettyCommands.Lighting.TimedOn2Minutes,
        nameof(OpenNettyCommands.Lighting.TimedOn3Minutes)        => OpenNettyCommands.Lighting.TimedOn3Minutes,
        nameof(OpenNettyCommands.Lighting.TimedOn4Minutes)        => OpenNettyCommands.Lighting.TimedOn4Minutes,
        nameof(OpenNettyCommands.Lighting.TimedOn5Minutes)        => OpenNettyCommands.Lighting.TimedOn5Minutes,
        nameof(OpenNettyCommands.Lighting.TimedOn15Minutes)       => OpenNettyCommands.Lighting.TimedOn15Minutes,
        nameof(OpenNettyCommands.Lighting.TimedOn30Seconds)       => OpenNettyCommands.Lighting.TimedOn30Seconds,
        nameof(OpenNettyCommands.Lighting.TimedOn500Milliseconds) => OpenNettyCommands.Lighting.TimedOn500Milliseconds,

        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static OpenNettyMessage CreateScsLightingMessage(string kind, OpenNettyAddress address) => kind switch
    {
        "On"               => CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On, address),
        "Off"              => CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.Off, address),
        "On50"             => CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.On50, address),
        "TimedOn1Minute"   => CreateCommand(OpenNettyProtocol.Scs, OpenNettyCommands.Lighting.TimedOn1Minute, address),
        "DimmerLevelSpeed" => CreateDimensionRead(OpenNettyProtocol.Scs, OpenNettyDimensions.Lighting.DimmerLevelSpeed, address, values: "150"),
        "DimmerStatus"     => CreateDimensionRead(OpenNettyProtocol.Scs, OpenNettyDimensions.Lighting.DimmerStatus, address, values: "150"),

        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static OpenNettyCommand GetShutterCommand(string name) => name switch
    {
        "Stop" => OpenNettyCommands.Automation.Stop,
        "Up"   => OpenNettyCommands.Automation.Up,
        "Down" => OpenNettyCommands.Automation.Down,

        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static (OpenNettyMessage Message, OpenNettyCapability Capability, Func<OpenNettyEndpoint, OpenNettyEvents.EventArgs> CreateEvent)
        CreateLinkedEndpointScenario(string kind, OpenNettyMode mode)
    {
        var address = CreateNitooAddress(unit: 1);

        return kind switch
        {
            "WaterHeatingMode" =>
            (
                CreateDimensionSet(OpenNettyProtocol.Nitoo, OpenNettyDimensions.TemperatureControl.WaterHeatingMode, address, mode, "1"),
                OpenNettyCapabilities.WaterHeating,
                static endpoint => new OpenNettyEvents.WaterHeaterSetpointModeReportedEventArgs(endpoint, OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn)
            ),

            "PilotWireSetpointMode" =>
            (
                CreateCommand(OpenNettyProtocol.Nitoo, OpenNettyCommands.TemperatureControl.WirePilotSetpointMode.WithParameters(["3"]), address, mode),
                OpenNettyCapabilities.PilotWireControl,
                static endpoint => new OpenNettyEvents.PilotWireSetpointModeReportedEventArgs(endpoint, OpenNettyModels.TemperatureControl.PilotWireMode.Eco)
            ),

            "PilotWireDerogationMode" =>
            (
                CreateCommand(OpenNettyProtocol.Nitoo, OpenNettyCommands.TemperatureControl.WirePilotDerogationMode.WithParameters(["35"]), address, mode),
                OpenNettyCapabilities.PilotWireDerogation,
                static endpoint => new OpenNettyEvents.PilotWireDerogationModeReportedEventArgs(endpoint, OpenNettyModels.TemperatureControl.PilotWireMode.Eco, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours)
            ),

            "CancelPilotWireDerogationMode" =>
            (
                CreateCommand(OpenNettyProtocol.Nitoo, OpenNettyCommands.TemperatureControl.CancelWirePilotDerogationMode, address, mode),
                OpenNettyCapabilities.PilotWireDerogation,
                static endpoint => new OpenNettyEvents.PilotWireDerogationModeReportedEventArgs(endpoint, null, null)
            ),

            "PilotWireShutdownMode" =>
            (
                CreateCommand(OpenNettyProtocol.Nitoo, OpenNettyCommands.TemperatureControl.WirePilotShutdownMode, address, mode),
                OpenNettyCapabilities.PilotWireShutdown,
                static endpoint => new OpenNettyEvents.PilotWireShutdownModeReportedEventArgs(endpoint, true)
            ),

            "CancelPilotWireShutdownMode" =>
            (
                CreateCommand(OpenNettyProtocol.Nitoo, OpenNettyCommands.TemperatureControl.CancelWirePilotShutdownMode, address, mode),
                OpenNettyCapabilities.PilotWireShutdown,
                static endpoint => new OpenNettyEvents.PilotWireShutdownModeReportedEventArgs(endpoint, false)
            ),

            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static OpenNettyCommand GetPressurePlusCommand(string name) => name switch
    {
        nameof(OpenNettyCommands.ScenariosPlus.ShortPressure)           => OpenNettyCommands.ScenariosPlus.ShortPressure,
        nameof(OpenNettyCommands.ScenariosPlus.StartOfExtendedPressure) => OpenNettyCommands.ScenariosPlus.StartOfExtendedPressure,
        nameof(OpenNettyCommands.ScenariosPlus.ExtendedPressure)        => OpenNettyCommands.ScenariosPlus.ExtendedPressure,
        nameof(OpenNettyCommands.ScenariosPlus.EndOfExtendedPressure)   => OpenNettyCommands.ScenariosPlus.EndOfExtendedPressure,

        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static async Task PostPilotWireModeChangeAsync(CoordinatorHarness harness, string kind, OpenNettyAddress address)
    {
        var setpoint = OpenNettyCommands.TemperatureControl.WirePilotSetpointMode.WithParameters(["3"]);
        var cancel = OpenNettyCommands.TemperatureControl.CancelWirePilotDerogationMode;

        switch (kind)
        {
            case "ReceivedSetpointMode":
                await harness.PostReceivedAsync(CreateCommand(OpenNettyProtocol.Nitoo, setpoint, address, OpenNettyMode.Broadcast));
                break;

            case "ReceivedCancelDerogationMode":
                await harness.PostReceivedAsync(CreateCommand(OpenNettyProtocol.Nitoo, cancel, address, OpenNettyMode.Broadcast));
                break;

            case "SentSetpointMode":
                await harness.PostSentAsync(CreateCommand(OpenNettyProtocol.Nitoo, setpoint, address));
                break;

            case "SentCancelDerogationMode":
                await harness.PostSentAsync(CreateCommand(OpenNettyProtocol.Nitoo, cancel, address));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static (OpenNettyMessage Message, OpenNettyCapability Capability) CreateBrightnessTrigger(string kind)
    {
        var address = CreateNitooAddress(unit: 1);

        return kind switch
        {
            "Action" =>
            (
                CreateCommand(OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.Action, address, OpenNettyMode.Broadcast),
                OpenNettyCapabilities.ActionScenarioEvent
            ),

            "ActionForTime" =>
            (
                CreateCommand(OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.ActionForTime.WithParameters(["10"]), address, OpenNettyMode.Broadcast),
                OpenNettyCapabilities.TimedScenarioEvent
            ),

            "ActionInTime" =>
            (
                CreateCommand(OpenNettyProtocol.Nitoo, OpenNettyCommands.ScenariosPlus.ActionInTime.WithParameters(["10"]), address, OpenNettyMode.Broadcast),
                OpenNettyCapabilities.ProgressiveScenarioEvent
            ),

            "DimmerStep" =>
            (
                CreateDimensionSet(OpenNettyProtocol.Nitoo, OpenNettyDimensions.Lighting.DimmerStep, address, OpenNettyMode.Broadcast, "5"),
                OpenNettyCapabilities.DimmingScenarioEvent
            ),

            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static OpenNettyNotification CreateSessionNotification(CoordinatorHarness harness, bool open, OpenNettySessionType type)
        => open
            ? new OpenNettyNotifications.SessionOpen { Gateway = harness.Gateway, Session = harness.CreateSession(type) }
            : new OpenNettyNotifications.SessionClosed { Gateway = harness.Gateway, Session = harness.CreateSession(type) };

    private static OpenNettyCommand GetZigbeeNetworkCommand(string name) => name switch
    {
        nameof(OpenNettyCommands.Management.CreateZigbeeNetwork) => OpenNettyCommands.Management.CreateZigbeeNetwork,
        nameof(OpenNettyCommands.Management.CloseZigbeeNetwork)  => OpenNettyCommands.Management.CloseZigbeeNetwork,
        nameof(OpenNettyCommands.Management.OpenZigbeeNetwork)   => OpenNettyCommands.Management.OpenZigbeeNetwork,
        nameof(OpenNettyCommands.Management.JoinZigbeeNetwork)   => OpenNettyCommands.Management.JoinZigbeeNetwork,
        nameof(OpenNettyCommands.Management.LeaveZigbeeNetwork)  => OpenNettyCommands.Management.LeaveZigbeeNetwork,

        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static OpenNettyCommand GetZigbeeBindingCommand(string name) => name switch
    {
        nameof(OpenNettyCommands.ScenariosPlus.OpenBinding)   => OpenNettyCommands.ScenariosPlus.OpenBinding,
        nameof(OpenNettyCommands.ScenariosPlus.CloseBinding)  => OpenNettyCommands.ScenariosPlus.CloseBinding,
        nameof(OpenNettyCommands.ScenariosPlus.CancelBinding) => OpenNettyCommands.ScenariosPlus.CancelBinding,

        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static async Task AssertWarningLoggedAndResubscribedAsync<TException>(CoordinatorHarness harness)
        where TException : Exception
    {
        var entry = Assert.Single(await harness.Logger.Entries.WaitAsync(1));

        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(6018, entry.EventId.Id);
        Assert.IsType<TException>(entry.Exception);

        await harness.Pipeline.WaitForSubscriptionAsync();
    }

    private static OpenNettyMessage CreateUnsupportedMessage(string kind) => kind switch
    {
        "StatusRequest"    => OpenNettyMessage.CreateStatusRequest(OpenNettyProtocol.Scs, OpenNettyCategories.Lighting, ScsPointAddress),
        "DimensionRequest" => OpenNettyMessage.CreateDimensionRequest(OpenNettyProtocol.Scs, OpenNettyDimensions.Lighting.DimmerLevelSpeed, ScsPointAddress),
        "Acknowledgement"  => OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, OpenNettyFrames.Acknowledgement),

        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static OpenNettyCapability[] GetAllCapabilities() =>
    [
        OpenNettyCapabilities.OnOffSwitchState,
        OpenNettyCapabilities.BasicDimmingState,
        OpenNettyCapabilities.BasicShutterState,
        OpenNettyCapabilities.WaterHeating,
        OpenNettyCapabilities.PilotWireControl,
        OpenNettyCapabilities.OutgoingCommunication
    ];

    private sealed class CoordinatorHarness : IAsyncDisposable
    {
        private static readonly OpenNettyDeviceDefinition GatewayDeviceDefinition = new()
        {
            Id = Guid.NewGuid(),
            Protocol = OpenNettyProtocol.Scs
        };

        private readonly Channel<OpenNettyEvents.EventArgs> _events = Channel.CreateUnbounded<OpenNettyEvents.EventArgs>();
        private readonly List<IAsyncDisposable> _disposables = [];
        private int _counter;

        private CoordinatorHarness()
        {
            var device = new OpenNettyDevice { Definition = GatewayDeviceDefinition, Name = "gateway" };

            Gateway = new OpenNettyGateway
            {
                ConnectionType = OpenNettyConnectionType.Tcp,
                Device = device,
                Options = OpenNettyGatewayOptions.CreateDefaults(device)
            };

            Manager = new CoordinatorManager { Gateway = Gateway };
            Pipeline = new CoordinatorPipeline();
            Events = new OpenNettyEvents(Substitute.For<IHostApplicationLifetime>());
            Logger = new CoordinatorLogger();
            Controller = Substitute.For<OpenNettyController>(Manager, Substitute.For<IOpenNettyService>());

            Controller.EnumerateBrightnessAsync(Arg.Any<OpenNettyEndpoint>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                BrightnessRequests.Record(call.ArgAt<OpenNettyEndpoint>(0));
                return CoordinatorManager.EmptyAsync<(OpenNettyEndpoint Endpoint, byte Level)>();
            });

            // Note: NSubstitute requires the ValueTask-returning call to be used as the receiver of Returns().
#pragma warning disable CA2012
            Controller.GetPilotWireConfigurationAsync(Arg.Any<OpenNettyEndpoint>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                PilotWireConfigurationRequests.Record(call.ArgAt<OpenNettyEndpoint>(0));
                return new ValueTask<OpenNettyModels.TemperatureControl.PilotWireConfiguration>(
                    new OpenNettyModels.TemperatureControl.PilotWireConfiguration
                    {
                        DerogationDuration = OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None,
                        IsDerogationActive = false,
                        IsShutdownActive = false,
                        Mode = OpenNettyModels.TemperatureControl.PilotWireMode.Comfort
                    });
            });

            Controller.GetWaterHeaterStateAsync(Arg.Any<OpenNettyEndpoint>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                WaterHeaterStateRequests.Record(call.ArgAt<OpenNettyEndpoint>(0));
                return new ValueTask<OpenNettyModels.TemperatureControl.WaterHeaterState>(
                    OpenNettyModels.TemperatureControl.WaterHeaterState.Idle);
            });

            Controller.GetUnitDescriptionAsync(Arg.Any<OpenNettyEndpoint>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                UnitDescriptionRequests.Record(call.ArgAt<OpenNettyEndpoint>(0));
                return new ValueTask<OpenNettyModels.Diagnostics.UnitDescription>(UnitDescriptionProvider(call.ArgAt<OpenNettyEndpoint>(0)));
            });
#pragma warning restore CA2012

            Coordinator = new OpenNettyCoordinator(Controller, Events, Logger, Manager, Pipeline);
            Marker = AddEndpoint(OpenNettyProtocol.Scs, null, [], name: "marker");
        }

        public OpenNettyController Controller { get; }

        public OpenNettyCoordinator Coordinator { get; }

        public OpenNettyEvents Events { get; }

        public OpenNettyGateway Gateway { get; }

        public CoordinatorLogger Logger { get; }

        public CoordinatorManager Manager { get; }

        public OpenNettyEndpoint Marker { get; }

        public CoordinatorPipeline Pipeline { get; }

        public IAsyncDisposable Subscription { get; private set; } = null!;

        public CoordinatorRecorder<OpenNettyEndpoint> BrightnessRequests { get; } = new();

        public CoordinatorRecorder<OpenNettyEndpoint> PilotWireConfigurationRequests { get; } = new();

        public CoordinatorRecorder<OpenNettyEndpoint> UnitDescriptionRequests { get; } = new();

        public CoordinatorRecorder<OpenNettyEndpoint> WaterHeaterStateRequests { get; } = new();

        public Func<OpenNettyEndpoint, OpenNettyModels.Diagnostics.UnitDescription> UnitDescriptionProvider { get; set; }
            = static _ => new() { FunctionCode = 0, Values = ["0"] };

        public static async Task<CoordinatorHarness> StartAsync()
        {
            var harness = new CoordinatorHarness();

            foreach (var property in typeof(OpenNettyEvents).GetProperties())
            {
                if (property.PropertyType is { IsGenericType: true } type &&
                    type.GetGenericTypeDefinition() == typeof(IAsyncObservable<>))
                {
                    var method = typeof(CoordinatorHarness)
                        .GetMethod(nameof(ObserveAsync), BindingFlags.Instance | BindingFlags.NonPublic)!
                        .MakeGenericMethod(type.GetGenericArguments()[0]);

                    harness._disposables.Add(await (ValueTask<IAsyncDisposable>) method.Invoke(
                        harness, [property.GetValue(harness.Events)])!);
                }
            }

            harness._disposables.Add(await harness.Events.ConnectAsync());
            harness.Subscription = await ((IOpenNettyHandler) harness.Coordinator).SubscribeAsync();
            harness.Pipeline.DiscardSubscriptions();

            return harness;
        }

        public OpenNettyEndpoint AddEndpoint(
            OpenNettyProtocol protocol, OpenNettyAddress? address,
            IEnumerable<OpenNettyCapability> capabilities,
            ImmutableDictionary<OpenNettySetting, string>? settings = null,
            OpenNettyUnit? unit = null, string? name = null)
        {
            var endpoint = new OpenNettyEndpoint
            {
                Address = address,
                Capabilities = [.. capabilities],
                Gateway = Gateway,
                Name = name ?? string.Create(CultureInfo.InvariantCulture, $"endpoint-{Interlocked.Increment(ref _counter)}"),
                Protocol = protocol,
                Settings = settings ?? [],
                Unit = unit
            };

            Manager.Endpoints.Add(endpoint);

            return endpoint;
        }

        public OpenNettyEndpoint AddNitooEndpoint(
            IEnumerable<OpenNettyCapability> capabilities,
            uint identifier = 1000, byte unit = 1, byte? associatedUnit = null,
            IEnumerable<OpenNettyScenario>? scenarios = null,
            ImmutableDictionary<OpenNettySetting, string>? settings = null,
            string? name = null)
        {
            var definition = new OpenNettyDeviceDefinition { Id = Guid.NewGuid(), Protocol = OpenNettyProtocol.Nitoo };
            var device = new OpenNettyDevice
            {
                Definition = definition,
                Name = string.Create(CultureInfo.InvariantCulture, $"device-{Interlocked.Increment(ref _counter)}")
            };

            return AddEndpoint(OpenNettyProtocol.Nitoo, OpenNettyAddress.FromNitooAddress(identifier, unit), capabilities, settings, new OpenNettyUnit
            {
                Definition = new OpenNettyUnitDefinition { AssociatedUnitId = associatedUnit, Device = definition, Id = unit },
                Device = device,
                Scenarios = [.. scenarios ?? []]
            }, name);
        }

        public OpenNettyEndpoint AddGatewayEndpoint(
            IEnumerable<OpenNettyCapability> capabilities,
            ImmutableDictionary<OpenNettySetting, string>? settings = null)
            => AddEndpoint(OpenNettyProtocol.Scs, null, capabilities, settings, new OpenNettyUnit
            {
                Definition = new OpenNettyUnitDefinition { Device = GatewayDeviceDefinition },
                Device = Gateway.Device
            });

        public OpenNettySession CreateSession(OpenNettySessionType type = OpenNettySessionType.Event)
            => (OpenNettySession) typeof(OpenNettySession)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single()
                .Invoke([Gateway, type, Substitute.For<OpenNettyConnection>()]);

        public OpenNettyNotifications.MessageReceived CreateReceivedNotification(
            OpenNettyMessage message, OpenNettySessionType sessionType = OpenNettySessionType.Event)
            => new()
            {
                Gateway = Gateway,
                Message = message,
                Session = CreateSession(sessionType)
            };

        public OpenNettyNotifications.MessageSent CreateSentNotification(
            OpenNettyMessage message, OpenNettySessionType sessionType = OpenNettySessionType.Event)
            => new()
            {
                Gateway = Gateway,
                Message = message,
                Session = CreateSession(sessionType),
                Transaction = OpenNettyTransaction.Create()
            };

        public Task PostReceivedAsync(OpenNettyMessage message, OpenNettySessionType sessionType = OpenNettySessionType.Event)
            => Pipeline.PublishAsync(CreateReceivedNotification(message, sessionType), TestContext.Current.CancellationToken).AsTask();

        public Task PostSentAsync(OpenNettyMessage message, OpenNettySessionType sessionType = OpenNettySessionType.Event)
            => Pipeline.PublishAsync(CreateSentNotification(message, sessionType), TestContext.Current.CancellationToken).AsTask();

        public Task<IReadOnlyList<OpenNettyEvents.EventArgs>> ReceiveAsync(
            OpenNettyMessage message, OpenNettySessionType sessionType = OpenNettySessionType.Event)
            => PublishAsync(CreateReceivedNotification(message, sessionType));

        public Task<IReadOnlyList<OpenNettyEvents.EventArgs>> SendAsync(
            OpenNettyMessage message, OpenNettySessionType sessionType = OpenNettySessionType.Event)
            => PublishAsync(CreateSentNotification(message, sessionType));

        public async Task<IReadOnlyList<OpenNettyEvents.EventArgs>> PublishAsync(OpenNettyNotification notification)
        {
            await Pipeline.PublishAsync(notification, TestContext.Current.CancellationToken);

            return await CollectEventsAsync();
        }

        public async Task<IReadOnlyList<OpenNettyEvents.EventArgs>> CollectEventsAsync()
        {
            await Events.PublishAsync(new OpenNettyEvents.BatteryAlertReportedEventArgs(Marker), TestContext.Current.CancellationToken);

            List<OpenNettyEvents.EventArgs> events = [];

            while (true)
            {
                var arguments = await ReadAsync(_events.Reader);
                if (arguments is OpenNettyEvents.BatteryAlertReportedEventArgs { Endpoint: var endpoint } && endpoint == Marker)
                {
                    return events;
                }

                if (arguments.Endpoint != Marker)
                {
                    events.Add(arguments);
                }
            }
        }

        public async Task<IReadOnlyList<OpenNettyEvents.EventArgs>> WaitForEventsAsync(int count)
        {
            List<OpenNettyEvents.EventArgs> events = [];

            while (events.Count < count)
            {
                events.Add(await ReadAsync(_events.Reader));
            }

            return events;
        }

        public async ValueTask DisposeAsync()
        {
            await Subscription.DisposeAsync();

            foreach (var disposable in _disposables)
            {
                await disposable.DisposeAsync();
            }

            await Events.DisposeAsync();
        }

        private ValueTask<IAsyncDisposable> ObserveAsync<TEventArgs>(IAsyncObservable<TEventArgs> observable)
            where TEventArgs : OpenNettyEvents.EventArgs
            => observable.SubscribeAsync(arguments =>
            {
                _events.Writer.TryWrite(arguments);
                return ValueTask.CompletedTask;
            });
    }

    private sealed class CoordinatorManager() : OpenNettyManager(Substitute.For<IOptionsMonitor<OpenNettyOptions>>())
    {
        public List<OpenNettyEndpoint> Endpoints { get; } = [];

        public OpenNettyGateway? Gateway { get; init; }

        public Exception? AddressLookupFailure { get; set; }

        public Exception? DeviceLookupFailure { get; set; }

        public Exception? GatewayLookupFailure { get; set; }

        public static IAsyncEnumerable<T> EmptyAsync<T>() => new CoordinatorAsyncList<T>([]);

        public override IAsyncEnumerable<OpenNettyEndpoint> EnumerateEndpointsAsync(CancellationToken cancellationToken = default)
            => new CoordinatorAsyncList<OpenNettyEndpoint>([.. Endpoints]);

        public override IAsyncEnumerable<OpenNettyGateway> EnumerateGatewaysAsync(CancellationToken cancellationToken = default)
            => new CoordinatorAsyncList<OpenNettyGateway>(Gateway is null ? [] : [Gateway]);

        public override ValueTask<OpenNettyEndpoint?> FindEndpointByNameAsync(string name, CancellationToken cancellationToken = default)
            => new(Endpoints.Find(endpoint => string.Equals(endpoint.Name, name, StringComparison.Ordinal)));

        public override IAsyncEnumerable<OpenNettyEndpoint> FindEndpointsByAddressAsync(
            OpenNettyGateway gateway, OpenNettyAddress address, CancellationToken cancellationToken = default)
            => new CoordinatorAsyncList<OpenNettyEndpoint>(
                [.. Endpoints.Where(endpoint => endpoint.Gateway == gateway && endpoint.Address == address)], AddressLookupFailure);

        public override IAsyncEnumerable<OpenNettyEndpoint> FindEndpointsByDeviceAsync(
            OpenNettyDevice device, CancellationToken cancellationToken = default)
            => new CoordinatorAsyncList<OpenNettyEndpoint>([.. Endpoints.Where(endpoint => endpoint.Unit?.Device == device)], DeviceLookupFailure);

        public override IAsyncEnumerable<OpenNettyEndpoint> FindEndpointsByGatewayAsync(
            OpenNettyGateway gateway, CancellationToken cancellationToken = default)
            => new CoordinatorAsyncList<OpenNettyEndpoint>([.. Endpoints.Where(endpoint => endpoint.Gateway == gateway)], GatewayLookupFailure);
    }

    private sealed class CoordinatorAsyncList<T>(IReadOnlyList<T> items, Exception? failure = null) : IAsyncEnumerable<T>
    {
        public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            if (failure is not null)
            {
                throw failure;
            }

            foreach (var item in items)
            {
                yield return item;
            }

            await Task.CompletedTask;
        }
    }

    private sealed class CoordinatorPipeline : IOpenNettyPipeline
    {
        private readonly Lock _lock = new();
        private readonly List<IAsyncObserver<OpenNettyNotification>> _observers = [];
        private readonly Channel<int> _subscriptions = Channel.CreateUnbounded<int>();

        public int ObserverCount
        {
            get { lock (_lock) { return _observers.Count; } }
        }

        public ValueTask<IAsyncDisposable> ConnectAsync() => new(new CoordinatorSubscription(() => { }));

        public async ValueTask PublishAsync(OpenNettyNotification notification, CancellationToken cancellationToken = default)
        {
            IAsyncObserver<OpenNettyNotification>[] observers;

            lock (_lock)
            {
                observers = [.. _observers];
            }

            foreach (var observer in observers)
            {
                await observer.OnNextAsync(notification);
            }
        }

        public ValueTask<IAsyncDisposable> SubscribeAsync(IAsyncObserver<OpenNettyNotification> observer)
        {
            lock (_lock)
            {
                _observers.Add(observer);
            }

            _subscriptions.Writer.TryWrite(0);

            return new(new CoordinatorSubscription(() =>
            {
                lock (_lock)
                {
                    _observers.Remove(observer);
                }
            }));
        }

        public async Task WaitForSubscriptionAsync()
        {
            using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            source.CancelAfter(WaitTimeout);

            await _subscriptions.Reader.ReadAsync(source.Token);
        }

        public void DiscardSubscriptions()
        {
            while (_subscriptions.Reader.TryRead(out _))
            {
            }
        }
    }

    private sealed class CoordinatorSubscription(Action action) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            action();

            return ValueTask.CompletedTask;
        }
    }

    private sealed record class CoordinatorLogEntry(LogLevel Level, EventId EventId, Exception? Exception);

    private sealed class CoordinatorLogger : ILogger<OpenNettyCoordinator>
    {
        public CoordinatorRecorder<CoordinatorLogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Record(new CoordinatorLogEntry(logLevel, eventId, exception));
    }

    private sealed class CoordinatorRecorder<T>
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
