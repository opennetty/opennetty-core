/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.ComponentModel;
using System.Net;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyControllerTests
{
    private static readonly Dictionary<OpenNettyProtocol, OpenNettyGateway> _gateways = new()
    {
        [OpenNettyProtocol.Nitoo]  = CreateGateway("88213"),
        [OpenNettyProtocol.Scs]    = CreateGateway("03598"),
        [OpenNettyProtocol.Zigbee] = CreateGateway("88328")
    };

    private readonly ControllerServiceFake _service = new();
    private readonly OpenNettyManager _manager = Substitute.For<OpenNettyManager>(
        Substitute.For<IOptionsMonitor<OpenNettyOptions>>());

    private readonly OpenNettyController _controller;

    public OpenNettyControllerTests() => _controller = new OpenNettyController(_manager, _service);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Constructor_NullManager_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new OpenNettyController(null!, _service));

        Assert.Equal("manager", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullService_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new OpenNettyController(_manager, null!));

        Assert.Equal("service", exception.ParamName);
    }

    [Fact]
    public async Task AddMemoryDataAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var data = CreateMemoryData(OpenNettyMedium.Radio);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => _controller.AddMemoryDataAsync(null!, data, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task AddMemoryDataAsync_NullData_ThrowsArgumentNullException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.MemoryWriting);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => _controller.AddMemoryDataAsync(endpoint, null!, Token).AsTask());

        Assert.Equal("data", exception.ParamName);
    }

    [Fact]
    public async Task AddMemoryDataAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.MemoryReading);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.AddMemoryDataAsync(endpoint, CreateMemoryData(OpenNettyMedium.Radio), Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyMedium.Radio, "64")]
    [InlineData(OpenNettyMedium.Powerline, "96")]
    [InlineData(OpenNettyMedium.Infrared, "128")]
    public async Task AddMemoryDataAsync_KnownMedium_SetsMemoryWriteDimension(OpenNettyMedium medium, string expectedMedium)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.MemoryWriting);

        await _controller.AddMemoryDataAsync(endpoint, CreateMemoryData(medium), Token);

        AssertDimensionSet(endpoint, OpenNettyDimensions.Diagnostics.MemoryWrite, [expectedMedium, "19745", "7"]);
    }

    [Fact]
    public async Task AddMemoryDataAsync_UnsupportedMedium_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.MemoryWriting);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _controller.AddMemoryDataAsync(endpoint, CreateMemoryData(OpenNettyMedium.Bus), Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Fact]
    public async Task BindZigbeeEndpointAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => _controller.BindZigbeeEndpointAsync(null!, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    public static TheoryData<string> PilotWireModeOperations => ["ActivateShutdown", "CancelShutdown", "CancelDerogation"];

    [Theory]
    [MemberData(nameof(PilotWireModeOperations))]
    public async Task PilotWireOperation_NullEndpoint_ThrowsArgumentNullException(string operation)
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => ExecutePilotWireOperationAsync(operation, null!, null));

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(PilotWireModeOperations))]
    public async Task PilotWireOperation_EndpointWithoutCapability_ThrowsInvalidOperationException(string operation)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.PilotWireControl);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ExecutePilotWireOperationAsync(operation, endpoint, null));

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData("ActivateShutdown", false, true)]
    [InlineData("ActivateShutdown", true, false)]
    [InlineData("CancelShutdown", false, true)]
    [InlineData("CancelShutdown", true, false)]
    [InlineData("CancelDerogation", false, true)]
    [InlineData("CancelDerogation", true, false)]
    public async Task PilotWireOperation_SupportedEndpoint_SendsMatchingCommand(string operation, bool excludeAssociatedDevices, bool expectMulticast)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, GetPilotWireCapability(operation));
        var options = new OpenNettyModels.TemperatureControl.PilotWireTransmissionOptions { ExcludeAssociatedDevices = excludeAssociatedDevices };

        await ExecutePilotWireOperationAsync(operation, endpoint, options);

        AssertCommandExecuted(endpoint, operation switch
        {
            "ActivateShutdown" => OpenNettyCommands.TemperatureControl.WirePilotShutdownMode,
            "CancelShutdown"   => OpenNettyCommands.TemperatureControl.CancelWirePilotShutdownMode,
            _                  => OpenNettyCommands.TemperatureControl.CancelWirePilotDerogationMode
        }, expectMulticast ? OpenNettyMode.Multicast : null);
    }

    [Theory]
    [MemberData(nameof(PilotWireModeOperations))]
    public async Task PilotWireOperation_NoOptions_UsesMulticastMode(string operation)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, GetPilotWireCapability(operation));

        await ExecutePilotWireOperationAsync(operation, endpoint, null);

        Assert.Equal(OpenNettyMode.Multicast, AssertSingleCall(ServiceMethod.ExecuteCommand).Mode);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("12", 12)]
    [InlineData("255", 255)]
    public async Task CountZigbeeDevicesAsync_ReturnedValue_IsParsedAsByte(string value, byte expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.ZigbeeNetworkManagement);
        _service.DimensionValues[OpenNettyDimensions.Management.NumberOfProducts] = [value];

        var count = await _controller.CountZigbeeDevicesAsync(endpoint, Token);

        Assert.Equal(expected, count);
        var call = AssertSingleCall(ServiceMethod.GetDimension);
        Assert.Equal(OpenNettyDimensions.Management.NumberOfProducts, call.Dimension);
        AssertCommonArguments(call, endpoint);
    }

    [Theory]
    [InlineData("256")]
    [InlineData("-1")]
    public async Task CountZigbeeDevicesAsync_ValueOutOfByteRange_ThrowsOverflowException(string value)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.ZigbeeNetworkManagement);
        _service.DimensionValues[OpenNettyDimensions.Management.NumberOfProducts] = [value];

        await Assert.ThrowsAsync<OverflowException>(() => _controller.CountZigbeeDevicesAsync(endpoint, Token).AsTask());
    }

    [Fact]
    public async Task DispatchActionScenarioAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.DispatchActionScenarioAsync(null!, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task DispatchActionScenarioAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.ActionScenarioEvent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.DispatchActionScenarioAsync(endpoint, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyModels.ScenariosPlus.ActionScenarioType.Action)]
    [InlineData(OpenNettyModels.ScenariosPlus.ActionScenarioType.StopAction)]
    public async Task DispatchActionScenarioAsync_KnownType_SendsMatchingScenariosPlusCommand(OpenNettyModels.ScenariosPlus.ActionScenarioType type)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.ActionScenarioActivation);

        await _controller.DispatchActionScenarioAsync(endpoint, type, Token);

        AssertCommandExecuted(endpoint, type is OpenNettyModels.ScenariosPlus.ActionScenarioType.Action
            ? OpenNettyCommands.ScenariosPlus.Action
            : OpenNettyCommands.ScenariosPlus.StopAction);
    }

    [Fact]
    public async Task DispatchActionScenarioAsync_UndefinedType_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.ActionScenarioActivation);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _controller.DispatchActionScenarioAsync(endpoint, (OpenNettyModels.ScenariosPlus.ActionScenarioType) 42, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Fact]
    public async Task DispatchDimmingScenarioAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.DispatchDimmingScenarioAsync(null!, 10, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Theory]
    [InlineData(-100, "156")]
    [InlineData(-10, "246")]
    [InlineData(-1, "255")]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(100, "100")]
    public async Task DispatchDimmingScenarioAsync_ValidStep_BroadcastsDimmerStepDimension(short step, string expectedValue)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.DimmingScenarioActivation);

        await _controller.DispatchDimmingScenarioAsync(endpoint, step, Token);

        AssertDimensionSet(endpoint, OpenNettyDimensions.Lighting.DimmerStep, [expectedValue], OpenNettyMode.Broadcast);
    }

    [Theory]
    [InlineData(-101)]
    [InlineData(101)]
    public async Task DispatchDimmingScenarioAsync_StepOutOfRange_ThrowsArgumentOutOfRangeException(short step)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.DimmingScenarioActivation);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _controller.DispatchDimmingScenarioAsync(endpoint, step, Token).AsTask());

        Assert.Equal("step", exception.ParamName);
        Assert.Empty(_service.Calls);
    }

    [Fact]
    public async Task DispatchDimmingScenarioAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.DimmingScenarioEvent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.DispatchDimmingScenarioAsync(endpoint, 10, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public async Task DispatchDryContactScenarioAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.DispatchDryContactScenarioAsync(null!, OpenNettyModels.ScenariosPlus.DryContactScenarioType.Open, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task DispatchDryContactScenarioAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.DryContactScenarioEvent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.DispatchDryContactScenarioAsync(endpoint, OpenNettyModels.ScenariosPlus.DryContactScenarioType.Open, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyModels.ScenariosPlus.DryContactScenarioType.Open)]
    [InlineData(OpenNettyModels.ScenariosPlus.DryContactScenarioType.Closed)]
    public async Task DispatchDryContactScenarioAsync_KnownType_SendsDryContactCommandWithScenarioParameter(OpenNettyModels.ScenariosPlus.DryContactScenarioType type)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.DryContactScenarioActivation);

        await _controller.DispatchDryContactScenarioAsync(endpoint, type, Token);

        AssertCommandExecuted(endpoint, (type is OpenNettyModels.ScenariosPlus.DryContactScenarioType.Open
            ? OpenNettyCommands.ScenariosPlus.DryContactOn
            : OpenNettyCommands.ScenariosPlus.DryContactOff).WithParameters(["1"]));
    }

    [Fact]
    public async Task DispatchDryContactScenarioAsync_UndefinedType_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.DryContactScenarioActivation);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _controller.DispatchDryContactScenarioAsync(endpoint, (OpenNettyModels.ScenariosPlus.DryContactScenarioType) 42, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public async Task DispatchOnOffScenarioAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.DispatchOnOffScenarioAsync(null!, OpenNettyModels.Lighting.OnOffScenarioType.On, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task DispatchOnOffScenarioAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.OnOffScenarioEvent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.DispatchOnOffScenarioAsync(endpoint, OpenNettyModels.Lighting.OnOffScenarioType.On, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyModels.Lighting.OnOffScenarioType.On)]
    [InlineData(OpenNettyModels.Lighting.OnOffScenarioType.Off)]
    public async Task DispatchOnOffScenarioAsync_KnownType_BroadcastsLightingCommand(OpenNettyModels.Lighting.OnOffScenarioType type)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.OnOffScenarioActivation);

        await _controller.DispatchOnOffScenarioAsync(endpoint, type, Token);

        AssertCommandExecuted(
            endpoint, type is OpenNettyModels.Lighting.OnOffScenarioType.On ? OpenNettyCommands.Lighting.On : OpenNettyCommands.Lighting.Off, OpenNettyMode.Broadcast);
    }

    [Fact]
    public async Task DispatchOnOffScenarioAsync_UndefinedType_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.OnOffScenarioActivation);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _controller.DispatchOnOffScenarioAsync(endpoint, (OpenNettyModels.Lighting.OnOffScenarioType) 42, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public async Task DispatchPressureScenarioAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.DispatchPressureScenarioAsync(null!, OpenNettyModels.Scenarios.PressureScenarioType.Pressure, 1, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task DispatchPressureScenarioAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.PressureScenarioEvent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.DispatchPressureScenarioAsync(endpoint, OpenNettyModels.Scenarios.PressureScenarioType.Pressure, 1, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyModels.Scenarios.PressureScenarioType.Pressure, 1, "1", null)]
    [InlineData(OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterShortPressure, 12, "12", "1")]
    [InlineData(OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterExtendedPressure, 0, "0", "2")]
    [InlineData(OpenNettyModels.Scenarios.PressureScenarioType.ExtendedPressure, 255, "255", "3")]
    public async Task DispatchPressureScenarioAsync_KnownType_SendsScenarioCommandForButton(
        OpenNettyModels.Scenarios.PressureScenarioType type, byte button, string expectedValue, string? expectedParameter)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.PressureScenarioActivation);

        await _controller.DispatchPressureScenarioAsync(endpoint, type, button, Token);

        var expected = expectedParameter is null
            ? new OpenNettyCommand(OpenNettyCategories.Scenarios, expectedValue)
            : new OpenNettyCommand(OpenNettyCategories.Scenarios, expectedValue, [expectedParameter]);
        AssertCommandExecuted(endpoint, expected);
    }

    [Fact]
    public async Task DispatchPressureScenarioAsync_UndefinedType_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.PressureScenarioActivation);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _controller.DispatchPressureScenarioAsync(endpoint, (OpenNettyModels.Scenarios.PressureScenarioType) 42, 1, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public async Task DispatchPressureScenarioPlusAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.DispatchPressureScenarioPlusAsync(null!, OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure, cancellationToken: Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task DispatchPressureScenarioPlusAsync_MissingButtonOnConfigurableEndpoint_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(
            OpenNettyProtocol.Scs, OpenNettyCapabilities.PressureScenarioPlusActivation, OpenNettyCapabilities.ConfigurablePushButtonNumbers);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.DispatchPressureScenarioPlusAsync(endpoint, OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure, cancellationToken: Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0117), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Fact]
    public async Task DispatchPressureScenarioPlusAsync_ButtonOnNonConfigurableEndpoint_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.PressureScenarioPlusActivation);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.DispatchPressureScenarioPlusAsync(endpoint, OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure, 3, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0118), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchPressureScenarioPlusAsync_EndpointWithoutActivationCapability_ThrowsInvalidOperationException(bool configurable)
    {
        var endpoint = configurable
            ? CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.ConfigurablePushButtonNumbers)
            : CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.PressureScenarioPlusEvent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.DispatchPressureScenarioPlusAsync(
                endpoint, OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure, configurable ? (byte) 1 : null, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure)]
    [InlineData(OpenNettyModels.ScenariosPlus.PressureScenarioType.StartOfExtendedPressure)]
    [InlineData(OpenNettyModels.ScenariosPlus.PressureScenarioType.ExtendedPressure)]
    [InlineData(OpenNettyModels.ScenariosPlus.PressureScenarioType.EndOfExtendedPressure)]
    public async Task DispatchPressureScenarioPlusAsync_FixedButtonEndpoint_SendsCommandWithoutParameters(OpenNettyModels.ScenariosPlus.PressureScenarioType type)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.PressureScenarioPlusActivation);

        await _controller.DispatchPressureScenarioPlusAsync(endpoint, type, cancellationToken: Token);

        AssertCommandExecuted(endpoint, GetPressureScenarioPlusCommand(type));
    }

    [Theory]
    [InlineData(OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure, 1)]
    [InlineData(OpenNettyModels.ScenariosPlus.PressureScenarioType.StartOfExtendedPressure, 7)]
    [InlineData(OpenNettyModels.ScenariosPlus.PressureScenarioType.ExtendedPressure, 20)]
    [InlineData(OpenNettyModels.ScenariosPlus.PressureScenarioType.EndOfExtendedPressure, 255)]
    public async Task DispatchPressureScenarioPlusAsync_ConfigurableEndpoint_SendsCommandWithButtonParameter(OpenNettyModels.ScenariosPlus.PressureScenarioType type, byte button)
    {
        var endpoint = CreateEndpoint(
            OpenNettyProtocol.Scs, OpenNettyCapabilities.PressureScenarioPlusActivation, OpenNettyCapabilities.ConfigurablePushButtonNumbers);

        await _controller.DispatchPressureScenarioPlusAsync(endpoint, type, button, Token);

        AssertCommandExecuted(
            endpoint, GetPressureScenarioPlusCommand(type).WithParameters([button.ToString(System.Globalization.CultureInfo.InvariantCulture)]));
    }

    [Fact]
    public async Task DispatchPressureScenarioPlusAsync_UndefinedType_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.PressureScenarioPlusActivation);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _controller.DispatchPressureScenarioPlusAsync(endpoint, (OpenNettyModels.ScenariosPlus.PressureScenarioType) 42, cancellationToken: Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public async Task DispatchProgressiveScenarioAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.DispatchProgressiveScenarioAsync(null!, TimeSpan.FromSeconds(1), Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task DispatchProgressiveScenarioAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.ProgressiveScenarioEvent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.DispatchProgressiveScenarioAsync(endpoint, TimeSpan.FromSeconds(1), Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(100, "1")]
    [InlineData(500, "3")]
    [InlineData(1000, "5")]
    [InlineData(2100, "11")]
    [InlineData(60000, "300")]
    public async Task DispatchProgressiveScenarioAsync_Duration_IsConvertedToFifthsOfSecond(int milliseconds, string expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.ProgressiveScenarioActivation);

        await _controller.DispatchProgressiveScenarioAsync(endpoint, TimeSpan.FromMilliseconds(milliseconds), Token);

        AssertCommandExecuted(endpoint, OpenNettyCommands.ScenariosPlus.ActionInTime.WithParameters([expected]));
    }

    [Fact]
    public async Task DispatchStopUpDownScenarioAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.DispatchStopUpDownScenarioAsync(null!, OpenNettyModels.Automation.StopUpDownScenarioType.Stop, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task DispatchStopUpDownScenarioAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.StopUpDownScenarioEvent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.DispatchStopUpDownScenarioAsync(endpoint, OpenNettyModels.Automation.StopUpDownScenarioType.Stop, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyModels.Automation.StopUpDownScenarioType.Stop)]
    [InlineData(OpenNettyModels.Automation.StopUpDownScenarioType.Up)]
    [InlineData(OpenNettyModels.Automation.StopUpDownScenarioType.Down)]
    public async Task DispatchStopUpDownScenarioAsync_KnownType_BroadcastsMatchingAutomationCommand(OpenNettyModels.Automation.StopUpDownScenarioType type)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.StopUpDownScenarioActivation);

        await _controller.DispatchStopUpDownScenarioAsync(endpoint, type, Token);

        AssertCommandExecuted(endpoint, type switch
        {
            OpenNettyModels.Automation.StopUpDownScenarioType.Up   => OpenNettyCommands.Automation.Up,
            OpenNettyModels.Automation.StopUpDownScenarioType.Down => OpenNettyCommands.Automation.Down,
            _                           => OpenNettyCommands.Automation.Stop
        }, OpenNettyMode.Broadcast);
    }

    [Fact]
    public async Task DispatchStopUpDownScenarioAsync_UndefinedType_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.StopUpDownScenarioActivation);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _controller.DispatchStopUpDownScenarioAsync(endpoint, (OpenNettyModels.Automation.StopUpDownScenarioType) 42, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Fact]
    public async Task DispatchTimedScenarioAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.DispatchTimedScenarioAsync(null!, TimeSpan.FromSeconds(1), Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task DispatchTimedScenarioAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.TimedScenarioEvent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.DispatchTimedScenarioAsync(endpoint, TimeSpan.FromSeconds(1), Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(100, "1")]
    [InlineData(500, "3")]
    [InlineData(1000, "5")]
    [InlineData(2100, "11")]
    [InlineData(60000, "300")]
    public async Task DispatchTimedScenarioAsync_Duration_IsConvertedToFifthsOfSecond(int milliseconds, string expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.TimedScenarioActivation);

        await _controller.DispatchTimedScenarioAsync(endpoint, TimeSpan.FromMilliseconds(milliseconds), Token);

        AssertCommandExecuted(endpoint, OpenNettyCommands.ScenariosPlus.ActionForTime.WithParameters([expected]));
    }

    [Fact]
    public void EnumerateBrightnessAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => _controller.EnumerateBrightnessAsync(null!, Token));

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public void EnumerateBrightnessAsync_EndpointWithoutDimmingStateCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicDimmingControl);

        var exception = Assert.Throws<InvalidOperationException>(() => _controller.EnumerateBrightnessAsync(endpoint, Token));

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public void EnumerateBrightnessAsync_UnsupportedFunctionType_ThrowsInvalidOperationException()
    {
        var endpoint = WithSetting(
            CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicDimmingState),
            OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.ContactState);

        var exception = Assert.Throws<InvalidOperationException>(() => _controller.EnumerateBrightnessAsync(endpoint, Token));

        Assert.Equal(SR.GetResourceString(SR.ID0099), exception.Message);
    }

    [Fact]
    public async Task EnumerateBrightnessAsync_AreaAddressWithoutGateway_ThrowsWhenEnumerated()
    {
        var area = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, null);
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, area, OpenNettyCapabilities.BasicDimmingState) with { Gateway = null };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ToListAsync(_controller.EnumerateBrightnessAsync(endpoint, Token)));

        Assert.Equal(SR.FormatID2023(endpoint.Name), exception.Message);
    }

    [Fact]
    public async Task EnumerateBrightnessAsync_NitooEndpoint_YieldsBrightnessOfEndpoint()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.AdvancedDimmingState, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = ["143", "42"];

        var levels = await ToListAsync(_controller.EnumerateBrightnessAsync(endpoint, Token));

        var (resolved, level) = Assert.Single(levels);
        Assert.Same(endpoint, resolved);
        Assert.Equal(42, level);
    }

    [Fact]
    public async Task EnumerateBrightnessAsync_ZigbeeUnitAddress_YieldsBrightnessOfEndpoint()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.AdvancedDimmingState);
        _service.ObservedMessages.Add(CreateDimensionRead(endpoint, OpenNettyDimensions.Lighting.DimmerLevelSpeed, "170", "0"));

        var levels = await ToListAsync(_controller.EnumerateBrightnessAsync(endpoint, Token));

        var (resolved, level) = Assert.Single(levels);
        Assert.Same(endpoint, resolved);
        Assert.Equal(70, level);
    }

    [Fact]
    public async Task EnumerateBrightnessAsync_ScsPointToPointAddress_YieldsBrightnessOfEndpoint()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicDimmingState);
        _service.StatusReply = OpenNettyCommands.Lighting.On30;

        var levels = await ToListAsync(_controller.EnumerateBrightnessAsync(endpoint, Token));

        var (resolved, level) = Assert.Single(levels);
        Assert.Same(endpoint, resolved);
        Assert.Equal(30, level);
    }

    [Fact]
    public async Task EnumerateBrightnessAsync_ScsAreaAddress_CollectsAdvancedThenBasicStatesWithoutDuplicates()
    {
        var area = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, null);
        var addresses = new[] { 1, 2, 3 }.Select(point => OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, (byte) point)).ToArray();
        var source = CreateEndpoint(
            OpenNettyProtocol.Scs, area, OpenNettyCapabilities.AdvancedDimmingState, OpenNettyCapabilities.BasicDimmingState);
        var advanced = CreateEndpoint(OpenNettyProtocol.Scs, addresses[0], OpenNettyCapabilities.AdvancedDimmingState, OpenNettyCapabilities.BasicDimmingState)
            with { Name = "Advanced" };
        var basic = CreateEndpoint(OpenNettyProtocol.Scs, addresses[1], OpenNettyCapabilities.BasicDimmingState) with { Name = "Basic" };
        var unsupported = CreateEndpoint(OpenNettyProtocol.Scs, addresses[2], OpenNettyCapabilities.OnOffSwitchState) with { Name = "Unsupported" };
        _service.ObservedMessages.AddRange(
        [
            OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, OpenNettyDimensions.Lighting.DimmerLevelSpeed, ["175", "0"], addresses[0]),
            OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, OpenNettyDimensions.Lighting.DimmerStatus, ["175", "0"], addresses[0]),
            OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, OpenNettyDimensions.Lighting.DimmerLevelSpeed, ["120", "0"], addresses[2]),
            CreateAcknowledgement(OpenNettyProtocol.Scs),
            OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, OpenNettyDimensions.Lighting.DimmerLevelSpeed, ["199", "0"], addresses[1])
        ]);
        _service.StatusEnumeration.AddRange(
        [
            (addresses[0], OpenNettyCommands.Lighting.Off),
            (addresses[1], OpenNettyCommands.Lighting.On60),
            (addresses[2], OpenNettyCommands.Lighting.On)
        ]);
        SetEndpointsAtAddress(source, addresses[0], advanced);
        SetEndpointsAtAddress(source, addresses[1], basic);
        SetEndpointsAtAddress(source, addresses[2], unsupported);

        var levels = await ToListAsync(_controller.EnumerateBrightnessAsync(source, Token));

        Assert.Equal([(advanced, (byte) 75), (basic, (byte) 60)], levels);
        Assert.Equal([ServiceMethod.ObserveMessages, ServiceMethod.EnumerateStatuses], _service.Calls.Select(static call => call.Method));
    }

    [Fact]
    public async Task EnumerateBrightnessAsync_ScsAreaAddressWithBasicStateOnly_DoesNotObserveDimensions()
    {
        var area = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, null);
        var point = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, 1);
        var source = CreateEndpoint(OpenNettyProtocol.Scs, area, OpenNettyCapabilities.BasicDimmingState);
        var light = CreateEndpoint(OpenNettyProtocol.Scs, point, OpenNettyCapabilities.BasicDimmingState) with { Name = "Light" };
        _service.StatusEnumeration.Add((point, OpenNettyCommands.Lighting.On90));
        SetEndpointsAtAddress(source, point, light);

        var levels = await ToListAsync(_controller.EnumerateBrightnessAsync(source, Token));

        Assert.Equal([(light, (byte) 90)], levels);
        var call = AssertSingleCall(ServiceMethod.EnumerateStatuses);
        Assert.Equal(OpenNettyCategories.Lighting, call.Category!.Value);
        AssertCommonArguments(call, source);
    }

    [Fact]
    public async Task EnumerateBrightnessAsync_ScsAreaAddress_StatusFilterAcceptsLightingLevelCommandsOnly()
    {
        var area = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, null);
        var source = CreateEndpoint(OpenNettyProtocol.Scs, area, OpenNettyCapabilities.BasicDimmingState);
        await ToListAsync(_controller.EnumerateBrightnessAsync(source, Token));
        var call = AssertSingleCall(ServiceMethod.EnumerateStatuses);

        Assert.True(await IsAcceptedAsync(call, OpenNettyCommands.Lighting.On20));
        Assert.True(await IsAcceptedAsync(call, OpenNettyCommands.Lighting.Off));
        Assert.False(await IsAcceptedAsync(call, OpenNettyCommands.Lighting.Toggle));
    }

    [Fact]
    public void EnumerateShutterPositionsAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => _controller.EnumerateShutterPositionsAsync(null!, Token));

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public void EnumerateShutterPositionsAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicShutterState);

        var exception = Assert.Throws<InvalidOperationException>(() => _controller.EnumerateShutterPositionsAsync(endpoint, Token));

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public void EnumerateShutterPositionsAsync_UnsupportedFunctionType_ThrowsInvalidOperationException()
    {
        var endpoint = WithSetting(
            CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.AdvancedShutterState),
            OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.LightActuator);

        var exception = Assert.Throws<InvalidOperationException>(() => _controller.EnumerateShutterPositionsAsync(endpoint, Token));

        Assert.Equal(SR.GetResourceString(SR.ID0099), exception.Message);
    }

    [Fact]
    public void EnumerateShutterPositionsAsync_EndpointWithoutGateway_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.AdvancedShutterState) with { Gateway = null };

        var exception = Assert.Throws<InvalidOperationException>(() => _controller.EnumerateShutterPositionsAsync(endpoint, Token));

        Assert.Equal(SR.FormatID2023(endpoint.Name), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task EnumerateShutterPositionsAsync_SingleDeviceAddress_YieldsPositionOfEndpoint(OpenNettyProtocol protocol)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.AdvancedShutterState);
        _service.DimensionValues[OpenNettyDimensions.Automation.ShutterStatus] = ["10", "35"];

        var positions = await ToListAsync(_controller.EnumerateShutterPositionsAsync(endpoint, Token));

        var (resolved, position) = Assert.Single(positions);
        Assert.Same(endpoint, resolved);
        Assert.Equal((byte) 35, position);
    }

    [Fact]
    public async Task EnumerateShutterPositionsAsync_AreaAddress_YieldsPositionsOfAdvancedEndpoints()
    {
        var area = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, null);
        var first = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, 1);
        var second = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, 2);
        var source = CreateEndpoint(OpenNettyProtocol.Scs, area, OpenNettyCapabilities.AdvancedShutterState);
        var open = CreateEndpoint(OpenNettyProtocol.Scs, first, OpenNettyCapabilities.AdvancedShutterState) with { Name = "Open" };
        var unknown = CreateEndpoint(OpenNettyProtocol.Scs, second, OpenNettyCapabilities.AdvancedShutterState) with { Name = "Unknown" };
        var basic = CreateEndpoint(OpenNettyProtocol.Scs, second, OpenNettyCapabilities.BasicShutterState) with { Name = "Basic" };
        _service.DimensionEnumeration.AddRange([(first, ["10", "80"]), (second, ["10", "255"])]);
        SetEndpointsAtAddress(source, first, open);
        SetEndpointsAtAddress(source, second, unknown, basic);

        var positions = await ToListAsync(_controller.EnumerateShutterPositionsAsync(source, Token));

        Assert.Equal([(open, (byte?) 80), (unknown, (byte?) null)], positions);
        var call = AssertSingleCall(ServiceMethod.EnumerateDimensions);
        Assert.Equal(OpenNettyDimensions.Automation.ShutterStatus, call.Dimension);
        Assert.Equal(source.Protocol, call.Protocol);
        Assert.Same(source.Gateway, call.Gateway);
        Assert.Equal(GetDefaultOptions(source), call.Options);
        Assert.Null(call.Address);
    }

    [Fact]
    public async Task EnumerateShutterPositionsAsync_AreaAddressWithInvalidPosition_ThrowsInvalidDataException()
    {
        var area = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, null);
        var point = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, 1);
        var source = CreateEndpoint(OpenNettyProtocol.Scs, area, OpenNettyCapabilities.AdvancedShutterState);
        _service.DimensionEnumeration.Add((point, ["10", "150"]));
        SetEndpointsAtAddress(source, point, source);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => ToListAsync(_controller.EnumerateShutterPositionsAsync(source, Token)));

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public void EnumerateShutterStatesAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => _controller.EnumerateShutterStatesAsync(null!, Token));

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public void EnumerateShutterStatesAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicShutterControl);

        var exception = Assert.Throws<InvalidOperationException>(() => _controller.EnumerateShutterStatesAsync(endpoint, Token));

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public void EnumerateShutterStatesAsync_UnsupportedFunctionType_ThrowsInvalidOperationException()
    {
        var endpoint = WithSetting(
            CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicShutterState),
            OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.LightActuator);

        var exception = Assert.Throws<InvalidOperationException>(() => _controller.EnumerateShutterStatesAsync(endpoint, Token));

        Assert.Equal(SR.GetResourceString(SR.ID0099), exception.Message);
    }

    [Fact]
    public void EnumerateShutterStatesAsync_EndpointWithoutGateway_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicShutterState) with { Gateway = null };

        var exception = Assert.Throws<InvalidOperationException>(() => _controller.EnumerateShutterStatesAsync(endpoint, Token));

        Assert.Equal(SR.FormatID2023(endpoint.Name), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task EnumerateShutterStatesAsync_SingleDeviceAddress_YieldsStateOfEndpoint(OpenNettyProtocol protocol)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.AdvancedShutterState, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Automation.ShutterStatus] = ["12", "0"];
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = ["139", "0"];

        var states = await ToListAsync(_controller.EnumerateShutterStatesAsync(endpoint, Token));

        var (resolved, state) = Assert.Single(states);
        Assert.Same(endpoint, resolved);
        Assert.Equal(OpenNettyModels.Automation.ShutterState.Closing, state);
    }

    [Fact]
    public async Task EnumerateShutterStatesAsync_AreaAddress_CollectsAdvancedThenBasicStatesWithoutDuplicates()
    {
        var area = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, null);
        var addresses = new[] { 1, 2, 3 }.Select(point => OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, (byte) point)).ToArray();
        var source = CreateEndpoint(OpenNettyProtocol.Scs, area, OpenNettyCapabilities.AdvancedShutterState, OpenNettyCapabilities.BasicShutterState);
        var advanced = CreateEndpoint(OpenNettyProtocol.Scs, addresses[0], OpenNettyCapabilities.AdvancedShutterState, OpenNettyCapabilities.BasicShutterState)
            with { Name = "Advanced" };
        var basic = CreateEndpoint(OpenNettyProtocol.Scs, addresses[1], OpenNettyCapabilities.BasicShutterState) with { Name = "Basic" };
        var moving = CreateEndpoint(OpenNettyProtocol.Scs, addresses[2], OpenNettyCapabilities.BasicShutterState) with { Name = "Moving" };
        _service.DimensionEnumeration.AddRange(
        [
            (addresses[0], ["10", "0"]),
            (addresses[1], ["11", "0"])
        ]);
        _service.StatusEnumeration.AddRange(
        [
            (addresses[0], OpenNettyCommands.Automation.Up),
            (addresses[1], OpenNettyCommands.Automation.Stop),
            (addresses[2], OpenNettyCommands.Automation.Down)
        ]);
        SetEndpointsAtAddress(source, addresses[0], advanced);
        SetEndpointsAtAddress(source, addresses[1], basic);
        SetEndpointsAtAddress(source, addresses[2], moving);

        var states = await ToListAsync(_controller.EnumerateShutterStatesAsync(source, Token));

        Assert.Equal(
            [(advanced, OpenNettyModels.Automation.ShutterState.Closed), (basic, OpenNettyModels.Automation.ShutterState.Stopped), (moving, OpenNettyModels.Automation.ShutterState.Closing)],
            states);
        Assert.Equal([ServiceMethod.EnumerateDimensions, ServiceMethod.EnumerateStatuses], _service.Calls.Select(static call => call.Method));
    }

    [Theory]
    [InlineData("10", "0", OpenNettyModels.Automation.ShutterState.Closed)]
    [InlineData("10", "75", OpenNettyModels.Automation.ShutterState.Open)]
    [InlineData("10", "255", OpenNettyModels.Automation.ShutterState.Stopped)]
    [InlineData("11", "0", OpenNettyModels.Automation.ShutterState.Opening)]
    [InlineData("13", "0", OpenNettyModels.Automation.ShutterState.Opening)]
    [InlineData("12", "0", OpenNettyModels.Automation.ShutterState.Closing)]
    [InlineData("14", "0", OpenNettyModels.Automation.ShutterState.Closing)]
    public async Task EnumerateShutterStatesAsync_AreaAddress_ParsesAdvancedShutterStatuses(string status, string position, OpenNettyModels.Automation.ShutterState expected)
    {
        var area = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, null);
        var point = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, 1);
        var source = CreateEndpoint(OpenNettyProtocol.Scs, area, OpenNettyCapabilities.AdvancedShutterState);
        var shutter = CreateEndpoint(OpenNettyProtocol.Scs, point, OpenNettyCapabilities.AdvancedShutterState) with { Name = "Shutter" };
        _service.DimensionEnumeration.Add((point, [status, position]));
        SetEndpointsAtAddress(source, point, shutter);

        var states = await ToListAsync(_controller.EnumerateShutterStatesAsync(source, Token));

        Assert.Equal([(shutter, expected)], states);
    }

    [Theory]
    [InlineData("10", "150")]
    [InlineData("99", "0")]
    public async Task EnumerateShutterStatesAsync_AreaAddressWithUnexpectedValues_ThrowsInvalidDataException(string status, string position)
    {
        var area = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, null);
        var point = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, 1);
        var source = CreateEndpoint(OpenNettyProtocol.Scs, area, OpenNettyCapabilities.AdvancedShutterState);
        _service.DimensionEnumeration.Add((point, [status, position]));
        SetEndpointsAtAddress(source, point, source);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => ToListAsync(_controller.EnumerateShutterStatesAsync(source, Token)));

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public async Task EnumerateShutterStatesAsync_AreaAddressWithBasicStateOnly_EnumeratesAutomationStatuses()
    {
        var area = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, null);
        var source = CreateEndpoint(OpenNettyProtocol.Scs, area, OpenNettyCapabilities.BasicShutterState);

        await ToListAsync(_controller.EnumerateShutterStatesAsync(source, Token));

        var call = AssertSingleCall(ServiceMethod.EnumerateStatuses);
        Assert.Equal(OpenNettyCategories.Automation, call.Category!.Value);
        AssertCommonArguments(call, source);
        Assert.True(await IsAcceptedAsync(call, OpenNettyCommands.Automation.Stop));
        Assert.True(await IsAcceptedAsync(call, OpenNettyCommands.Automation.Up));
        Assert.True(await IsAcceptedAsync(call, OpenNettyCommands.Automation.Down));
        Assert.False(await IsAcceptedAsync(call, OpenNettyCommands.Lighting.On));
    }

    [Fact]
    public void EnumerateSwitchStatesAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => _controller.EnumerateSwitchStatesAsync(null!, Token));

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public void EnumerateSwitchStatesAsync_EndpointWithoutStateCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchControl);

        var exception = Assert.Throws<InvalidOperationException>(() => _controller.EnumerateSwitchStatesAsync(endpoint, Token));

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public void EnumerateSwitchStatesAsync_UnsupportedFunctionType_ThrowsInvalidOperationException()
    {
        var endpoint = WithSetting(
            CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchState),
            OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.ContactState);

        var exception = Assert.Throws<InvalidOperationException>(() => _controller.EnumerateSwitchStatesAsync(endpoint, Token));

        Assert.Equal(SR.GetResourceString(SR.ID0099), exception.Message);
    }

    [Fact]
    public void EnumerateSwitchStatesAsync_EndpointWithoutGateway_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchState) with { Gateway = null };

        var exception = Assert.Throws<InvalidOperationException>(() => _controller.EnumerateSwitchStatesAsync(endpoint, Token));

        Assert.Equal(SR.FormatID2023(endpoint.Name), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task EnumerateSwitchStatesAsync_SingleDeviceAddress_YieldsSwitchStateOfEndpoint(OpenNettyProtocol protocol)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.UnitDescription);
        _service.StatusReply = OpenNettyCommands.Lighting.On;
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = ["129", "128"];

        var states = await ToListAsync(_controller.EnumerateSwitchStatesAsync(endpoint, Token));

        var (resolved, state) = Assert.Single(states);
        Assert.Same(endpoint, resolved);
        Assert.Equal(OpenNettyModels.Lighting.SwitchState.On, state);
    }

    [Fact]
    public async Task EnumerateSwitchStatesAsync_ScsAreaAddress_YieldsStatesOfAllEndpointsSupportingSwitchState()
    {
        var area = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, null);
        var first = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, 1);
        var second = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, 2);
        var source = CreateEndpoint(OpenNettyProtocol.Scs, area, OpenNettyCapabilities.OnOffSwitchState);
        var lightOn = CreateEndpoint(OpenNettyProtocol.Scs, first, OpenNettyCapabilities.OnOffSwitchState) with { Name = "On light" };
        var lightOff = CreateEndpoint(OpenNettyProtocol.Scs, second, OpenNettyCapabilities.OnOffSwitchState) with { Name = "Off light" };
        var withoutState = CreateEndpoint(OpenNettyProtocol.Scs, second, OpenNettyCapabilities.OnOffSwitchControl) with { Name = "No state" };
        _service.StatusEnumeration.AddRange([(first, OpenNettyCommands.Lighting.On50), (second, OpenNettyCommands.Lighting.Off)]);
        SetEndpointsAtAddress(source, first, lightOn);
        SetEndpointsAtAddress(source, second, lightOff, withoutState);

        var states = await ToListAsync(_controller.EnumerateSwitchStatesAsync(source, Token));

        Assert.Equal([(lightOn, OpenNettyModels.Lighting.SwitchState.On), (lightOff, OpenNettyModels.Lighting.SwitchState.Off)], states);
        var call = AssertSingleCall(ServiceMethod.EnumerateStatuses);
        Assert.Equal(OpenNettyCategories.Lighting, call.Category!.Value);
        AssertCommonArguments(call, source);
    }

    [Fact]
    public async Task EnumerateSwitchStatesAsync_ScsGeneralAddress_FilterAcceptsLightingLevelCommandsOnly()
    {
        var source = CreateEndpoint(
            OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(0, true, null, null, null), OpenNettyCapabilities.OnOffSwitchState);
        await ToListAsync(_controller.EnumerateSwitchStatesAsync(source, Token));
        var call = AssertSingleCall(ServiceMethod.EnumerateStatuses);

        Assert.True(await IsAcceptedAsync(call, OpenNettyCommands.Lighting.Off));
        Assert.True(await IsAcceptedAsync(call, OpenNettyCommands.Lighting.On100));
        Assert.False(await IsAcceptedAsync(call, OpenNettyCommands.Lighting.Toggle));
    }

    [Theory]
    [InlineData(0u, 1)]
    [InlineData(0u, 0)]
    [InlineData(4000000u, 0)]
    public async Task EnumerateSwitchStatesAsync_ZigbeeNonUnitSpecificAddress_EnumeratesLightingStatuses(uint identifier, int unit)
    {
        var address = OpenNettyAddress.FromDecimalZigbeeAddress(identifier, (byte) unit);
        var source = CreateEndpoint(OpenNettyProtocol.Zigbee, address, OpenNettyCapabilities.OnOffSwitchState);
        var light = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.OnOffSwitchState) with { Name = "Light" };
        _service.StatusEnumeration.Add((light.Address!.Value, OpenNettyCommands.Lighting.On));
        SetEndpointsAtAddress(source, light.Address.Value, light);

        var states = await ToListAsync(_controller.EnumerateSwitchStatesAsync(source, Token));

        Assert.Equal([(light, OpenNettyModels.Lighting.SwitchState.On)], states);
        var call = AssertSingleCall(ServiceMethod.EnumerateStatuses);
        AssertCommonArguments(call, source);
    }

    [Fact]
    public async Task EnumerateSwitchStatesAsync_ScsGroupAddress_EnumeratesLightingStatuses()
    {
        var group = OpenNettyAddress.FromScsLightPointAddress(0, false, 3, null, null);
        var source = CreateEndpoint(OpenNettyProtocol.Scs, group, OpenNettyCapabilities.OnOffSwitchState);

        var states = await ToListAsync(_controller.EnumerateSwitchStatesAsync(source, Token));

        Assert.Empty(states);
        AssertSingleCall(ServiceMethod.EnumerateStatuses);
    }

    [Fact]
    public async Task GetBrightnessAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.GetBrightnessAsync(null!, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task GetBrightnessAsync_EndpointWithoutDimmingStateCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.AdvancedDimmingControl);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetBrightnessAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public async Task GetBrightnessAsync_UnsupportedFunctionType_ThrowsInvalidOperationException()
    {
        var endpoint = WithSetting(
            CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicDimmingState),
            OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.AutomationActuator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetBrightnessAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0099), exception.Message);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("50", 50)]
    [InlineData("100", 100)]
    [InlineData("12.4", 12)]
    [InlineData("12.5", 12)]
    [InlineData("13.5", 14)]
    [InlineData("99.6", 100)]
    public async Task GetBrightnessAsync_NitooEndpoint_ReadsRoundedLevelFromUnitDescription(string value, byte expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.AdvancedDimmingState, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = ["143", value];

        var level = await _controller.GetBrightnessAsync(endpoint, Token);

        Assert.Equal(expected, level);
    }

    [Fact]
    public async Task GetBrightnessAsync_NitooEndpointWithUnexpectedFunctionCode_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.AdvancedDimmingState, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = ["129", "128"];

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => _controller.GetBrightnessAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public async Task GetBrightnessAsync_NitooEndpointWithEmptyUnitDescriptionValue_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.AdvancedDimmingState, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = ["143", ""];

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetBrightnessAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task GetBrightnessAsync_AdvancedState_ObservesDimensionRequestAndReturnsLevel(OpenNettyProtocol protocol)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.AdvancedDimmingState);
        _service.ObservedMessages.Add(CreateDimensionRead(endpoint, OpenNettyDimensions.Lighting.DimmerLevelSpeed, "165", "0"));

        var level = await _controller.GetBrightnessAsync(endpoint, Token);

        Assert.Equal(65, level);
        var call = AssertSingleCall(ServiceMethod.ObserveMessages);
        var expectedRequest = OpenNettyMessage.CreateDimensionRequest(
            protocol, OpenNettyDimensions.Lighting.DimmerLevelSpeed, endpoint.Address, endpoint.Medium);
        Assert.Equal(expectedRequest, call.Message);
        Assert.Same(endpoint.Gateway, call.Gateway);
        Assert.Equal(GetDefaultOptions(endpoint), call.Options);
        Assert.Equal(Token, call.CancellationToken);
    }

    [Fact]
    public async Task GetBrightnessAsync_AdvancedState_AcceptsDimmerStatusReplies()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.AdvancedDimmingState);
        _service.ObservedMessages.Add(CreateDimensionRead(endpoint, OpenNettyDimensions.Lighting.DimmerStatus, "180", "0"));

        var level = await _controller.GetBrightnessAsync(endpoint, Token);

        Assert.Equal(80, level);
    }

    [Fact]
    public async Task GetBrightnessAsync_AdvancedState_IgnoresRepliesFromOtherAddressesAndKeepsFirstMatchingReply()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.AdvancedDimmingState);
        var other = endpoint with { Address = OpenNettyAddress.FromDecimalZigbeeAddress(5000000, 1) };
        _service.ObservedMessages.AddRange(
        [
            CreateDimensionRead(other, OpenNettyDimensions.Lighting.DimmerLevelSpeed, "110", "0"),
            CreateDimensionRead(endpoint, OpenNettyDimensions.Lighting.DimmerLevelSpeed, "130", "0"),
            CreateDimensionRead(endpoint, OpenNettyDimensions.Lighting.DimmerLevelSpeed, "190", "0")
        ]);

        var level = await _controller.GetBrightnessAsync(endpoint, Token);

        Assert.Equal(30, level);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(20, 20)]
    [InlineData(30, 30)]
    [InlineData(40, 40)]
    [InlineData(50, 50)]
    [InlineData(60, 60)]
    [InlineData(70, 70)]
    [InlineData(80, 80)]
    [InlineData(90, 90)]
    [InlineData(100, 100)]
    public async Task GetBrightnessAsync_BasicState_MapsLightingStatusToLevel(int reply, byte expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicDimmingState);
        _service.StatusReply = GetLightingCommand(reply);

        var level = await _controller.GetBrightnessAsync(endpoint, Token);

        Assert.Equal(expected, level);
        var call = AssertSingleCall(ServiceMethod.GetStatus);
        Assert.Equal(OpenNettyCategories.Lighting, call.Category!.Value);
        AssertCommonArguments(call, endpoint);
    }

    [Fact]
    public async Task GetBrightnessAsync_BasicStateWithPlainOnReply_ReturnsFullBrightness()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicDimmingState);
        _service.StatusReply = OpenNettyCommands.Lighting.On;

        var level = await _controller.GetBrightnessAsync(endpoint, Token);

        Assert.Equal(100, level);
    }

    [Fact]
    public async Task GetBrightnessAsync_BasicStateWithUnexpectedReply_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicDimmingState);
        _service.StatusReply = OpenNettyCommands.Lighting.Toggle;

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => _controller.GetBrightnessAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public async Task GetBrightnessAsync_BasicState_FilterRejectsUnrelatedCommands()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicDimmingState);
        _service.StatusReply = OpenNettyCommands.Lighting.On;
        await _controller.GetBrightnessAsync(endpoint, Token);
        var call = AssertSingleCall(ServiceMethod.GetStatus);

        Assert.True(await IsAcceptedAsync(call, OpenNettyCommands.Lighting.On70));
        Assert.False(await IsAcceptedAsync(call, OpenNettyCommands.Lighting.Toggle));
    }

    [Theory]
    [InlineData("001", 1)]
    [InlineData("000", 0)]
    [InlineData("012", 12)]
    [InlineData("105", -5)]
    [InlineData("112", -12)]
    public async Task GetDateTimeAsync_DimensionValues_AreConvertedToDateTimeOffset(string zone, int expectedOffsetHours)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.DateTime);
        _service.DimensionValues[OpenNettyDimensions.Management.DateTime] = ["14", "30", "45", zone, "03", "10", "11", "2026"];

        var date = await _controller.GetDateTimeAsync(endpoint, Token);

        Assert.Equal(new DateTimeOffset(2026, 11, 10, 14, 30, 45, TimeSpan.FromHours(expectedOffsetHours)), date);
        Assert.Equal(TimeSpan.FromHours(expectedOffsetHours), date.Offset);
        var call = AssertSingleCall(ServiceMethod.GetDimension);
        Assert.Equal(OpenNettyDimensions.Management.DateTime, call.Dimension);
        AssertCommonArguments(call, endpoint);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("205")]
    [InlineData("")]
    public async Task GetDateTimeAsync_InvalidTimeZoneValue_ThrowsInvalidDataException(string zone)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.DateTime);
        _service.DimensionValues[OpenNettyDimensions.Management.DateTime] = ["14", "30", "45", zone, "03", "10", "11", "2026"];

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => _controller.GetDateTimeAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public async Task GetDeviceDescriptionAsync_DimensionValues_AreConvertedToDeviceDescription()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.DeviceDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.DeviceDescription] = ["9223", "2", "3", "4"];

        var description = await _controller.GetDeviceDescriptionAsync(endpoint, Token);

        Assert.Equal("2407", description.Model);
        Assert.Equal(new Version(2, 0), description.Version);
        Assert.Equal(3, description.FunctionCode);
        Assert.Equal(4, description.Units);
        var call = AssertSingleCall(ServiceMethod.GetDimension);
        Assert.Equal(OpenNettyDimensions.Diagnostics.DeviceDescription, call.Dimension);
        AssertCommonArguments(call, endpoint);
    }

    [Fact]
    public async Task GetDeviceDescriptionAsync_IncompleteDimensionValues_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.DeviceDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.DeviceDescription] = ["9223", "2"];

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetDeviceDescriptionAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public async Task GetDryContactStateAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => _controller.GetDryContactStateAsync(null!, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task GetDryContactStateAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.DryContactScenarioEvent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetDryContactStateAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Theory]
    [InlineData(true, OpenNettyModels.ScenariosPlus.DryContactState.Open)]
    [InlineData(false, OpenNettyModels.ScenariosPlus.DryContactState.Closed)]
    public async Task GetDryContactStateAsync_StatusReply_IsConvertedToDryContactState(bool openReply, OpenNettyModels.ScenariosPlus.DryContactState expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.DryContactState);
        _service.StatusReply = (openReply ? OpenNettyCommands.ScenariosPlus.DryContactOn : OpenNettyCommands.ScenariosPlus.DryContactOff)
            .WithParameters(["0"]);

        var state = await _controller.GetDryContactStateAsync(endpoint, Token);

        Assert.Equal(expected, state);
        var call = AssertSingleCall(ServiceMethod.GetStatus);
        Assert.Equal(OpenNettyCategories.ScenariosPlus, call.Category!.Value);
        AssertCommonArguments(call, endpoint);
    }

    [Fact]
    public async Task GetDryContactStateAsync_UnexpectedStatusReply_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.DryContactState);
        _service.StatusReply = OpenNettyCommands.ScenariosPlus.DryContactOn.WithParameters(["1"]);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => _controller.GetDryContactStateAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public async Task GetDryContactStateAsync_StatusFilter_AcceptsOnlyDryContactStateCommands()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.DryContactState);
        _service.StatusReply = OpenNettyCommands.ScenariosPlus.DryContactOn.WithParameters(["0"]);
        await _controller.GetDryContactStateAsync(endpoint, Token);
        var call = AssertSingleCall(ServiceMethod.GetStatus);

        Assert.True(await IsAcceptedAsync(call, OpenNettyCommands.ScenariosPlus.DryContactOn.WithParameters(["0"])));
        Assert.True(await IsAcceptedAsync(call, OpenNettyCommands.ScenariosPlus.DryContactOff.WithParameters(["0"])));
        Assert.False(await IsAcceptedAsync(call, OpenNettyCommands.ScenariosPlus.DryContactOn.WithParameters(["1"])));
        Assert.False(await IsAcceptedAsync(call, OpenNettyCommands.Lighting.On));
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task GetFirmwareVersionAsync_NonNitooEndpoint_ReadsFirmwareVersionDimension(OpenNettyProtocol protocol)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.FirmwareVersion);
        _service.DimensionValues[OpenNettyDimensions.Management.FirmwareVersion] = ["3", "4", "5"];

        var version = await _controller.GetFirmwareVersionAsync(endpoint, Token);

        Assert.Equal(new Version(3, 4, 5), version);
        var call = AssertSingleCall(ServiceMethod.GetDimension);
        Assert.Equal(OpenNettyDimensions.Management.FirmwareVersion, call.Dimension);
        AssertCommonArguments(call, endpoint);
    }

    [Fact]
    public async Task GetFirmwareVersionAsync_NitooDevice_UsesDeviceDescriptionVersion()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.FirmwareVersion, OpenNettyCapabilities.DeviceDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.DeviceDescription] = ["9223", "5", "3", "2"];

        var version = await _controller.GetFirmwareVersionAsync(endpoint, Token);

        Assert.Equal(new Version(5, 0), version);
        Assert.Equal(OpenNettyDimensions.Diagnostics.DeviceDescription, AssertSingleCall(ServiceMethod.GetDimension).Dimension);
    }

    [Fact]
    public async Task GetFirmwareVersionAsync_NitooDeviceWithoutDeviceDescriptionCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.FirmwareVersion);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetFirmwareVersionAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public async Task GetHardwareVersionAsync_ScsEndpoint_ReadsHardwareVersionDimension()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.HardwareVersion);
        _service.DimensionValues[OpenNettyDimensions.Management.HardwareVersion] = ["6", "7", "8"];

        var version = await _controller.GetHardwareVersionAsync(endpoint, Token);

        Assert.Equal(new Version(6, 7, 8), version);
        AssertCommonArguments(AssertSingleCall(ServiceMethod.GetDimension), endpoint);
    }

    [Theory]
    [InlineData("FirmwareVersion")]
    [InlineData("HardwareVersion")]
    public async Task GetVersionAsync_NitooGateway_DisablesAcknowledgementValidation(string operation)
    {
        var endpoint = CreateEndpoint(
            OpenNettyProtocol.Nitoo, OpenNettyCapabilities.OpenWebNetGateway,
            OpenNettyCapabilities.FirmwareVersion, OpenNettyCapabilities.HardwareVersion);
        _service.DimensionValues[GetVersionDimension(operation)] = ["1", "2", "3"];

        var version = await ReadVersionAsync(operation, endpoint);

        Assert.Equal(new Version(1, 2, 3), version);
        var call = AssertSingleCall(ServiceMethod.GetDimension);
        Assert.Equal(GetVersionDimension(operation), call.Dimension);
        Assert.Equal(GetDefaultOptions(endpoint) with { DisableAcknowledgementValidation = true }, call.Options);
    }

    [Theory]
    [InlineData("FirmwareVersion")]
    [InlineData("HardwareVersion")]
    public async Task GetVersionAsync_ZigbeeEndDevice_RelaxesTransmissionOptions(string operation)
    {
        var endpoint = WithSetting(
            CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.FirmwareVersion, OpenNettyCapabilities.HardwareVersion),
            OpenNettySettings.ZigbeeDeviceType, OpenNettySettings.ZigbeeDeviceTypes.EndDevice);
        _service.DimensionValues[GetVersionDimension(operation)] = ["1", "2", "3"];

        await ReadVersionAsync(operation, endpoint);

        Assert.Equal(
            GetDefaultOptions(endpoint) with
            {
                DisallowAllRetransmissions = true,
                KeepSessionAliveOnMissingAcknowledgement = true,
                AcknowledgementTimeout = TimeSpan.FromSeconds(10),
                OutgoingMessageProcessingTimeout = TimeSpan.FromSeconds(20),
                UniqueDimensionReplyTimeout = TimeSpan.FromSeconds(10)
            }, AssertSingleCall(ServiceMethod.GetDimension).Options);
    }

    [Theory]
    [InlineData("FirmwareVersion")]
    [InlineData("HardwareVersion")]
    public async Task GetVersionAsync_ZigbeeEndDeviceWithLongerTimeouts_KeepsExistingTimeouts(string operation)
    {
        var gateway = GetGateway(OpenNettyProtocol.Zigbee);
        var customized = gateway with
        {
            Options = gateway.Options with
            {
                DefaultTransmissionOptions = gateway.Options.DefaultTransmissionOptions with
                {
                    AcknowledgementTimeout = TimeSpan.FromSeconds(15),
                    OutgoingMessageProcessingTimeout = TimeSpan.FromSeconds(40),
                    UniqueDimensionReplyTimeout = TimeSpan.FromSeconds(25)
                }
            }
        };
        var endpoint = WithSetting(
            CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.FirmwareVersion, OpenNettyCapabilities.HardwareVersion) with { Gateway = customized },
            OpenNettySettings.ZigbeeDeviceType, OpenNettySettings.ZigbeeDeviceTypes.EndDevice);
        _service.DimensionValues[GetVersionDimension(operation)] = ["1", "2", "3"];

        await ReadVersionAsync(operation, endpoint);

        var options = AssertSingleCall(ServiceMethod.GetDimension).Options!;
        Assert.Equal(TimeSpan.FromSeconds(15), options.AcknowledgementTimeout);
        Assert.Equal(TimeSpan.FromSeconds(40), options.OutgoingMessageProcessingTimeout);
        Assert.Equal(TimeSpan.FromSeconds(25), options.UniqueDimensionReplyTimeout);
        Assert.True(options.DisallowAllRetransmissions);
        Assert.True(options.KeepSessionAliveOnMissingAcknowledgement);
    }

    [Fact]
    public async Task GetMacAddressAsync_DimensionValues_AreFormattedAsColonSeparatedHexadecimal()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.MacAddress);
        _service.DimensionValues[OpenNettyDimensions.Management.MacAddress] = ["0", "31", "5", "170", "187", "204"];

        var address = await _controller.GetMacAddressAsync(endpoint, Token);

        Assert.Equal("00:1F:05:AA:BB:CC", address);
        var call = AssertSingleCall(ServiceMethod.GetDimension);
        Assert.Equal(OpenNettyDimensions.Management.MacAddress, call.Dimension);
        AssertCommonArguments(call, endpoint);
    }

    [Fact]
    public async Task GetMemoryDataAsync_NoMemoryEntries_ReturnsEmptyArray()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.MemoryReading);
        _service.ObservedMessages.Add(CreateDimensionRead(endpoint, OpenNettyDimensions.Diagnostics.MemoryDepth, "0"));

        var data = await _controller.GetMemoryDataAsync(endpoint, Token);

        Assert.Empty(data);
    }

    [Fact]
    public async Task GetMemoryDataAsync_MemoryRead_ObservesMemoryReadCommandWithoutActionValidation()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.MemoryReading);
        _service.ObservedMessages.Add(CreateDimensionRead(endpoint, OpenNettyDimensions.Diagnostics.MemoryDepth, "0"));

        await _controller.GetMemoryDataAsync(endpoint, Token);

        var call = AssertSingleCall(ServiceMethod.ObserveMessages);
        var expected = OpenNettyMessage.CreateCommand(
            OpenNettyProtocol.Nitoo, OpenNettyCommands.Diagnostics.MemoryRead, endpoint.Address, endpoint.Medium);
        Assert.Equal(expected, call.Message);
        Assert.Same(endpoint.Gateway, call.Gateway);
        Assert.Equal(GetDefaultOptions(endpoint) with { DisableActionValidation = true }, call.Options);
    }

    [Fact]
    public async Task GetMemoryDataAsync_MemoryEntries_AreReturnedOrderedByFrameNumber()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.MemoryReading);
        _service.ObservedMessages.AddRange(
        [
            CreateDimensionRead(endpoint, OpenNettyDimensions.Diagnostics.MemoryDepth, "3"),
            CreateDimensionRead(endpoint, OpenNettyDimensions.Diagnostics.MemoryData, "96", "19745", "7", "3"),
            CreateDimensionRead(endpoint, OpenNettyDimensions.Diagnostics.ExtendedMemoryData, "64", "19746", "8", "1"),
            CreateDimensionRead(endpoint, OpenNettyDimensions.Diagnostics.MemoryData, "128", "19747", "9", "2")
        ]);

        var data = await _controller.GetMemoryDataAsync(endpoint, Token);

        Assert.Equal(
            [
                new OpenNettyModels.Diagnostics.MemoryData { Address = OpenNettyAddress.FromNitooAddress(1234, 2), FunctionCode = 8, Medium = OpenNettyMedium.Radio },
                new OpenNettyModels.Diagnostics.MemoryData { Address = OpenNettyAddress.FromNitooAddress(1234, 3), FunctionCode = 9, Medium = OpenNettyMedium.Infrared },
                new OpenNettyModels.Diagnostics.MemoryData { Address = OpenNettyAddress.FromNitooAddress(1234, 1), FunctionCode = 7, Medium = OpenNettyMedium.Powerline }
            ], data);
    }

    [Fact]
    public async Task GetMemoryDataAsync_MessagesFromOtherAddresses_AreIgnored()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.MemoryReading);
        var other = endpoint with { Address = OpenNettyAddress.FromNitooAddress(4321, 1) };
        _service.ObservedMessages.AddRange(
        [
            CreateDimensionRead(other, OpenNettyDimensions.Diagnostics.MemoryDepth, "5"),
            CreateDimensionRead(other, OpenNettyDimensions.Diagnostics.MemoryData, "64", "19745", "1", "1"),
            CreateDimensionRead(endpoint, OpenNettyDimensions.Diagnostics.MemoryDepth, "1"),
            CreateDimensionRead(endpoint, OpenNettyDimensions.Diagnostics.MemoryData, "96", "19745", "7", "1")
        ]);

        var data = await _controller.GetMemoryDataAsync(endpoint, Token);

        var item = Assert.Single(data);
        Assert.Equal(OpenNettyMedium.Powerline, item.Medium);
    }

    [Fact]
    public async Task GetPilotWireConfigurationAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => _controller.GetPilotWireConfigurationAsync(null!, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task GetPilotWireConfigurationAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.UnitDescription);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetPilotWireConfigurationAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("133")]
    public async Task GetPilotWireConfigurationAsync_UnexpectedFunctionCode_ThrowsInvalidOperationException(string functionCode)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.PilotWireControl, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = [functionCode, "3"];

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetPilotWireConfigurationAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Theory]
    [InlineData("6", "3", OpenNettyModels.TemperatureControl.PilotWireMode.Eco, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, false, false)]
    [InlineData("132", "0", OpenNettyModels.TemperatureControl.PilotWireMode.Comfort, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, false, false)]
    [InlineData("6", "75", OpenNettyModels.TemperatureControl.PilotWireMode.Eco, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, true, false)]
    [InlineData("132", "145", OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, false, true)]
    [InlineData("6", "28", OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, true, true)]
    public async Task GetPilotWireConfigurationAsync_UnitDescription_IsConvertedToConfiguration(
        string functionCode, string value, OpenNettyModels.TemperatureControl.PilotWireMode mode, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration duration, bool derogation, bool shutdown)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.PilotWireControl, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = [functionCode, value];

        var configuration = await _controller.GetPilotWireConfigurationAsync(endpoint, Token);

        Assert.Equal(
            new OpenNettyModels.TemperatureControl.PilotWireConfiguration
            {
                DerogationDuration = duration,
                IsDerogationActive = derogation,
                IsShutdownActive = shutdown,
                Mode = mode
            }, configuration);
    }

    [Fact]
    public async Task GetShutterPositionAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => _controller.GetShutterPositionAsync(null!, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task GetShutterPositionAsync_EndpointWithoutAdvancedShutterState_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicShutterState);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetShutterPositionAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public async Task GetShutterPositionAsync_UnsupportedFunctionType_ThrowsInvalidOperationException()
    {
        var endpoint = WithSetting(
            CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.AdvancedShutterState),
            OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.LightActuator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetShutterPositionAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0099), exception.Message);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("57", 57)]
    [InlineData("100", 100)]
    public async Task GetShutterPositionAsync_ValidPosition_ReturnsPosition(string value, byte expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.AdvancedShutterState);
        _service.DimensionValues[OpenNettyDimensions.Automation.ShutterStatus] = ["10", value];

        var position = await _controller.GetShutterPositionAsync(endpoint, Token);

        Assert.Equal(expected, position);
        var call = AssertSingleCall(ServiceMethod.GetDimension);
        Assert.Equal(OpenNettyDimensions.Automation.ShutterStatus, call.Dimension);
        AssertCommonArguments(call, endpoint);
    }

    [Fact]
    public async Task GetShutterPositionAsync_UnknownPositionValue_ReturnsNull()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.AdvancedShutterState);
        _service.DimensionValues[OpenNettyDimensions.Automation.ShutterStatus] = ["10", "255"];

        var position = await _controller.GetShutterPositionAsync(endpoint, Token);

        Assert.Null(position);
    }

    [Theory]
    [InlineData("101")]
    [InlineData("254")]
    public async Task GetShutterPositionAsync_PositionOutOfRange_ThrowsInvalidDataException(string value)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.AdvancedShutterState);
        _service.DimensionValues[OpenNettyDimensions.Automation.ShutterStatus] = ["10", value];

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => _controller.GetShutterPositionAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Theory]
    [InlineData("10", null)]
    [InlineData("10", "")]
    public async Task GetShutterPositionAsync_MissingPositionValue_ThrowsInvalidDataException(string status, string? position)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.AdvancedShutterState);
        _service.DimensionValues[OpenNettyDimensions.Automation.ShutterStatus] = position is null ? [status] : [status, position];

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => _controller.GetShutterPositionAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public async Task GetShutterStateAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => _controller.GetShutterStateAsync(null!, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task GetShutterStateAsync_EndpointWithoutShutterStateCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicShutterControl);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetShutterStateAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public async Task GetShutterStateAsync_UnsupportedFunctionType_ThrowsInvalidOperationException()
    {
        var endpoint = WithSetting(
            CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicShutterState),
            OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.LightActuator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetShutterStateAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0099), exception.Message);
    }

    [Theory]
    [InlineData("100", OpenNettyModels.Automation.ShutterState.Opening)]
    [InlineData("102", OpenNettyModels.Automation.ShutterState.Opening)]
    [InlineData("0", OpenNettyModels.Automation.ShutterState.Closing)]
    [InlineData("103", OpenNettyModels.Automation.ShutterState.Closing)]
    [InlineData("101", OpenNettyModels.Automation.ShutterState.Stopped)]
    [InlineData("50", OpenNettyModels.Automation.ShutterState.Stopped)]
    public async Task GetShutterStateAsync_NitooEndpoint_ParsesUnitDescription(string value, OpenNettyModels.Automation.ShutterState expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.BasicShutterState, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = ["139", value];

        var state = await _controller.GetShutterStateAsync(endpoint, Token);

        Assert.Equal(expected, state);
    }

    [Fact]
    public async Task GetShutterStateAsync_NitooEndpointWithUnexpectedFunctionCode_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.BasicShutterState, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = ["129", "100"];

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => _controller.GetShutterStateAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Theory]
    [InlineData("10", "0", OpenNettyModels.Automation.ShutterState.Closed)]
    [InlineData("10", "1", OpenNettyModels.Automation.ShutterState.Open)]
    [InlineData("10", "100", OpenNettyModels.Automation.ShutterState.Open)]
    [InlineData("10", "255", OpenNettyModels.Automation.ShutterState.Stopped)]
    [InlineData("11", "0", OpenNettyModels.Automation.ShutterState.Opening)]
    [InlineData("13", "0", OpenNettyModels.Automation.ShutterState.Opening)]
    [InlineData("12", "0", OpenNettyModels.Automation.ShutterState.Closing)]
    [InlineData("14", "0", OpenNettyModels.Automation.ShutterState.Closing)]
    public async Task GetShutterStateAsync_AdvancedState_ParsesShutterStatusDimension(string status, string position, OpenNettyModels.Automation.ShutterState expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.AdvancedShutterState, OpenNettyCapabilities.BasicShutterState);
        _service.DimensionValues[OpenNettyDimensions.Automation.ShutterStatus] = [status, position];

        var state = await _controller.GetShutterStateAsync(endpoint, Token);

        Assert.Equal(expected, state);
        var call = AssertSingleCall(ServiceMethod.GetDimension);
        Assert.Equal(OpenNettyDimensions.Automation.ShutterStatus, call.Dimension);
        AssertCommonArguments(call, endpoint);
    }

    [Theory]
    [InlineData("10", "150")]
    [InlineData("99", "0")]
    [InlineData("0", "0")]
    public async Task GetShutterStateAsync_AdvancedStateWithUnexpectedValues_ThrowsInvalidDataException(string status, string position)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.AdvancedShutterState);
        _service.DimensionValues[OpenNettyDimensions.Automation.ShutterStatus] = [status, position];

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => _controller.GetShutterStateAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task GetShutterStateAsync_BasicState_ConvertsAutomationStatusToShutterState(OpenNettyProtocol protocol)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.BasicShutterState);

        _service.StatusReply = OpenNettyCommands.Automation.Stop;
        Assert.Equal(OpenNettyModels.Automation.ShutterState.Stopped, await _controller.GetShutterStateAsync(endpoint, Token));

        _service.StatusReply = OpenNettyCommands.Automation.Up;
        Assert.Equal(OpenNettyModels.Automation.ShutterState.Opening, await _controller.GetShutterStateAsync(endpoint, Token));

        _service.StatusReply = OpenNettyCommands.Automation.Down;
        Assert.Equal(OpenNettyModels.Automation.ShutterState.Closing, await _controller.GetShutterStateAsync(endpoint, Token));
    }

    [Fact]
    public async Task GetShutterStateAsync_BasicStateOnScs_UsesDefaultTransmissionOptions()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicShutterState);
        _service.StatusReply = OpenNettyCommands.Automation.Stop;

        await _controller.GetShutterStateAsync(endpoint, Token);

        var call = AssertSingleCall(ServiceMethod.GetStatus);
        Assert.Equal(OpenNettyCategories.Automation, call.Category!.Value);
        AssertCommonArguments(call, endpoint);
    }

    [Fact]
    public async Task GetShutterStateAsync_BasicStateWithUnexpectedReply_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicShutterState);
        _service.StatusReply = OpenNettyCommands.Lighting.On;

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => _controller.GetShutterStateAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public async Task GetShutterStateAsync_BasicState_FilterAcceptsAutomationCommandsOnly()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicShutterState);
        _service.StatusReply = OpenNettyCommands.Automation.Stop;
        await _controller.GetShutterStateAsync(endpoint, Token);
        var call = AssertSingleCall(ServiceMethod.GetStatus);

        Assert.True(await IsAcceptedAsync(call, OpenNettyCommands.Automation.Stop));
        Assert.True(await IsAcceptedAsync(call, OpenNettyCommands.Automation.Up));
        Assert.True(await IsAcceptedAsync(call, OpenNettyCommands.Automation.Down));
        Assert.False(await IsAcceptedAsync(call, OpenNettyCommands.Lighting.On));
    }

    [Fact]
    public async Task GetShutterStateAsync_ZigbeeBasicState_UsesCustomValidationAndDisablesAcknowledgementValidation()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.BasicShutterState);
        _service.StatusReply = OpenNettyCommands.Automation.Up;

        await _controller.GetShutterStateAsync(endpoint, Token);

        var options = AssertSingleCall(ServiceMethod.GetStatus).Options!;
        Assert.NotNull(options.CustomValidationDelegate);
        Assert.True(options.DisableAcknowledgementValidation);
    }

    [Fact]
    public async Task GetShutterStateAsync_ZigbeeCustomValidation_AcceptsAutomationCommandFromEndpoint()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.BasicShutterState);
        var message = OpenNettyMessage.CreateCommand(OpenNettyProtocol.Zigbee, OpenNettyCommands.Automation.Up, endpoint.Address, endpoint.Medium);

        var valid = await ValidateZigbeeShutterStatusAsync(endpoint, message);

        Assert.True(valid);
    }

    [Fact]
    public async Task GetShutterStateAsync_ZigbeeCustomValidation_IgnoresLightingCommandsAndAcceptsAcknowledgement()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.BasicShutterState);
        var lighting = OpenNettyMessage.CreateCommand(OpenNettyProtocol.Zigbee, OpenNettyCommands.Lighting.On, endpoint.Address, endpoint.Medium);

        var valid = await ValidateZigbeeShutterStatusAsync(endpoint, lighting, CreateAcknowledgement(OpenNettyProtocol.Zigbee));

        Assert.True(valid);
    }

    [Fact]
    public async Task GetShutterStateAsync_ZigbeeCustomValidation_RejectsNegativeAcknowledgement()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.BasicShutterState);
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Zigbee, OpenNettyFrames.NegativeAcknowledgement);

        var valid = await ValidateZigbeeShutterStatusAsync(endpoint, message);

        Assert.False(valid);
    }

    [Fact]
    public async Task GetSmartMeterIndexesAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => _controller.GetSmartMeterIndexesAsync(null!, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task GetSmartMeterIndexesAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.SmartMeterInformation);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetSmartMeterIndexesAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public async Task GetSmartMeterIndexesAsync_BaseSubscription_ReturnsBaseIndexWithoutOffset()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.SmartMeterIndexes);
        _service.DimensionValues[OpenNettyDimensions.TemperatureControl.SmartMeterIndexes] = ["1", "12345"];

        var indexes = await _controller.GetSmartMeterIndexesAsync(endpoint, Token);

        Assert.Equal(OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.Base, indexes.SubscriptionType);
        Assert.Equal(new OpenNettyModels.TemperatureControl.SmartMeterIndex { BaseIndex = 12345, OffPeakIndex = 0 }, indexes.BaseIndex);
        Assert.Null(indexes.BlueIndex);
        Assert.Null(indexes.PeakOffPeakIndex);
        Assert.Null(indexes.RedIndex);
        Assert.Null(indexes.WhiteIndex);
        var call = AssertSingleCall(ServiceMethod.GetDimension);
        Assert.Equal(OpenNettyDimensions.TemperatureControl.SmartMeterIndexes, call.Dimension);
        AssertCommonArguments(call, endpoint);
    }

    [Theory]
    [InlineData("500", 1500UL)]
    [InlineData("-200", 800UL)]
    [InlineData("0", 1000UL)]
    public async Task GetSmartMeterIndexesAsync_BaseSubscriptionWithOffsetSetting_AppliesOffsetToBaseIndex(string offset, ulong expected)
    {
        var endpoint = WithSetting(
            CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.SmartMeterIndexes),
            OpenNettySettings.SmartMeterBaseIndexOffset, offset);
        _service.DimensionValues[OpenNettyDimensions.TemperatureControl.SmartMeterIndexes] = ["1", "1000"];

        var indexes = await _controller.GetSmartMeterIndexesAsync(endpoint, Token);

        Assert.Equal(new OpenNettyModels.TemperatureControl.SmartMeterIndex { BaseIndex = expected, OffPeakIndex = 0 }, indexes.BaseIndex);
    }

    [Fact]
    public async Task GetSmartMeterIndexesAsync_BaseSubscriptionWithInvalidOffsetSetting_IgnoresOffset()
    {
        var endpoint = WithSetting(
            CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.SmartMeterIndexes),
            OpenNettySettings.SmartMeterBaseIndexOffset, "not a number");
        _service.DimensionValues[OpenNettyDimensions.TemperatureControl.SmartMeterIndexes] = ["1", "1000"];

        var indexes = await _controller.GetSmartMeterIndexesAsync(endpoint, Token);

        Assert.Equal(1000UL, indexes.BaseIndex!.BaseIndex);
    }

    [Theory]
    [InlineData("2", OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.PeakOffPeak)]
    [InlineData("3", OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.Tempo)]
    [InlineData("4", OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.Tempo)]
    [InlineData("5", OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.Tempo)]
    public async Task GetSmartMeterIndexesAsync_ComplexSubscription_ReturnsOnlyMatchingIndex(string kind, OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.SmartMeterIndexes);
        _service.DimensionValues[OpenNettyDimensions.TemperatureControl.SmartMeterIndexes] = [kind, "100", "200"];

        var indexes = await _controller.GetSmartMeterIndexesAsync(endpoint, Token);

        var expectedIndex = new OpenNettyModels.TemperatureControl.SmartMeterIndex { BaseIndex = 100, OffPeakIndex = 200 };
        Assert.Equal(expected, indexes.SubscriptionType);
        Assert.Null(indexes.BaseIndex);
        Assert.Equal(kind is "2" ? expectedIndex : null, indexes.PeakOffPeakIndex);
        Assert.Equal(kind is "3" ? expectedIndex : null, indexes.BlueIndex);
        Assert.Equal(kind is "4" ? expectedIndex : null, indexes.WhiteIndex);
        Assert.Equal(kind is "5" ? expectedIndex : null, indexes.RedIndex);
    }

    [Theory]
    [InlineData("2", "SmartMeterPeakOffPeakIndexOffsetBase", "SmartMeterPeakOffPeakIndexOffsetOffPeak")]
    [InlineData("3", "SmartMeterBlueIndexOffsetBase", "SmartMeterBlueIndexOffsetOffPeak")]
    [InlineData("4", "SmartMeterWhiteIndexOffsetBase", "SmartMeterWhiteIndexOffsetOffPeak")]
    [InlineData("5", "SmartMeterRedIndexOffsetBase", "SmartMeterRedIndexOffsetOffPeak")]
    public async Task GetSmartMeterIndexesAsync_ComplexSubscriptionWithOffsetSettings_AppliesOffsetsIndependently(
        string kind, string baseSetting, string offPeakSetting)
    {
        var endpoint = WithSetting(
            WithSetting(CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.SmartMeterIndexes), GetSmartMeterSetting(baseSetting), "50"),
            GetSmartMeterSetting(offPeakSetting), "-25");
        _service.DimensionValues[OpenNettyDimensions.TemperatureControl.SmartMeterIndexes] = [kind, "1000", "2000"];

        var indexes = await _controller.GetSmartMeterIndexesAsync(endpoint, Token);

        var index = kind switch
        {
            "2" => indexes.PeakOffPeakIndex,
            "3" => indexes.BlueIndex,
            "4" => indexes.WhiteIndex,
            _   => indexes.RedIndex
        };
        Assert.Equal(new OpenNettyModels.TemperatureControl.SmartMeterIndex { BaseIndex = 1050, OffPeakIndex = 1975 }, index);
    }

    [Fact]
    public async Task GetSmartMeterIndexesAsync_UnknownSubscriptionType_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.SmartMeterIndexes);
        _service.DimensionValues[OpenNettyDimensions.TemperatureControl.SmartMeterIndexes] = ["9", "100"];

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => _controller.GetSmartMeterIndexesAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public async Task GetSmartMeterInformationAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => _controller.GetSmartMeterInformationAsync(null!, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task GetSmartMeterInformationAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.SmartMeterIndexes, OpenNettyCapabilities.UnitDescription);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetSmartMeterInformationAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public async Task GetSmartMeterInformationAsync_UnexpectedFunctionCode_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.SmartMeterInformation, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = ["6", "32"];

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetSmartMeterInformationAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Theory]
    [InlineData("32", OpenNettyModels.TemperatureControl.SmartMeterRateType.OffPeak, false)]
    [InlineData("33", OpenNettyModels.TemperatureControl.SmartMeterRateType.OffPeak, true)]
    [InlineData("48", OpenNettyModels.TemperatureControl.SmartMeterRateType.Peak, false)]
    [InlineData("49", OpenNettyModels.TemperatureControl.SmartMeterRateType.Peak, true)]
    public async Task GetSmartMeterInformationAsync_UnitDescription_IsConvertedToInformation(string value, OpenNettyModels.TemperatureControl.SmartMeterRateType rate, bool powerCut)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.SmartMeterInformation, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = ["7", value];

        var information = await _controller.GetSmartMeterInformationAsync(endpoint, Token);

        Assert.Equal(new OpenNettyModels.TemperatureControl.SmartMeterInformation { IsPowerCutActive = powerCut, RateType = rate }, information);
    }

    [Fact]
    public async Task GetSwitchStateAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.GetSwitchStateAsync(null!, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task GetSwitchStateAsync_EndpointWithoutStateCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchControl);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetSwitchStateAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public async Task GetSwitchStateAsync_UnsupportedFunctionType_ThrowsInvalidOperationException()
    {
        var endpoint = WithSetting(
            CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchState),
            OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.ContactState);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetSwitchStateAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0099), exception.Message);
    }

    [Theory]
    [InlineData(0, OpenNettyModels.Lighting.SwitchState.Off)]
    [InlineData(20, OpenNettyModels.Lighting.SwitchState.On)]
    [InlineData(30, OpenNettyModels.Lighting.SwitchState.On)]
    [InlineData(40, OpenNettyModels.Lighting.SwitchState.On)]
    [InlineData(50, OpenNettyModels.Lighting.SwitchState.On)]
    [InlineData(60, OpenNettyModels.Lighting.SwitchState.On)]
    [InlineData(70, OpenNettyModels.Lighting.SwitchState.On)]
    [InlineData(80, OpenNettyModels.Lighting.SwitchState.On)]
    [InlineData(90, OpenNettyModels.Lighting.SwitchState.On)]
    [InlineData(100, OpenNettyModels.Lighting.SwitchState.On)]
    public async Task GetSwitchStateAsync_ScsEndpoint_MapsLightingStatusToSwitchState(int level, OpenNettyModels.Lighting.SwitchState expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchState);
        _service.StatusReply = GetLightingCommand(level);

        var state = await _controller.GetSwitchStateAsync(endpoint, Token);

        Assert.Equal(expected, state);
        var call = AssertSingleCall(ServiceMethod.GetStatus);
        Assert.Equal(OpenNettyCategories.Lighting, call.Category!.Value);
        AssertCommonArguments(call, endpoint);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(40)]
    [InlineData(50)]
    [InlineData(60)]
    [InlineData(70)]
    [InlineData(80)]
    [InlineData(90)]
    [InlineData(100)]
    public async Task GetSwitchStateAsync_ScsEndpoint_FilterAcceptsLightingLevelCommands(int level)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchState);
        _service.StatusReply = OpenNettyCommands.Lighting.On;
        await _controller.GetSwitchStateAsync(endpoint, Token);

        var accepted = await IsAcceptedAsync(Assert.Single(_service.Calls), GetLightingCommand(level));

        Assert.True(accepted);
    }

    [Fact]
    public async Task GetSwitchStateAsync_ScsEndpoint_FilterRejectsUnrelatedCommands()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchState);
        _service.StatusReply = OpenNettyCommands.Lighting.On;
        await _controller.GetSwitchStateAsync(endpoint, Token);

        var accepted = await IsAcceptedAsync(Assert.Single(_service.Calls), OpenNettyCommands.Lighting.Toggle);

        Assert.False(accepted);
    }

    [Theory]
    [InlineData("129", "128", OpenNettyModels.Lighting.SwitchState.On)]
    [InlineData("129", "129", OpenNettyModels.Lighting.SwitchState.On)]
    [InlineData("129", "130", OpenNettyModels.Lighting.SwitchState.On)]
    [InlineData("129", "0", OpenNettyModels.Lighting.SwitchState.Off)]
    [InlineData("143", "50", OpenNettyModels.Lighting.SwitchState.On)]
    [InlineData("143", "0", OpenNettyModels.Lighting.SwitchState.Off)]
    public async Task GetSwitchStateAsync_NitooEndpoint_ParsesUnitDescription(string functionCode, string value, OpenNettyModels.Lighting.SwitchState expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = [functionCode, value];

        var state = await _controller.GetSwitchStateAsync(endpoint, Token);

        Assert.Equal(expected, state);
        Assert.Equal(OpenNettyDimensions.Diagnostics.UnitDescription, AssertSingleCall(ServiceMethod.GetDimension).Dimension);
    }

    [Fact]
    public async Task GetSwitchStateAsync_NitooEndpointWithUnknownFunctionCode_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = ["7", "1"];

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => _controller.GetSwitchStateAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public async Task GetSwitchStateAsync_NitooEndpointWithoutUnitDescriptionCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.OnOffSwitchState);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetSwitchStateAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public async Task GetSwitchStateAsync_ZigbeeEndpoint_UsesCustomValidationAndDisablesAcknowledgementValidation()
    {
        var endpoint = CreateZigbeeSwitch();
        _service.StatusReply = OpenNettyCommands.Lighting.On;

        var state = await _controller.GetSwitchStateAsync(endpoint, Token);

        Assert.Equal(OpenNettyModels.Lighting.SwitchState.On, state);
        var call = AssertSingleCall(ServiceMethod.GetStatus);
        Assert.NotNull(call.Options!.CustomValidationDelegate);
        Assert.True(call.Options.DisableAcknowledgementValidation);
        Assert.Equal(
            GetDefaultOptions(endpoint) with
            {
                CustomValidationDelegate = call.Options.CustomValidationDelegate,
                DisableAcknowledgementValidation = true
            }, call.Options);
    }

    [Fact]
    public async Task GetSwitchStateAsync_ZigbeeCustomValidation_AcceptsAcknowledgement()
    {
        var endpoint = CreateZigbeeSwitch();

        var valid = await ValidateZigbeeStatusAsync(endpoint, CreateAcknowledgement(OpenNettyProtocol.Zigbee));

        Assert.True(valid);
    }

    [Fact]
    public async Task GetSwitchStateAsync_ZigbeeCustomValidation_AcceptsLightingCommandFromEndpoint()
    {
        var endpoint = CreateZigbeeSwitch();
        var message = OpenNettyMessage.CreateCommand(
            OpenNettyProtocol.Zigbee, OpenNettyCommands.Lighting.On, endpoint.Address, endpoint.Medium);

        var valid = await ValidateZigbeeStatusAsync(endpoint, message);

        Assert.True(valid);
    }

    [Fact]
    public async Task GetSwitchStateAsync_ZigbeeCustomValidation_IgnoresCommandsFromOtherEndpoints()
    {
        var endpoint = CreateZigbeeSwitch();
        var other = OpenNettyMessage.CreateCommand(
            OpenNettyProtocol.Zigbee, OpenNettyCommands.Lighting.On, OpenNettyAddress.FromDecimalZigbeeAddress(5000000, 1), endpoint.Medium);

        var valid = await ValidateZigbeeStatusAsync(endpoint, other, CreateAcknowledgement(OpenNettyProtocol.Zigbee));

        Assert.True(valid);
    }

    [Fact]
    public async Task GetSwitchStateAsync_ZigbeeCustomValidation_RejectsNegativeAcknowledgement()
    {
        var endpoint = CreateZigbeeSwitch();
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Zigbee, OpenNettyFrames.NegativeAcknowledgement);

        var valid = await ValidateZigbeeStatusAsync(endpoint, message);

        Assert.False(valid);
    }

    [Fact]
    public async Task GetSwitchStateAsync_ZigbeeCustomValidation_RejectsBusyNegativeAcknowledgement()
    {
        var endpoint = CreateZigbeeSwitch();
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Zigbee, OpenNettyFrames.BusyNegativeAcknowledgement);

        var valid = await ValidateZigbeeStatusAsync(endpoint, message);

        Assert.False(valid);
    }

    [Fact]
    public async Task GetUnitDescriptionAsync_DimensionValues_AreConvertedToUnitDescription()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = ["143", "50", "7"];

        var description = await _controller.GetUnitDescriptionAsync(endpoint, Token);

        Assert.Equal(143, description.FunctionCode);
        Assert.Equal(["50", "7"], description.Values, StringComparer.Ordinal);
        var call = AssertSingleCall(ServiceMethod.GetDimension);
        Assert.Equal(OpenNettyDimensions.Diagnostics.UnitDescription, call.Dimension);
        AssertCommonArguments(call, endpoint);
    }

    [Fact]
    public async Task GetUnitDescriptionAsync_MissingValues_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = ["143"];

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetUnitDescriptionAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public async Task GetUptimeAsync_DimensionValues_AreConvertedToTimeSpan()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.Uptime);
        _service.DimensionValues[OpenNettyDimensions.Management.Uptime] = ["2", "3", "4", "5"];

        var uptime = await _controller.GetUptimeAsync(endpoint, Token);

        Assert.Equal(new TimeSpan(2, 3, 4, 5), uptime);
        var call = AssertSingleCall(ServiceMethod.GetDimension);
        Assert.Equal(OpenNettyDimensions.Management.Uptime, call.Dimension);
        AssertCommonArguments(call, endpoint);
    }

    public static TheoryData<string> DiagnosticReadOperations =>
    [
        "DeviceDescription", "UnitDescription", "FirmwareVersion", "HardwareVersion", "MacAddress", "Uptime", "DateTime", "MemoryData"
    ];

    [Theory]
    [MemberData(nameof(DiagnosticReadOperations))]
    public async Task DiagnosticRead_NullEndpoint_ThrowsArgumentNullException(string operation)
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => ReadDiagnosticAsync(operation, null!));

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(DiagnosticReadOperations))]
    public async Task DiagnosticRead_EndpointWithoutCapability_ThrowsInvalidOperationException(string operation)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.OutgoingCommunication);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ReadDiagnosticAsync(operation, endpoint));

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Fact]
    public async Task GetWaterHeaterStateAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => _controller.GetWaterHeaterStateAsync(null!, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task GetWaterHeaterStateAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.UnitDescription);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.GetWaterHeaterStateAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Theory]
    [InlineData("132", "0")]
    [InlineData("133", "")]
    public async Task GetWaterHeaterStateAsync_UnexpectedUnitDescription_ThrowsInvalidOperationException(string functionCode, string value)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.WaterHeating, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = [functionCode, value];

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => _controller.GetWaterHeaterStateAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Theory]
    [InlineData("0", OpenNettyModels.TemperatureControl.WaterHeaterState.Idle)]
    [InlineData("32", OpenNettyModels.TemperatureControl.WaterHeaterState.Idle)]
    [InlineData("1", OpenNettyModels.TemperatureControl.WaterHeaterState.Heating)]
    [InlineData("17", OpenNettyModels.TemperatureControl.WaterHeaterState.Heating)]
    [InlineData("33", OpenNettyModels.TemperatureControl.WaterHeaterState.Heating)]
    public async Task GetWaterHeaterStateAsync_UnitDescription_IsConvertedToState(string value, OpenNettyModels.TemperatureControl.WaterHeaterState expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.WaterHeating, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = ["133", value];

        var state = await _controller.GetWaterHeaterStateAsync(endpoint, Token);

        Assert.Equal(expected, state);
    }

    [Fact]
    public async Task GetWaterHeaterStateAsync_UnknownStateValue_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.WaterHeating, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = ["133", "99"];

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => _controller.GetWaterHeaterStateAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Theory]
    [InlineData("11", 11)]
    [InlineData("26", 26)]
    public async Task GetZigbeeChannelAsync_ReturnedValue_IsParsedAsByte(string value, byte expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.ZigbeeNetworkManagement);
        _service.DimensionValues[OpenNettyDimensions.Management.ZigbeeChannel] = [value];

        var channel = await _controller.GetZigbeeChannelAsync(endpoint, Token);

        Assert.Equal(expected, channel);
        var call = AssertSingleCall(ServiceMethod.GetDimension);
        Assert.Equal(OpenNettyDimensions.Management.ZigbeeChannel, call.Dimension);
        AssertCommonArguments(call, endpoint);
    }

    public static TheoryData<string> ZigbeeNetworkOperations =>
    [
        "OpenNetwork", "CloseNetwork", "EnableSupervision", "DisableSupervision",
        "CreateNetwork", "JoinNetwork", "LeaveNetwork", "CountDevices", "GetChannel"
    ];

    public static TheoryData<string> ZigbeeLongRunningNetworkOperations => ["CreateNetwork", "JoinNetwork", "LeaveNetwork"];

    [Theory]
    [MemberData(nameof(ZigbeeNetworkOperations))]
    public async Task ZigbeeNetworkOperation_NullEndpoint_ThrowsArgumentNullException(string operation)
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => ExecuteZigbeeNetworkOperationAsync(operation, null!));

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(ZigbeeNetworkOperations))]
    public async Task ZigbeeNetworkOperation_EndpointWithoutCapability_ThrowsInvalidOperationException(string operation)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.ZigbeeBinding);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteZigbeeNetworkOperationAsync(operation, endpoint));

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData("OpenNetwork")]
    [InlineData("CloseNetwork")]
    [InlineData("EnableSupervision")]
    [InlineData("DisableSupervision")]
    public async Task ZigbeeNetworkOperation_SimpleCommand_SendsMatchingManagementCommand(string operation)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.ZigbeeNetworkManagement);

        await ExecuteZigbeeNetworkOperationAsync(operation, endpoint);

        AssertCommandExecuted(endpoint, operation switch
        {
            "OpenNetwork"       => OpenNettyCommands.Management.OpenZigbeeNetwork,
            "CloseNetwork"      => OpenNettyCommands.Management.CloseZigbeeNetwork,
            "EnableSupervision" => OpenNettyCommands.Management.Supervisor,
            _                   => OpenNettyCommands.Management.SupervisorRemove
        });
    }

    [Theory]
    [InlineData("CreateNetwork")]
    [InlineData("JoinNetwork")]
    [InlineData("LeaveNetwork")]
    public async Task ZigbeeNetworkOperation_LongRunningCommand_SendsMatchingManagementCommand(string operation)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.ZigbeeNetworkManagement);

        await ExecuteZigbeeNetworkOperationAsync(operation, endpoint);

        var call = AssertSingleCall(ServiceMethod.ExecuteCommand);
        Assert.Equal(operation switch
        {
            "CreateNetwork" => OpenNettyCommands.Management.CreateZigbeeNetwork,
            "JoinNetwork"   => OpenNettyCommands.Management.JoinZigbeeNetwork,
            _               => OpenNettyCommands.Management.LeaveZigbeeNetwork
        }, call.Command);
        Assert.Equal(endpoint.Address, call.Address);
        Assert.Equal(endpoint.Medium, call.Medium);
        Assert.Null(call.Mode);
        Assert.Same(endpoint.Gateway, call.Gateway);
    }

    [Theory]
    [MemberData(nameof(ZigbeeLongRunningNetworkOperations))]
    public async Task ZigbeeNetworkOperation_ShortDefaultTimeouts_AreIncreased(string operation)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.ZigbeeNetworkManagement);

        await ExecuteZigbeeNetworkOperationAsync(operation, endpoint);

        Assert.Equal(
            GetDefaultOptions(endpoint) with
            {
                AcknowledgementTimeout = TimeSpan.FromSeconds(20),
                OutgoingMessageProcessingTimeout = TimeSpan.FromSeconds(30)
            }, AssertSingleCall(ServiceMethod.ExecuteCommand).Options);
    }

    [Theory]
    [MemberData(nameof(ZigbeeLongRunningNetworkOperations))]
    public async Task ZigbeeNetworkOperation_LongDefaultTimeouts_AreKept(string operation)
    {
        var endpoint = CreateZigbeeEndpointWithTimeouts(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(60));

        await ExecuteZigbeeNetworkOperationAsync(operation, endpoint);

        Assert.Equal(GetDefaultOptions(endpoint), AssertSingleCall(ServiceMethod.ExecuteCommand).Options);
    }

    [Theory]
    [InlineData("CreateNetwork", 30, 5, 30, 30)]
    [InlineData("JoinNetwork", 30, 5, 30, 30)]
    [InlineData("LeaveNetwork", 30, 5, 30, 30)]
    [InlineData("CreateNetwork", 5, 90, 20, 90)]
    [InlineData("JoinNetwork", 5, 90, 20, 90)]
    [InlineData("LeaveNetwork", 5, 90, 20, 90)]
    [InlineData("CreateNetwork", 20, 30, 20, 30)]
    public async Task ZigbeeNetworkOperation_MixedDefaultTimeouts_OnlyShortTimeoutsAreIncreased(
        string operation, int acknowledgement, int processing, int expectedAcknowledgement, int expectedProcessing)
    {
        var endpoint = CreateZigbeeEndpointWithTimeouts(TimeSpan.FromSeconds(acknowledgement), TimeSpan.FromSeconds(processing));

        await ExecuteZigbeeNetworkOperationAsync(operation, endpoint);

        var options = AssertSingleCall(ServiceMethod.ExecuteCommand).Options!;
        Assert.Equal(TimeSpan.FromSeconds(expectedAcknowledgement), options.AcknowledgementTimeout);
        Assert.Equal(TimeSpan.FromSeconds(expectedProcessing), options.OutgoingMessageProcessingTimeout);
    }

    [Fact]
    public async Task SendRawMessageAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var message = CreateAcknowledgement(OpenNettyProtocol.Scs);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => _controller.SendRawMessageAsync(null!, message, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task SendRawMessageAsync_EndpointWithoutGatewayCapability_ThrowsInvalidOperationException()
    {
        var endpoint = WithSetting(CreateEndpoint(OpenNettyProtocol.Scs), OpenNettySettings.RawOutgoingMessages, "True");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.SendRawMessageAsync(endpoint, CreateAcknowledgement(OpenNettyProtocol.Scs), Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("False")]
    [InlineData("invalid")]
    public async Task SendRawMessageAsync_RawOutgoingMessagesNotEnabled_ThrowsInvalidOperationException(string? setting)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OpenWebNetGateway);
        if (setting is not null)
        {
            endpoint = WithSetting(endpoint, OpenNettySettings.RawOutgoingMessages, setting);
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.SendRawMessageAsync(endpoint, CreateAcknowledgement(OpenNettyProtocol.Scs), Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0121), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task SendRawMessageAsync_RawOutgoingMessagesEnabled_SendsMessageThroughGateway(OpenNettyProtocol protocol)
    {
        var endpoint = WithSetting(
            CreateEndpoint(protocol, OpenNettyCapabilities.OpenWebNetGateway), OpenNettySettings.RawOutgoingMessages, "True");
        var message = OpenNettyMessage.CreateCommand(protocol, OpenNettyCommands.Lighting.On, endpoint.Address, endpoint.Medium);

        await _controller.SendRawMessageAsync(endpoint, message, Token);

        var call = AssertSingleCall(ServiceMethod.SendMessage);
        Assert.Same(message, call.Message);
        Assert.Same(endpoint.Gateway, call.Gateway);
        Assert.Equal(GetDefaultOptions(endpoint), call.Options);
        Assert.Equal(Token, call.CancellationToken);
    }

    [Fact]
    public async Task SetBrightnessAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.SetBrightnessAsync(null!, 50, cancellationToken: Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task SetBrightnessAsync_LevelAboveOneHundred_ThrowsArgumentOutOfRangeException(OpenNettyProtocol protocol)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.AdvancedDimmingControl);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _controller.SetBrightnessAsync(endpoint, 101, cancellationToken: Token).AsTask());

        Assert.Equal("level", exception.ParamName);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData(51, 1999)]
    [InlineData(100, 0)]
    public async Task SetBrightnessAsync_NitooHighLevelWithTooShortTransition_ThrowsArgumentOutOfRangeException(byte level, int milliseconds)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.AdvancedDimmingControl);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _controller.SetBrightnessAsync(endpoint, level, TimeSpan.FromMilliseconds(milliseconds), Token).AsTask());

        Assert.Equal("transition", exception.ParamName);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task SetBrightnessAsync_ScsOrZigbeeTransitionLongerThanFiftySeconds_ThrowsArgumentOutOfRangeException(OpenNettyProtocol protocol)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.AdvancedDimmingControl);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _controller.SetBrightnessAsync(endpoint, 50, TimeSpan.FromSeconds(51), Token).AsTask());

        Assert.Equal("transition", exception.ParamName);
        Assert.Empty(_service.Calls);
    }

    [Fact]
    public async Task SetBrightnessAsync_NitooTransitionLongerThanFiveMinutes_ThrowsArgumentOutOfRangeException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.AdvancedDimmingControl);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _controller.SetBrightnessAsync(endpoint, 50, TimeSpan.FromMinutes(5).Add(TimeSpan.FromMilliseconds(1)), Token).AsTask());

        Assert.Equal("transition", exception.ParamName);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task SetBrightnessAsync_EndpointWithoutDimmingControl_ThrowsInvalidOperationException(OpenNettyProtocol protocol)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.OnOffSwitchControl);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.SetBrightnessAsync(endpoint, 50, cancellationToken: Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs, 1, "101", "0")]
    [InlineData(OpenNettyProtocol.Scs, 50, "150", "0")]
    [InlineData(OpenNettyProtocol.Scs, 100, "200", "0")]
    [InlineData(OpenNettyProtocol.Zigbee, 75, "175", "0")]
    public async Task SetBrightnessAsync_AdvancedControlWithoutTransition_SetsLevelSpeedDimension(
        OpenNettyProtocol protocol, byte level, string expectedLevel, string expectedSpeed)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.AdvancedDimmingControl);

        await _controller.SetBrightnessAsync(endpoint, level, cancellationToken: Token);

        AssertDimensionSet(endpoint, OpenNettyDimensions.Lighting.DimmerLevelSpeed, [expectedLevel, expectedSpeed]);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs, 0, "0")]
    [InlineData(OpenNettyProtocol.Scs, 1000, "5")]
    [InlineData(OpenNettyProtocol.Scs, 2500, "13")]
    [InlineData(OpenNettyProtocol.Zigbee, 10000, "50")]
    [InlineData(OpenNettyProtocol.Zigbee, 50000, "250")]
    public async Task SetBrightnessAsync_AdvancedControlWithTransition_ConvertsTransitionToFifthsOfSecond(
        OpenNettyProtocol protocol, int milliseconds, string expectedSpeed)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.AdvancedDimmingControl);

        await _controller.SetBrightnessAsync(endpoint, 40, TimeSpan.FromMilliseconds(milliseconds), Token);

        AssertDimensionSet(endpoint, OpenNettyDimensions.Lighting.DimmerLevelSpeed, ["140", expectedSpeed]);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task SetBrightnessAsync_ScsOrZigbeeAdvancedControlWithZeroLevel_SendsOffCommand(OpenNettyProtocol protocol)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.AdvancedDimmingControl);

        await _controller.SetBrightnessAsync(endpoint, 0, TimeSpan.FromSeconds(5), Token);

        AssertCommandExecuted(endpoint, OpenNettyCommands.Lighting.Off);
    }

    [Theory]
    [InlineData(0, "0", "10")]
    [InlineData(1, "1", "10")]
    [InlineData(50, "50", "10")]
    [InlineData(51, "51", "5")]
    [InlineData(60, "60", "6")]
    [InlineData(99, "99", "9")]
    [InlineData(100, "100", "10")]
    public async Task SetBrightnessAsync_NitooWithoutTransition_ComputesSpeedFromLevel(byte level, string expectedLevel, string expectedSpeed)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.AdvancedDimmingControl);

        await _controller.SetBrightnessAsync(endpoint, level, cancellationToken: Token);

        AssertDimensionSet(endpoint, OpenNettyDimensions.Lighting.DimmerLevelSpeed, [expectedLevel, expectedSpeed]);
    }

    [Theory]
    [InlineData(30, 1000, "5")]
    [InlineData(50, 1000, "5")]
    [InlineData(80, 2000, "10")]
    [InlineData(80, 300000, "1500")]
    public async Task SetBrightnessAsync_NitooWithTransition_UsesRequestedDuration(byte level, int milliseconds, string expectedSpeed)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.AdvancedDimmingControl);

        await _controller.SetBrightnessAsync(endpoint, level, TimeSpan.FromMilliseconds(milliseconds), Token);

        AssertDimensionSet(
            endpoint, OpenNettyDimensions.Lighting.DimmerLevelSpeed,
            [level.ToString(System.Globalization.CultureInfo.InvariantCulture), expectedSpeed]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 20)]
    [InlineData(29, 20)]
    [InlineData(30, 30)]
    [InlineData(39, 30)]
    [InlineData(40, 40)]
    [InlineData(49, 40)]
    [InlineData(50, 50)]
    [InlineData(59, 50)]
    [InlineData(60, 60)]
    [InlineData(69, 60)]
    [InlineData(70, 70)]
    [InlineData(79, 70)]
    [InlineData(80, 80)]
    [InlineData(89, 80)]
    [InlineData(90, 90)]
    [InlineData(99, 90)]
    [InlineData(100, 100)]
    public async Task SetBrightnessAsync_BasicControl_SendsClosestLightingCommand(byte level, int expectedBucket)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicDimmingControl);

        await _controller.SetBrightnessAsync(endpoint, level, cancellationToken: Token);

        AssertCommandExecuted(endpoint, GetLightingCommand(expectedBucket));
    }

    [Fact]
    public async Task SetBrightnessAsync_BothAdvancedAndBasicControl_PrefersAdvancedControl()
    {
        var endpoint = CreateEndpoint(
            OpenNettyProtocol.Scs, OpenNettyCapabilities.AdvancedDimmingControl, OpenNettyCapabilities.BasicDimmingControl);

        await _controller.SetBrightnessAsync(endpoint, 50, cancellationToken: Token);

        AssertSingleCall(ServiceMethod.SetDimension);
    }

    [Fact]
    public async Task SetDateTimeAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => _controller.SetDateTimeAsync(null!, DateTimeOffset.UnixEpoch, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task SetDateTimeAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.Uptime);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.SetDateTimeAsync(endpoint, DateTimeOffset.UnixEpoch, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Theory]
    [InlineData(2, "002")]
    [InlineData(0, "000")]
    [InlineData(12, "012")]
    public async Task SetDateTimeAsync_NonNegativeOffset_SetsDateTimeDimension(int offsetHours, string expectedOffset)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.DateTime);
        var date = new DateTimeOffset(2026, 3, 7, 4, 5, 6, TimeSpan.FromHours(offsetHours));

        await _controller.SetDateTimeAsync(endpoint, date, Token);

        AssertDimensionSet(endpoint, OpenNettyDimensions.Management.DateTime, ["04", "05", "06", expectedOffset, "06", "07", "03", "2026"]);
    }

    [Fact]
    public async Task SetDateTimeAsync_NegativeOffset_SetsDateTimeDimensionWithNegativeOffsetMarker()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.DateTime);
        var date = new DateTimeOffset(2026, 3, 7, 4, 5, 6, TimeSpan.FromHours(-5));

        await _controller.SetDateTimeAsync(endpoint, date, Token);

        AssertDimensionSet(endpoint, OpenNettyDimensions.Management.DateTime, ["04", "05", "06", "105", "06", "07", "03", "2026"]);
    }

    [Fact]
    public async Task SetPilotWireDerogationModeAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.SetPilotWireDerogationModeAsync(null!, OpenNettyModels.TemperatureControl.PilotWireMode.Eco, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, cancellationToken: Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task SetPilotWireDerogationModeAsync_UndefinedDuration_ThrowsInvalidEnumArgumentException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.PilotWireDerogation);

        var exception = await Assert.ThrowsAsync<InvalidEnumArgumentException>(
            () => _controller.SetPilotWireDerogationModeAsync(
                endpoint, OpenNettyModels.TemperatureControl.PilotWireMode.Eco, (OpenNettyModels.TemperatureControl.PilotWireDerogationDuration) 42, cancellationToken: Token).AsTask());

        Assert.Equal("duration", exception.ParamName);
        Assert.Empty(_service.Calls);
    }

    [Fact]
    public async Task SetPilotWireDerogationModeAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.PilotWireControl);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.SetPilotWireDerogationModeAsync(
                endpoint, OpenNettyModels.TemperatureControl.PilotWireMode.Eco, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, cancellationToken: Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public async Task SetPilotWireDerogationModeAsync_UndefinedMode_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.PilotWireDerogation);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _controller.SetPilotWireDerogationModeAsync(
                endpoint, (OpenNettyModels.TemperatureControl.PilotWireMode) 42, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, cancellationToken: Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.Comfort, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, "0")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, "1")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, "2")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.Eco, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, "3")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, "4")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.Comfort, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, "32")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.Eco, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, "35")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, "36")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.Comfort, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, "128")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, "130")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, "132")]
    public async Task SetPilotWireDerogationModeAsync_ModeAndDuration_AreEncodedInCommandParameter(
        OpenNettyModels.TemperatureControl.PilotWireMode mode, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration duration, string expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.PilotWireDerogation);

        await _controller.SetPilotWireDerogationModeAsync(endpoint, mode, duration, cancellationToken: Token);

        AssertCommandExecuted(
            endpoint, OpenNettyCommands.TemperatureControl.WirePilotDerogationMode.WithParameters([expected]), OpenNettyMode.Multicast);
    }

    [Theory]
    [InlineData(false, OpenNettyMode.Multicast)]
    [InlineData(true, null)]
    public async Task SetPilotWireDerogationModeAsync_ExcludeAssociatedDevices_ControlsTransmissionMode(bool exclude, OpenNettyMode? expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.PilotWireDerogation);
        var options = new OpenNettyModels.TemperatureControl.PilotWireTransmissionOptions { ExcludeAssociatedDevices = exclude };

        await _controller.SetPilotWireDerogationModeAsync(endpoint, OpenNettyModels.TemperatureControl.PilotWireMode.Eco, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, options, Token);

        Assert.Equal(expected, AssertSingleCall(ServiceMethod.ExecuteCommand).Mode);
    }

    [Fact]
    public async Task SetPilotWireSetpointModeAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.SetPilotWireSetpointModeAsync(null!, OpenNettyModels.TemperatureControl.PilotWireMode.Eco, cancellationToken: Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task SetPilotWireSetpointModeAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.PilotWireDerogation);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.SetPilotWireSetpointModeAsync(endpoint, OpenNettyModels.TemperatureControl.PilotWireMode.Eco, cancellationToken: Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public async Task SetPilotWireSetpointModeAsync_UndefinedMode_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.PilotWireControl);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _controller.SetPilotWireSetpointModeAsync(endpoint, (OpenNettyModels.TemperatureControl.PilotWireMode) 42, cancellationToken: Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.Comfort, "0")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne, "1")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo, "2")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.Eco, "3")]
    [InlineData(OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection, "4")]
    public async Task SetPilotWireSetpointModeAsync_Mode_IsEncodedInCommandParameter(OpenNettyModels.TemperatureControl.PilotWireMode mode, string expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.PilotWireControl);

        await _controller.SetPilotWireSetpointModeAsync(endpoint, mode, cancellationToken: Token);

        AssertCommandExecuted(
            endpoint, OpenNettyCommands.TemperatureControl.WirePilotSetpointMode.WithParameters([expected]), OpenNettyMode.Multicast);
    }

    [Theory]
    [InlineData(false, OpenNettyMode.Multicast)]
    [InlineData(true, null)]
    public async Task SetPilotWireSetpointModeAsync_ExcludeAssociatedDevices_ControlsTransmissionMode(bool exclude, OpenNettyMode? expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.PilotWireControl);
        var options = new OpenNettyModels.TemperatureControl.PilotWireTransmissionOptions { ExcludeAssociatedDevices = exclude };

        await _controller.SetPilotWireSetpointModeAsync(endpoint, OpenNettyModels.TemperatureControl.PilotWireMode.Eco, options, Token);

        Assert.Equal(expected, AssertSingleCall(ServiceMethod.ExecuteCommand).Mode);
    }

    [Fact]
    public async Task SetShutterPositionAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.SetShutterPositionAsync(null!, 50, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task SetShutterPositionAsync_PositionAboveOneHundred_ThrowsArgumentOutOfRangeException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.AdvancedShutterState);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _controller.SetShutterPositionAsync(endpoint, 101, Token).AsTask());

        Assert.Equal("position", exception.ParamName);
        Assert.Empty(_service.Calls);
    }

    [Fact]
    public async Task SetShutterPositionAsync_EndpointWithoutAdvancedShutterState_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicShutterControl, OpenNettyCapabilities.AdvancedShutterControl);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.SetShutterPositionAsync(endpoint, 50, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs, 0, "0", "0")]
    [InlineData(OpenNettyProtocol.Scs, 100, "0", "100")]
    [InlineData(OpenNettyProtocol.Nitoo, 42, "0", "42")]
    public async Task SetShutterPositionAsync_NonZigbeeEndpoint_SetsGoToLevelDimensionWithLeadingZero(
        OpenNettyProtocol protocol, byte position, string first, string second)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.AdvancedShutterState);

        await _controller.SetShutterPositionAsync(endpoint, position, Token);

        AssertDimensionSet(endpoint, OpenNettyDimensions.Automation.ShutterGoToLevel, [first, second]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(100)]
    public async Task SetShutterPositionAsync_ZigbeeEndpoint_SetsGoToLevelDimensionWithPositionOnly(byte position)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.AdvancedShutterState);

        await _controller.SetShutterPositionAsync(endpoint, position, Token);

        AssertDimensionSet(endpoint, OpenNettyDimensions.Automation.ShutterGoToLevel, [position.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
    }

    [Fact]
    public async Task SetWaterHeaterSetpointModeAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.SetWaterHeaterSetpointModeAsync(null!, OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task SetWaterHeaterSetpointModeAsync_EndpointWithoutCapability_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.PilotWireControl);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.SetWaterHeaterSetpointModeAsync(endpoint, OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOff, "0")]
    [InlineData(OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn, "1")]
    [InlineData(OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic, "2")]
    public async Task SetWaterHeaterSetpointModeAsync_Mode_SetsWaterHeatingModeDimension(OpenNettyModels.TemperatureControl.WaterHeaterMode mode, string expected)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.WaterHeating);

        await _controller.SetWaterHeaterSetpointModeAsync(endpoint, mode, Token);

        AssertDimensionSet(endpoint, OpenNettyDimensions.TemperatureControl.WaterHeatingMode, [expected]);
    }

    [Fact]
    public async Task SetWaterHeaterSetpointModeAsync_UndefinedMode_ThrowsInvalidDataException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.WaterHeating);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _controller.SetWaterHeaterSetpointModeAsync(endpoint, (OpenNettyModels.TemperatureControl.WaterHeaterMode) 42, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    public static TheoryData<string> ShutterControlOperations => ["MoveUp", "MoveDown", "Stop"];

    [Theory]
    [MemberData(nameof(ShutterControlOperations))]
    public async Task ShutterControl_NullEndpoint_ThrowsArgumentNullException(string operation)
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => ControlShutterAsync(operation, null!));

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(ShutterControlOperations))]
    public async Task ShutterControl_EndpointWithoutBasicShutterControl_ThrowsInvalidOperationException(string operation)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.AdvancedShutterControl);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ControlShutterAsync(operation, endpoint));

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [MemberData(nameof(ShutterControlOperations))]
    public async Task ShutterControl_UnsupportedFunctionType_ThrowsInvalidOperationException(string operation)
    {
        var endpoint = WithSetting(
            CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.BasicShutterControl),
            OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.LightActuator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ControlShutterAsync(operation, endpoint));

        Assert.Equal(SR.GetResourceString(SR.ID0099), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData("MoveUp", OpenNettyProtocol.Nitoo)]
    [InlineData("MoveUp", OpenNettyProtocol.Scs)]
    [InlineData("MoveUp", OpenNettyProtocol.Zigbee)]
    [InlineData("MoveDown", OpenNettyProtocol.Nitoo)]
    [InlineData("MoveDown", OpenNettyProtocol.Scs)]
    [InlineData("MoveDown", OpenNettyProtocol.Zigbee)]
    [InlineData("Stop", OpenNettyProtocol.Nitoo)]
    [InlineData("Stop", OpenNettyProtocol.Scs)]
    [InlineData("Stop", OpenNettyProtocol.Zigbee)]
    public async Task ShutterControl_SupportedEndpoint_SendsMatchingAutomationCommand(string operation, OpenNettyProtocol protocol)
    {
        var endpoint = WithSetting(
            CreateEndpoint(protocol, OpenNettyCapabilities.BasicShutterControl),
            OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.AutomationActuator);

        await ControlShutterAsync(operation, endpoint);

        AssertCommandExecuted(endpoint, operation switch
        {
            "MoveUp"   => OpenNettyCommands.Automation.Up,
            "MoveDown" => OpenNettyCommands.Automation.Down,
            _          => OpenNettyCommands.Automation.Stop
        });
    }

    [Fact]
    public async Task SwitchOffAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.SwitchOffAsync(null!, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task SwitchOnAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.SwitchOnAsync(null!, cancellationToken: Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task SwitchOnAsync_TransitionLongerThanFiftySeconds_ThrowsArgumentOutOfRangeException(OpenNettyProtocol protocol)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.BasicDimmingControl);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _controller.SwitchOnAsync(endpoint, TimeSpan.FromSeconds(51), Token).AsTask());

        Assert.Equal("transition", exception.ParamName);
        Assert.Empty(_service.Calls);
    }

    [Fact]
    public async Task SwitchOnAsync_TransitionWithoutBasicDimmingControl_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchControl);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.SwitchOnAsync(endpoint, TimeSpan.FromSeconds(5), Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs, 50)]
    [InlineData(OpenNettyProtocol.Zigbee, 1)]
    [InlineData(OpenNettyProtocol.Nitoo, 120)]
    public async Task SwitchOnAsync_ValidTransition_SendsOnCommand(OpenNettyProtocol protocol, int seconds)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.BasicDimmingControl);

        await _controller.SwitchOnAsync(endpoint, TimeSpan.FromSeconds(seconds), Token);

        AssertCommandExecuted(endpoint, OpenNettyCommands.Lighting.On);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Nitoo)]
    public async Task SwitchOnAsync_NonZigbeeFailure_IsRethrown(OpenNettyProtocol protocol)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.OnOffSwitchState);
        _service.CommandFailures.Enqueue(new OpenNettyException(OpenNettyErrorCode.NoStatusReportReceived, "No report."));

        var exception = await Assert.ThrowsAsync<OpenNettyException>(() => _controller.SwitchOnAsync(endpoint, cancellationToken: Token).AsTask());

        Assert.Equal(OpenNettyErrorCode.NoStatusReportReceived, exception.ErrorCode);
        Assert.Single(_service.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SwitchOnOff_EndpointWithoutSwitchControl_ThrowsInvalidOperationException(bool on)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchState);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => SwitchAsync(on, endpoint));

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SwitchOnOff_UnsupportedFunctionType_ThrowsInvalidOperationException(bool on)
    {
        var endpoint = WithSetting(
            CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchControl),
            OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.AutomationActuator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => SwitchAsync(on, endpoint));

        Assert.Equal(SR.GetResourceString(SR.ID0099), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Nitoo, true)]
    [InlineData(OpenNettyProtocol.Nitoo, false)]
    [InlineData(OpenNettyProtocol.Scs, true)]
    [InlineData(OpenNettyProtocol.Scs, false)]
    [InlineData(OpenNettyProtocol.Zigbee, true)]
    [InlineData(OpenNettyProtocol.Zigbee, false)]
    public async Task SwitchOnOff_LightActuatorFunctionType_SendsCommand(OpenNettyProtocol protocol, bool on)
    {
        var endpoint = WithSetting(
            CreateEndpoint(protocol, OpenNettyCapabilities.OnOffSwitchControl),
            OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.LightActuator);

        await SwitchAsync(on, endpoint);

        AssertCommandExecuted(endpoint, on ? OpenNettyCommands.Lighting.On : OpenNettyCommands.Lighting.Off);
    }

    [Theory]
    [InlineData(true, OpenNettyModels.Lighting.SwitchState.On)]
    [InlineData(false, OpenNettyModels.Lighting.SwitchState.Off)]
    public async Task SwitchOnOff_ZigbeeEndpointAlreadyInRequestedState_SkipsCommandWhenStateQueryEnabled(bool on, OpenNettyModels.Lighting.SwitchState current)
    {
        var endpoint = WithSetting(CreateZigbeeSwitch(), OpenNettySettings.OnOffSwitchStateQuery, "True");
        _service.StatusReply = GetLightingCommand(current is OpenNettyModels.Lighting.SwitchState.On ? 100 : 0);

        await SwitchAsync(on, endpoint);

        Assert.Equal([ServiceMethod.GetStatus], _service.Calls.Select(static call => call.Method));
    }

    [Theory]
    [InlineData(true, OpenNettyModels.Lighting.SwitchState.Off)]
    [InlineData(false, OpenNettyModels.Lighting.SwitchState.On)]
    public async Task SwitchOnOff_ZigbeeEndpointInOppositeState_SendsCommandAfterStateQuery(bool on, OpenNettyModels.Lighting.SwitchState current)
    {
        var endpoint = WithSetting(CreateZigbeeSwitch(), OpenNettySettings.OnOffSwitchStateQuery, "True");
        _service.StatusReply = GetLightingCommand(current is OpenNettyModels.Lighting.SwitchState.On ? 100 : 0);

        await SwitchAsync(on, endpoint);

        Assert.Equal([ServiceMethod.GetStatus, ServiceMethod.ExecuteCommand], _service.Calls.Select(static call => call.Method));
        Assert.Equal(on ? OpenNettyCommands.Lighting.On : OpenNettyCommands.Lighting.Off, _service.Calls[1].Command);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SwitchOnOff_ZigbeeEndpointWithoutStateQuery_SendsCommandWithoutQueryingState(bool on)
    {
        var endpoint = CreateZigbeeSwitch();

        await SwitchAsync(on, endpoint);

        AssertCommandExecuted(endpoint, on ? OpenNettyCommands.Lighting.On : OpenNettyCommands.Lighting.Off);
    }

    [Theory]
    [InlineData(true, OpenNettyModels.Lighting.SwitchState.On)]
    [InlineData(false, OpenNettyModels.Lighting.SwitchState.Off)]
    public async Task SwitchOnOff_ZigbeeToggleEndpointAlreadyInRequestedState_SkipsCommand(bool on, OpenNettyModels.Lighting.SwitchState current)
    {
        var endpoint = WithToggleCommandType(CreateZigbeeSwitch());
        _service.StatusReply = GetLightingCommand(current is OpenNettyModels.Lighting.SwitchState.On ? 100 : 0);

        await SwitchAsync(on, endpoint);

        Assert.Equal([ServiceMethod.GetStatus], _service.Calls.Select(static call => call.Method));
    }

    [Theory]
    [InlineData(true, OpenNettyModels.Lighting.SwitchState.Off)]
    [InlineData(false, OpenNettyModels.Lighting.SwitchState.On)]
    public async Task SwitchOnOff_ZigbeeToggleEndpointInOppositeState_SendsToggleWithoutUnsafeRetransmissions(bool on, OpenNettyModels.Lighting.SwitchState current)
    {
        var endpoint = WithToggleCommandType(CreateZigbeeSwitch());
        _service.StatusReply = GetLightingCommand(current is OpenNettyModels.Lighting.SwitchState.On ? 100 : 0);

        await SwitchAsync(on, endpoint);

        var call = _service.Calls[^1];
        Assert.Equal(ServiceMethod.ExecuteCommand, call.Method);
        Assert.Equal(OpenNettyCommands.Lighting.Toggle, call.Command);
        Assert.Equal(GetDefaultOptions(endpoint) with { DisallowUnsafeRetransmissions = true }, call.Options);
    }

    [Theory]
    [InlineData(true, OpenNettyModels.Lighting.SwitchState.On)]
    [InlineData(false, OpenNettyModels.Lighting.SwitchState.Off)]
    public async Task SwitchOnOff_ZigbeeMissingStatusReportWhenAlreadyInRequestedState_IgnoresException(bool on, OpenNettyModels.Lighting.SwitchState current)
    {
        var endpoint = CreateZigbeeSwitch();
        _service.CommandFailures.Enqueue(new OpenNettyException(OpenNettyErrorCode.NoStatusReportReceived, "No report."));
        _service.StatusReply = GetLightingCommand(current is OpenNettyModels.Lighting.SwitchState.On ? 100 : 0);

        await SwitchAsync(on, endpoint);

        Assert.Equal([ServiceMethod.ExecuteCommand, ServiceMethod.GetStatus], _service.Calls.Select(static call => call.Method));
    }

    [Theory]
    [InlineData(true, OpenNettyModels.Lighting.SwitchState.Off)]
    [InlineData(false, OpenNettyModels.Lighting.SwitchState.On)]
    public async Task SwitchOnOff_ZigbeeMissingStatusReportWhenInOppositeState_RethrowsException(bool on, OpenNettyModels.Lighting.SwitchState current)
    {
        var endpoint = CreateZigbeeSwitch();
        _service.CommandFailures.Enqueue(new OpenNettyException(OpenNettyErrorCode.NoStatusReportReceived, "No report."));
        _service.StatusReply = GetLightingCommand(current is OpenNettyModels.Lighting.SwitchState.On ? 100 : 0);

        var exception = await Assert.ThrowsAsync<OpenNettyException>(() => SwitchAsync(on, endpoint));

        Assert.Equal(OpenNettyErrorCode.NoStatusReportReceived, exception.ErrorCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SwitchOnOff_ZigbeeOtherOpenNettyException_IsRethrownWithoutQueryingState(bool on)
    {
        var endpoint = CreateZigbeeSwitch();
        _service.CommandFailures.Enqueue(new OpenNettyException(OpenNettyErrorCode.GatewayBusy, "Busy."));

        var exception = await Assert.ThrowsAsync<OpenNettyException>(() => SwitchAsync(on, endpoint));

        Assert.Equal(OpenNettyErrorCode.GatewayBusy, exception.ErrorCode);
        Assert.Equal([ServiceMethod.ExecuteCommand], _service.Calls.Select(static call => call.Method));
    }

    [Fact]
    public async Task SwitchOnTimedAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.SwitchOnTimedAsync(null!, cancellationToken: Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task SwitchOnTimedAsync_UndefinedDuration_ThrowsInvalidEnumArgumentException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchControl);

        var exception = await Assert.ThrowsAsync<InvalidEnumArgumentException>(
            () => _controller.SwitchOnTimedAsync(endpoint, (OpenNettyModels.Lighting.TimedOnDuration) 42, Token).AsTask());

        Assert.Equal("duration", exception.ParamName);
        Assert.Empty(_service.Calls);
    }

    [Fact]
    public async Task SwitchOnTimedAsync_EndpointWithoutSwitchControl_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.SwitchOnTimedAsync(endpoint, OpenNettyModels.Lighting.TimedOnDuration.OneMinute, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public async Task SwitchOnTimedAsync_UnsupportedFunctionType_ThrowsInvalidOperationException()
    {
        var endpoint = WithSetting(
            CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchControl),
            OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.ContactState);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.SwitchOnTimedAsync(endpoint, OpenNettyModels.Lighting.TimedOnDuration.OneMinute, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0099), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyModels.Lighting.TimedOnDuration.FiveHundredMilliseconds)]
    [InlineData(OpenNettyModels.Lighting.TimedOnDuration.ThirtySeconds)]
    [InlineData(OpenNettyModels.Lighting.TimedOnDuration.OneMinute)]
    [InlineData(OpenNettyModels.Lighting.TimedOnDuration.TwoMinutes)]
    [InlineData(OpenNettyModels.Lighting.TimedOnDuration.ThreeMinutes)]
    [InlineData(OpenNettyModels.Lighting.TimedOnDuration.FourMinutes)]
    [InlineData(OpenNettyModels.Lighting.TimedOnDuration.FiveMinutes)]
    [InlineData(OpenNettyModels.Lighting.TimedOnDuration.FifteenMinutes)]
    public async Task SwitchOnTimedAsync_KnownDuration_SendsMatchingTimedCommand(OpenNettyModels.Lighting.TimedOnDuration duration)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchControl);

        await _controller.SwitchOnTimedAsync(endpoint, duration, Token);

        AssertCommandExecuted(endpoint, GetTimedOnCommand(duration));
    }

    [Fact]
    public async Task SwitchOnTimedAsync_NoDuration_SendsRegularOnCommand()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchControl);

        await _controller.SwitchOnTimedAsync(endpoint, cancellationToken: Token);

        AssertCommandExecuted(endpoint, OpenNettyCommands.Lighting.On);
    }

    [Fact]
    public async Task SwitchOnTimedAsync_MissingStatusReportWhenSwitchedOn_IgnoresException()
    {
        var endpoint = CreateZigbeeSwitch();
        _service.CommandFailures.Enqueue(new OpenNettyException(OpenNettyErrorCode.NoStatusReportReceived, "No report."));
        _service.StatusReply = OpenNettyCommands.Lighting.On;

        await _controller.SwitchOnTimedAsync(endpoint, OpenNettyModels.Lighting.TimedOnDuration.FiveMinutes, Token);

        Assert.Equal([ServiceMethod.ExecuteCommand, ServiceMethod.GetStatus], _service.Calls.Select(static call => call.Method));
    }

    [Fact]
    public async Task SwitchOnTimedAsync_MissingStatusReportWhenSwitchedOff_RethrowsException()
    {
        var endpoint = CreateZigbeeSwitch();
        _service.CommandFailures.Enqueue(new OpenNettyException(OpenNettyErrorCode.NoStatusReportReceived, "No report."));
        _service.StatusReply = OpenNettyCommands.Lighting.Off;

        var exception = await Assert.ThrowsAsync<OpenNettyException>(
            () => _controller.SwitchOnTimedAsync(endpoint, OpenNettyModels.Lighting.TimedOnDuration.FiveMinutes, Token).AsTask());

        Assert.Equal(OpenNettyErrorCode.NoStatusReportReceived, exception.ErrorCode);
    }

    [Fact]
    public async Task SwitchOnTimedAsync_OtherOpenNettyException_IsRethrown()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchControl);
        _service.CommandFailures.Enqueue(new OpenNettyException(OpenNettyErrorCode.InvalidFrame, "Invalid."));

        var exception = await Assert.ThrowsAsync<OpenNettyException>(
            () => _controller.SwitchOnTimedAsync(endpoint, OpenNettyModels.Lighting.TimedOnDuration.OneMinute, Token).AsTask());

        Assert.Equal(OpenNettyErrorCode.InvalidFrame, exception.ErrorCode);
        Assert.Single(_service.Calls);
    }

    [Fact]
    public async Task ToggleAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _controller.ToggleAsync(null!, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task ToggleAsync_EndpointWithoutSwitchControl_ThrowsInvalidOperationException()
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.OnOffSwitchState);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.ToggleAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
    }

    [Fact]
    public async Task ToggleAsync_UnsupportedFunctionType_ThrowsInvalidOperationException()
    {
        var endpoint = WithSetting(
            CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.OnOffSwitchControl),
            OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.AutomationActuator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.ToggleAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0099), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Scs)]
    public async Task ToggleAsync_NitooOrScsEndpointWithoutStateCapability_ThrowsInvalidOperationException(OpenNettyProtocol protocol)
    {
        var endpoint = CreateEndpoint(protocol, OpenNettyCapabilities.OnOffSwitchControl);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.ToggleAsync(endpoint, Token).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData(OpenNettyModels.Lighting.SwitchState.On, true)]
    [InlineData(OpenNettyModels.Lighting.SwitchState.Off, false)]
    public async Task ToggleAsync_ScsEndpoint_SendsCommandOppositeToCurrentState(OpenNettyModels.Lighting.SwitchState current, bool expectOff)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs, OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.OnOffSwitchState);
        _service.StatusReply = GetLightingCommand(current is OpenNettyModels.Lighting.SwitchState.On ? 100 : 0);

        await _controller.ToggleAsync(endpoint, Token);

        var call = _service.Calls[^1];
        Assert.Equal(ServiceMethod.ExecuteCommand, call.Method);
        Assert.Equal(expectOff ? OpenNettyCommands.Lighting.Off : OpenNettyCommands.Lighting.On, call.Command);
        AssertCommonArguments(call, endpoint);
    }

    [Theory]
    [InlineData("128", true)]
    [InlineData("0", false)]
    public async Task ToggleAsync_NitooEndpoint_UsesUnitDescriptionToDetermineCurrentState(string value, bool expectOff)
    {
        var endpoint = CreateEndpoint(
            OpenNettyProtocol.Nitoo, OpenNettyCapabilities.OnOffSwitchControl,
            OpenNettyCapabilities.OnOffSwitchState, OpenNettyCapabilities.UnitDescription);
        _service.DimensionValues[OpenNettyDimensions.Diagnostics.UnitDescription] = ["129", value];

        await _controller.ToggleAsync(endpoint, Token);

        Assert.Equal(expectOff ? OpenNettyCommands.Lighting.Off : OpenNettyCommands.Lighting.On, _service.Calls[^1].Command);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToggleAsync_ZigbeeEndpoint_SendsToggleCommandWithoutUnsafeRetransmissions(bool withStateCapability)
    {
        var endpoint = withStateCapability
            ? CreateZigbeeSwitch()
            : CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.OnOffSwitchControl);

        await _controller.ToggleAsync(endpoint, Token);

        AssertCommandExecuted(
            endpoint, OpenNettyCommands.Lighting.Toggle, options: GetDefaultOptions(endpoint) with { DisallowUnsafeRetransmissions = true });
    }

    [Fact]
    public async Task UnbindZigbeeEndpointAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => _controller.UnbindZigbeeEndpointAsync(null!, Token).AsTask());

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ZigbeeBinding_EndpointWithoutCapability_ThrowsInvalidOperationException(bool bind)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.ZigbeeNetworkManagement);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => BindOrUnbindAsync(_controller, bind, endpoint));

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ZigbeeBinding_EndpointWithoutGateway_ThrowsInvalidOperationException(bool bind)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.ZigbeeBinding) with { Gateway = null };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => BindOrUnbindAsync(_controller, bind, endpoint));

        Assert.Equal(SR.FormatID2023(endpoint.Name), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData(true, 1, 2, 2)]
    [InlineData(true, 1, 0, 9)]
    [InlineData(true, 0, 99, 99)]
    [InlineData(false, 1, 2, 2)]
    [InlineData(false, 1, 1, 9)]
    [InlineData(false, 0, 9, 9)]
    public async Task ZigbeeBinding_OutdatedGatewayFirmware_ThrowsInvalidOperationException(bool bind, int major, int minor, int build)
    {
        var controller = new FixedGatewayVersionController(_manager, _service, new Version(major, minor, build));
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.ZigbeeBinding);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => BindOrUnbindAsync(controller, bind, endpoint));

        Assert.Equal(SR.FormatID0131("1.2.3", endpoint.Gateway, new Version(major, minor, build).ToString()), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData(true, 1, 2, 3)]
    [InlineData(true, 1, 10, 0)]
    [InlineData(true, 2, 0, 0)]
    [InlineData(false, 1, 2, 3)]
    [InlineData(false, 1, 10, 0)]
    [InlineData(false, 2, 0, 0)]
    public async Task ZigbeeBinding_SupportedGatewayFirmware_SendsBindingCommand(bool bind, int major, int minor, int build)
    {
        var controller = new FixedGatewayVersionController(_manager, _service, new Version(major, minor, build));
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.ZigbeeBinding);

        await BindOrUnbindAsync(controller, bind, endpoint);

        AssertCommandExecuted(
            endpoint, bind ? OpenNettyCommands.ScenariosPlus.BindingRequest : OpenNettyCommands.ScenariosPlus.UnbindingRequest);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ZigbeeBinding_GatewayFirmwareResolvedFromGatewayEndpoint_QueriesFirmwareBeforeSendingCommand(bool bind)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.ZigbeeBinding);
        var gateway = GetGateway(OpenNettyProtocol.Zigbee);
        var gatewayEndpoint = OpenNettyEndpoint.Create(gateway.Device.GetUnit(0), "Gateway unit");
        _manager.FindEndpointsByDeviceAsync(gateway.Device, Arg.Any<CancellationToken>())
            .Returns(_ => EnumerateAsync(gatewayEndpoint));
        _service.DimensionValues[OpenNettyDimensions.Management.FirmwareVersion] = ["1", "2", "3"];

        await BindOrUnbindAsync(_controller, bind, endpoint);

        Assert.Equal([ServiceMethod.GetDimension, ServiceMethod.ExecuteCommand], _service.Calls.Select(static call => call.Method));
        Assert.Equal(OpenNettyDimensions.Management.FirmwareVersion, _service.Calls[0].Dimension);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ZigbeeBinding_OldGatewayFirmwareResolvedFromGatewayEndpoint_DoesNotSendCommand(bool bind)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.ZigbeeBinding);
        var gateway = GetGateway(OpenNettyProtocol.Zigbee);
        var gatewayEndpoint = OpenNettyEndpoint.Create(gateway.Device.GetUnit(0), "Gateway unit");
        _manager.FindEndpointsByDeviceAsync(gateway.Device, Arg.Any<CancellationToken>())
            .Returns(_ => EnumerateAsync(gatewayEndpoint));
        _service.DimensionValues[OpenNettyDimensions.Management.FirmwareVersion] = ["1", "1", "0"];

        await Assert.ThrowsAsync<InvalidOperationException>(() => BindOrUnbindAsync(_controller, bind, endpoint));

        Assert.Equal([ServiceMethod.GetDimension], _service.Calls.Select(static call => call.Method));
    }

    public static TheoryData<string> SimpleDiagnosticOperations => ["EraseAddress", "ResetMemoryData", "LockActuator", "UnlockActuator"];

    [Theory]
    [MemberData(nameof(SimpleDiagnosticOperations))]
    public async Task SimpleDiagnosticOperation_NullEndpoint_ThrowsArgumentNullException(string operation)
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => ExecuteSimpleDiagnosticOperationAsync(operation, null!));

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(SimpleDiagnosticOperations))]
    public async Task SimpleDiagnosticOperation_EndpointWithoutCapability_ThrowsInvalidOperationException(string operation)
    {
        var endpoint = CreateEndpoint(OpenNettyProtocol.Nitoo, OpenNettyCapabilities.OutgoingCommunication);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteSimpleDiagnosticOperationAsync(operation, endpoint));

        Assert.Equal(SR.GetResourceString(SR.ID0069), exception.Message);
        Assert.Empty(_service.Calls);
    }

    [Theory]
    [InlineData("EraseAddress", OpenNettyProtocol.Nitoo)]
    [InlineData("EraseAddress", OpenNettyProtocol.Zigbee)]
    [InlineData("ResetMemoryData", OpenNettyProtocol.Nitoo)]
    [InlineData("ResetMemoryData", OpenNettyProtocol.Zigbee)]
    [InlineData("LockActuator", OpenNettyProtocol.Nitoo)]
    [InlineData("LockActuator", OpenNettyProtocol.Scs)]
    [InlineData("UnlockActuator", OpenNettyProtocol.Nitoo)]
    [InlineData("UnlockActuator", OpenNettyProtocol.Scs)]
    public async Task SimpleDiagnosticOperation_SupportedEndpoint_SendsMatchingCommand(string operation, OpenNettyProtocol protocol)
    {
        var endpoint = CreateEndpoint(protocol, GetSimpleDiagnosticCapability(operation));

        await ExecuteSimpleDiagnosticOperationAsync(operation, endpoint);

        var (command, mode) = operation switch
        {
            "EraseAddress"    => (OpenNettyCommands.Diagnostics.AddressErase, OpenNettyMode.Broadcast),
            "ResetMemoryData" => (OpenNettyCommands.Diagnostics.MemoryReset, (OpenNettyMode?) null),
            "LockActuator"    => (OpenNettyCommands.ActuatorsControl.Disable, null),
            _                 => (OpenNettyCommands.ActuatorsControl.Enable, null)
        };
        AssertCommandExecuted(endpoint, command, mode);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public void GetTransmissionOptions_NoOverride_ReturnsGatewayDefaults(OpenNettyProtocol protocol)
    {
        var controller = new ExposedController(_manager, _service);
        var endpoint = CreateEndpoint(protocol);

        var options = controller.GetOptions(endpoint);

        Assert.Equal(GetDefaultOptions(endpoint), options);
    }

    [Theory]
    [InlineData("True", false)]
    [InlineData("False", true)]
    public void GetTransmissionOptions_ActionValidationSetting_OverridesGatewayDefault(string value, bool expected)
    {
        var controller = new ExposedController(_manager, _service);
        var endpoint = WithSetting(CreateEndpoint(OpenNettyProtocol.Scs), OpenNettySettings.ActionValidation, value);

        var options = controller.GetOptions(endpoint);

        Assert.Equal(expected, options.DisableActionValidation);
        Assert.Equal(GetDefaultOptions(endpoint).DisableStatusReportValidation, options.DisableStatusReportValidation);
    }

    [Theory]
    [InlineData("True", false)]
    [InlineData("False", true)]
    public void GetTransmissionOptions_StatusReportValidationSetting_OverridesGatewayDefault(string value, bool expected)
    {
        var controller = new ExposedController(_manager, _service);
        var endpoint = WithSetting(CreateEndpoint(OpenNettyProtocol.Zigbee), OpenNettySettings.StatusReportValidation, value);

        var options = controller.GetOptions(endpoint);

        Assert.Equal(expected, options.DisableStatusReportValidation);
        Assert.Equal(GetDefaultOptions(endpoint).DisableActionValidation, options.DisableActionValidation);
    }

    [Fact]
    public void GetTransmissionOptions_NullEndpoint_ThrowsArgumentNullException()
    {
        var controller = new ExposedController(_manager, _service);

        var exception = Assert.Throws<ArgumentNullException>(() => controller.GetOptions(null!));

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public void GetTransmissionOptions_EndpointWithoutGateway_ThrowsInvalidOperationException()
    {
        var controller = new ExposedController(_manager, _service);
        var endpoint = CreateEndpoint(OpenNettyProtocol.Scs) with { Gateway = null };

        var exception = Assert.Throws<InvalidOperationException>(() => controller.GetOptions(endpoint));

        Assert.Equal(SR.FormatID2023(endpoint.Name), exception.Message);
    }

    [Fact]
    public async Task GetGatewayFirmwareVersionAsync_NullGateway_ThrowsArgumentNullException()
    {
        var controller = new ExposedController(_manager, _service);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => controller.GetGatewayVersionAsync(null!, Token).AsTask());

        Assert.Equal("gateway", exception.ParamName);
    }

    [Fact]
    public async Task GetGatewayFirmwareVersionAsync_NoMatchingEndpoint_ThrowsInvalidOperationException()
    {
        var controller = new ExposedController(_manager, _service);
        var gateway = GetGateway(OpenNettyProtocol.Zigbee);
        _manager.FindEndpointsByDeviceAsync(gateway.Device, Arg.Any<CancellationToken>())
            .Returns(_ => EnumerateAsync<OpenNettyEndpoint>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.GetGatewayVersionAsync(gateway, Token).AsTask());

        Assert.Equal(SR.FormatID0130(gateway.Device.Name), exception.Message);
    }

    [Fact]
    public async Task GetGatewayFirmwareVersionAsync_GatewayEndpointWithFirmwareCapability_ReturnsFirmwareVersion()
    {
        var controller = new ExposedController(_manager, _service);
        var gateway = GetGateway(OpenNettyProtocol.Zigbee);
        var otherUnit = OpenNettyEndpoint.Create(gateway.Device.GetUnit(0), "Gateway unit");
        _manager.FindEndpointsByDeviceAsync(gateway.Device, Arg.Any<CancellationToken>())
            .Returns(_ => EnumerateAsync(otherUnit));
        _service.DimensionValues[OpenNettyDimensions.Management.FirmwareVersion] = ["1", "2", "3"];

        var version = await controller.GetGatewayVersionAsync(gateway, Token);

        Assert.Equal(new Version(1, 2, 3), version);
    }

    private Task SwitchAsync(bool on, OpenNettyEndpoint endpoint) => on
        ? _controller.SwitchOnAsync(endpoint, cancellationToken: Token).AsTask()
        : _controller.SwitchOffAsync(endpoint, Token).AsTask();

    private static OpenNettyEndpoint CreateZigbeeSwitch()
        => CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.OnOffSwitchControl, OpenNettyCapabilities.OnOffSwitchState);

    private static OpenNettyEndpoint WithToggleCommandType(OpenNettyEndpoint endpoint)
        => WithSetting(endpoint, OpenNettySettings.OnOffCommandType, OpenNettySettings.OnOffCommandTypes.Toggle);

    private async Task<bool> ValidateZigbeeStatusAsync(OpenNettyEndpoint endpoint, params OpenNettyMessage[] messages)
    {
        _service.StatusReply = OpenNettyCommands.Lighting.On;
        await _controller.GetSwitchStateAsync(endpoint, Token);

        var validation = Assert.Single(_service.Calls).Options!.CustomValidationDelegate!;

        return await validation(CreateObservable(messages), Token);
    }

    private static OpenNettyCommand GetLightingCommand(int level) => level switch
    {
        0   => OpenNettyCommands.Lighting.Off,
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

    private static OpenNettyCommand     GetTimedOnCommand(OpenNettyModels.Lighting.TimedOnDuration duration) => duration switch
    {
            OpenNettyModels.Lighting.TimedOnDuration.FiveHundredMilliseconds => OpenNettyCommands.Lighting.TimedOn500Milliseconds,
            OpenNettyModels.Lighting.TimedOnDuration.ThirtySeconds           => OpenNettyCommands.Lighting.TimedOn30Seconds,
            OpenNettyModels.Lighting.TimedOnDuration.OneMinute               => OpenNettyCommands.Lighting.TimedOn1Minute,
            OpenNettyModels.Lighting.TimedOnDuration.TwoMinutes              => OpenNettyCommands.Lighting.TimedOn2Minutes,
            OpenNettyModels.Lighting.TimedOnDuration.ThreeMinutes            => OpenNettyCommands.Lighting.TimedOn3Minutes,
            OpenNettyModels.Lighting.TimedOnDuration.FourMinutes             => OpenNettyCommands.Lighting.TimedOn4Minutes,
            OpenNettyModels.Lighting.TimedOnDuration.FiveMinutes             => OpenNettyCommands.Lighting.TimedOn5Minutes,
            OpenNettyModels.Lighting.TimedOnDuration.FifteenMinutes          => OpenNettyCommands.Lighting.TimedOn15Minutes,

        _ => throw new ArgumentOutOfRangeException(nameof(duration))
    };

    private Task ControlShutterAsync(string operation, OpenNettyEndpoint endpoint) => operation switch
    {
        "MoveUp"   => _controller.MoveShutterUpAsync(endpoint, Token).AsTask(),
        "MoveDown" => _controller.MoveShutterDownAsync(endpoint, Token).AsTask(),
        "Stop"     => _controller.StopShutterAsync(endpoint, Token).AsTask(),

        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private async Task<bool> ValidateZigbeeShutterStatusAsync(OpenNettyEndpoint endpoint, params OpenNettyMessage[] messages)
    {
        _service.StatusReply = OpenNettyCommands.Automation.Up;
        await _controller.GetShutterStateAsync(endpoint, Token);

        var validation = Assert.Single(_service.Calls).Options!.CustomValidationDelegate!;

        return await validation(CreateObservable(messages), Token);
    }

    private static OpenNettyCommand     GetPressureScenarioPlusCommand(OpenNettyModels.ScenariosPlus.PressureScenarioType type) => type switch
    {
            OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure           => OpenNettyCommands.ScenariosPlus.ShortPressure,
            OpenNettyModels.ScenariosPlus.PressureScenarioType.StartOfExtendedPressure => OpenNettyCommands.ScenariosPlus.StartOfExtendedPressure,
            OpenNettyModels.ScenariosPlus.PressureScenarioType.ExtendedPressure        => OpenNettyCommands.ScenariosPlus.ExtendedPressure,
            OpenNettyModels.ScenariosPlus.PressureScenarioType.EndOfExtendedPressure   => OpenNettyCommands.ScenariosPlus.EndOfExtendedPressure,

        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private Task ExecutePilotWireOperationAsync(string operation, OpenNettyEndpoint endpoint, OpenNettyModels.TemperatureControl.PilotWireTransmissionOptions? options) => operation switch
    {
        "ActivateShutdown" => _controller.ActivatePilotWireShutdownModeAsync(endpoint, options, Token).AsTask(),
        "CancelShutdown"   => _controller.CancelPilotWireShutdownModeAsync(endpoint, options, Token).AsTask(),
        "CancelDerogation" => _controller.CancelPilotWireDerogationModeAsync(endpoint, options, Token).AsTask(),

        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static OpenNettyCapability GetPilotWireCapability(string operation) => operation switch
    {
        "CancelDerogation" => OpenNettyCapabilities.PilotWireDerogation,
        _                  => OpenNettyCapabilities.PilotWireShutdown
    };

    private static OpenNettySetting GetSmartMeterSetting(string name) => name switch
    {
        "SmartMeterPeakOffPeakIndexOffsetBase"    => OpenNettySettings.SmartMeterPeakOffPeakIndexOffsetBase,
        "SmartMeterPeakOffPeakIndexOffsetOffPeak" => OpenNettySettings.SmartMeterPeakOffPeakIndexOffsetOffPeak,
        "SmartMeterBlueIndexOffsetBase"           => OpenNettySettings.SmartMeterBlueIndexOffsetBase,
        "SmartMeterBlueIndexOffsetOffPeak"        => OpenNettySettings.SmartMeterBlueIndexOffsetOffPeak,
        "SmartMeterWhiteIndexOffsetBase"          => OpenNettySettings.SmartMeterWhiteIndexOffsetBase,
        "SmartMeterWhiteIndexOffsetOffPeak"       => OpenNettySettings.SmartMeterWhiteIndexOffsetOffPeak,
        "SmartMeterRedIndexOffsetBase"            => OpenNettySettings.SmartMeterRedIndexOffsetBase,
        "SmartMeterRedIndexOffsetOffPeak"         => OpenNettySettings.SmartMeterRedIndexOffsetOffPeak,

        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private Task ExecuteZigbeeNetworkOperationAsync(string operation, OpenNettyEndpoint endpoint) => operation switch
    {
        "OpenNetwork"        => _controller.OpenZigbeeNetworkAsync(endpoint, Token).AsTask(),
        "CloseNetwork"       => _controller.CloseZigbeeNetworkAsync(endpoint, Token).AsTask(),
        "EnableSupervision"  => _controller.EnableZigbeeSupervisionAsync(endpoint, Token).AsTask(),
        "DisableSupervision" => _controller.DisableZigbeeSupervisionAsync(endpoint, Token).AsTask(),
        "CreateNetwork"      => _controller.CreateZigbeeNetworkAsync(endpoint, Token).AsTask(),
        "JoinNetwork"        => _controller.JoinZigbeeNetworkAsync(endpoint, Token).AsTask(),
        "LeaveNetwork"       => _controller.LeaveZigbeeNetworkAsync(endpoint, Token).AsTask(),
        "CountDevices"       => _controller.CountZigbeeDevicesAsync(endpoint, Token).AsTask(),
        "GetChannel"         => _controller.GetZigbeeChannelAsync(endpoint, Token).AsTask(),

        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static Task BindOrUnbindAsync(OpenNettyController controller, bool bind, OpenNettyEndpoint endpoint) => bind
        ? controller.BindZigbeeEndpointAsync(endpoint, Token).AsTask()
        : controller.UnbindZigbeeEndpointAsync(endpoint, Token).AsTask();

    private static OpenNettyEndpoint CreateZigbeeEndpointWithTimeouts(TimeSpan acknowledgement, TimeSpan processing)
    {
        var gateway = GetGateway(OpenNettyProtocol.Zigbee);
        var customized = gateway with
        {
            Options = gateway.Options with
            {
                DefaultTransmissionOptions = gateway.Options.DefaultTransmissionOptions with
                {
                    AcknowledgementTimeout = acknowledgement,
                    OutgoingMessageProcessingTimeout = processing
                }
            }
        };

        return CreateEndpoint(OpenNettyProtocol.Zigbee, OpenNettyCapabilities.ZigbeeNetworkManagement) with { Gateway = customized };
    }

    private Task ExecuteSimpleDiagnosticOperationAsync(string operation, OpenNettyEndpoint endpoint) => operation switch
    {
        "EraseAddress"    => _controller.EraseAddressAsync(endpoint, Token).AsTask(),
        "ResetMemoryData" => _controller.ResetMemoryDataAsync(endpoint, Token).AsTask(),
        "LockActuator"    => _controller.LockActuatorAsync(endpoint, Token).AsTask(),
        "UnlockActuator"  => _controller.UnlockActuatorAsync(endpoint, Token).AsTask(),

        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private Task ReadDiagnosticAsync(string operation, OpenNettyEndpoint endpoint) => operation switch
    {
        "DeviceDescription" => _controller.GetDeviceDescriptionAsync(endpoint, Token).AsTask(),
        "UnitDescription"   => _controller.GetUnitDescriptionAsync(endpoint, Token).AsTask(),
        "FirmwareVersion"   => _controller.GetFirmwareVersionAsync(endpoint, Token).AsTask(),
        "HardwareVersion"   => _controller.GetHardwareVersionAsync(endpoint, Token).AsTask(),
        "MacAddress"        => _controller.GetMacAddressAsync(endpoint, Token).AsTask(),
        "Uptime"            => _controller.GetUptimeAsync(endpoint, Token).AsTask(),
        "DateTime"          => _controller.GetDateTimeAsync(endpoint, Token).AsTask(),
        "MemoryData"        => _controller.GetMemoryDataAsync(endpoint, Token).AsTask(),

        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private Task<Version> ReadVersionAsync(string operation, OpenNettyEndpoint endpoint) => operation switch
    {
        "FirmwareVersion" => _controller.GetFirmwareVersionAsync(endpoint, Token).AsTask(),
        "HardwareVersion" => _controller.GetHardwareVersionAsync(endpoint, Token).AsTask(),

        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static OpenNettyDimension GetVersionDimension(string operation) => operation switch
    {
        "FirmwareVersion" => OpenNettyDimensions.Management.FirmwareVersion,
        "HardwareVersion" => OpenNettyDimensions.Management.HardwareVersion,

        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static OpenNettyCapability GetSimpleDiagnosticCapability(string operation) => operation switch
    {
        "EraseAddress" or "ResetMemoryData" => OpenNettyCapabilities.MemoryWriting,
        _                                   => OpenNettyCapabilities.ActuatorLocking
    };

    private static OpenNettyModels.Diagnostics.MemoryData CreateMemoryData(OpenNettyMedium medium) => new()
    {
        Address      = OpenNettyAddress.FromNitooAddress(1234, 1),
        FunctionCode = 7,
        Medium       = medium
    };

    private static OpenNettyGateway CreateGateway(string model)
    {
        var device = OpenNettyDevice.Create(OpenNettyBrand.Legrand, model, "Gateway " + model, identifier: null);
        var gateway = OpenNettyGateway.Create(device, new IPEndPoint(IPAddress.Loopback, 20000));

        device.Gateway = gateway;
        return gateway;
    }

    private static OpenNettyGateway GetGateway(OpenNettyProtocol protocol) => _gateways[protocol];

    private static OpenNettyAddress GetDefaultAddress(OpenNettyProtocol protocol) => protocol switch
    {
        OpenNettyProtocol.Nitoo  => OpenNettyAddress.FromNitooAddress(1234, 1),
        OpenNettyProtocol.Scs    => OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, 2),
        OpenNettyProtocol.Zigbee => OpenNettyAddress.FromDecimalZigbeeAddress(4000000, 1),

        _ => throw new ArgumentOutOfRangeException(nameof(protocol))
    };

    private static OpenNettyMedium GetDefaultMedium(OpenNettyProtocol protocol) => protocol switch
    {
        OpenNettyProtocol.Nitoo  => OpenNettyMedium.Powerline,
        OpenNettyProtocol.Scs    => OpenNettyMedium.Bus,
        OpenNettyProtocol.Zigbee => OpenNettyMedium.Radio,

        _ => throw new ArgumentOutOfRangeException(nameof(protocol))
    };

    private static OpenNettyEndpoint CreateEndpoint(OpenNettyProtocol protocol, params OpenNettyCapability[] capabilities)
        => CreateEndpoint(protocol, GetDefaultAddress(protocol), capabilities);

    private static OpenNettyEndpoint CreateEndpoint(
        OpenNettyProtocol protocol, OpenNettyAddress address, params OpenNettyCapability[] capabilities) => new()
    {
        Address      = address,
        Capabilities = [.. capabilities],
        Gateway      = GetGateway(protocol),
        Medium       = GetDefaultMedium(protocol),
        Name         = "Test endpoint",
        Protocol     = protocol
    };

    private static OpenNettyEndpoint WithSetting(OpenNettyEndpoint endpoint, OpenNettySetting setting, string value)
        => endpoint with { Settings = endpoint.Settings.SetItem(setting, value) };

    private static OpenNettyTransmissionOptions GetDefaultOptions(OpenNettyEndpoint endpoint)
        => endpoint.Gateway!.Options.DefaultTransmissionOptions;

    private static OpenNettyMessage CreateDimensionRead(
        OpenNettyEndpoint endpoint, OpenNettyDimension dimension, params string[] values)
        => OpenNettyMessage.CreateDimensionRead(endpoint.Protocol, dimension, [.. values], endpoint.Address, endpoint.Medium);

    private static OpenNettyMessage CreateAcknowledgement(OpenNettyProtocol protocol)
        => OpenNettyMessage.CreateFromFrame(protocol, OpenNettyFrames.Acknowledgement);

    private static IAsyncObservable<OpenNettyMessage> CreateObservable(IEnumerable<OpenNettyMessage> messages, bool completes = true)
    {
        var snapshot = messages.ToArray();

        return AsyncObservable.Create<OpenNettyMessage>(async observer =>
        {
            try
            {
                foreach (var message in snapshot)
                {
                    await observer.OnNextAsync(message);
                }

                if (completes)
                {
                    await observer.OnCompletedAsync();
                }
            }

            // Operators like TakeWhile terminate their observer before the source completes: stop emitting when that happens.
            catch (InvalidOperationException)
            {
            }

            return AsyncDisposable.Nop;
        });
    }

    private static async IAsyncEnumerable<T> EnumerateAsync<T>(
        IEnumerable<T> items, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }

        await Task.CompletedTask;
    }

    private static IAsyncEnumerable<T> EnumerateAsync<T>(params T[] items) => EnumerateAsync(items.AsEnumerable());

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        List<T> items = [];

        await foreach (var item in source.WithCancellation(Token))
        {
            items.Add(item);
        }

        return items;
    }

    private void SetEndpointsAtAddress(OpenNettyEndpoint source, OpenNettyAddress address, params OpenNettyEndpoint[] endpoints)
        => _manager.FindEndpointsByAddressAsync(source.Gateway!, address, Arg.Any<CancellationToken>())
            .Returns(_ => EnumerateAsync(endpoints));

    private ServiceCall AssertSingleCall(ServiceMethod method)
    {
        var call = Assert.Single(_service.Calls);

        Assert.Equal(method, call.Method);
        return call;
    }

    private void AssertCommandExecuted(
        OpenNettyEndpoint endpoint, OpenNettyCommand command, OpenNettyMode? mode = null, OpenNettyTransmissionOptions? options = null)
    {
        var call = AssertSingleCall(ServiceMethod.ExecuteCommand);

        Assert.Equal(command, call.Command);
        AssertCommonArguments(call, endpoint, mode, options);
    }

    private void AssertDimensionSet(
        OpenNettyEndpoint endpoint, OpenNettyDimension dimension, string[] values, OpenNettyMode? mode = null)
    {
        var call = AssertSingleCall(ServiceMethod.SetDimension);

        Assert.Equal(dimension, call.Dimension);
        Assert.Equal(values, call.Values, StringComparer.Ordinal);
        AssertCommonArguments(call, endpoint, mode);
    }

    private static void AssertCommonArguments(
        ServiceCall call, OpenNettyEndpoint endpoint, OpenNettyMode? mode = null, OpenNettyTransmissionOptions? options = null)
    {
        Assert.Equal(endpoint.Protocol, call.Protocol);
        Assert.Equal(endpoint.Address, call.Address);
        Assert.Equal(endpoint.Medium, call.Medium);
        Assert.Equal(mode, call.Mode);
        Assert.Same(endpoint.Gateway, call.Gateway);
        Assert.Equal(options ?? GetDefaultOptions(endpoint), call.Options);
        Assert.Equal(Token, call.CancellationToken);
    }

    private static async Task<bool> IsAcceptedAsync(ServiceCall call, OpenNettyCommand command)
    {
        Assert.NotNull(call.Filter);

        return await call.Filter(command);
    }

    private enum ServiceMethod
    {
        EnumerateDimensions,
        EnumerateStatuses,
        ExecuteCommand,
        GetDimension,
        GetStatus,
        ObserveMessages,
        SendMessage,
        SetDimension
    }

    private sealed record class ServiceCall(ServiceMethod Method)
    {
        public CancellationToken CancellationToken { get; init; }
        public OpenNettyAddress? Address { get; init; }
        public OpenNettyCategory? Category { get; init; }
        public OpenNettyCommand? Command { get; init; }
        public OpenNettyDimension? Dimension { get; init; }
        public Func<OpenNettyCommand, ValueTask<bool>>? Filter { get; init; }
        public OpenNettyGateway? Gateway { get; init; }
        public OpenNettyMedium? Medium { get; init; }
        public OpenNettyMessage? Message { get; init; }
        public OpenNettyMode? Mode { get; init; }
        public OpenNettyTransmissionOptions? Options { get; init; }
        public OpenNettyProtocol? Protocol { get; init; }
        public ImmutableArray<string> Values { get; init; } = [];
    }

    private sealed class ExposedController(OpenNettyManager manager, IOpenNettyService service)
        : OpenNettyController(manager, service)
    {
        public OpenNettyTransmissionOptions GetOptions(OpenNettyEndpoint endpoint) => GetTransmissionOptions(endpoint);

        public ValueTask<Version> GetGatewayVersionAsync(OpenNettyGateway gateway, CancellationToken cancellationToken)
            => GetGatewayFirmwareVersionAsync(gateway, cancellationToken);
    }

    private sealed class FixedGatewayVersionController(
        OpenNettyManager manager, IOpenNettyService service, Version version) : OpenNettyController(manager, service)
    {
        protected override ValueTask<Version> GetGatewayFirmwareVersionAsync(
            OpenNettyGateway gateway, CancellationToken cancellationToken) => ValueTask.FromResult(version);
    }

    private sealed class ControllerServiceFake : IOpenNettyService
    {
        public List<ServiceCall> Calls { get; } = [];

        public Dictionary<OpenNettyDimension, ImmutableArray<string>> DimensionValues { get; } = [];

        public List<(OpenNettyAddress Address, ImmutableArray<string> Values)> DimensionEnumeration { get; } = [];

        public List<(OpenNettyAddress Address, OpenNettyCommand Command)> StatusEnumeration { get; } = [];

        public List<OpenNettyMessage> ObservedMessages { get; } = [];

        public Queue<Exception> CommandFailures { get; } = [];

        public OpenNettyCommand? StatusReply { get; set; }

        public IAsyncEnumerable<(OpenNettyAddress Address, ImmutableArray<string> Values)> EnumerateDimensionsAsync(
            OpenNettyProtocol protocol, OpenNettyDimension dimension, OpenNettyAddress? address = null,
            OpenNettyMedium? medium = null, OpenNettyMode? mode = null, OpenNettyGateway? gateway = null,
            OpenNettyTransmissionOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(new(ServiceMethod.EnumerateDimensions)
            {
                Address = address, CancellationToken = cancellationToken, Dimension = dimension, Gateway = gateway,
                Medium = medium, Mode = mode, Options = options, Protocol = protocol
            });

            return EnumerateAsync(DimensionEnumeration.ToArray());
        }

        public IAsyncEnumerable<(OpenNettyAddress Address, OpenNettyCommand Command)> EnumerateStatusesAsync(
            OpenNettyProtocol protocol, OpenNettyCategory category, OpenNettyAddress? address = null,
            OpenNettyMedium? medium = null, OpenNettyMode? mode = null, Func<OpenNettyCommand, ValueTask<bool>>? filter = null,
            OpenNettyGateway? gateway = null, OpenNettyTransmissionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new(ServiceMethod.EnumerateStatuses)
            {
                Address = address, CancellationToken = cancellationToken, Category = category, Filter = filter,
                Gateway = gateway, Medium = medium, Mode = mode, Options = options, Protocol = protocol
            });

            return EnumerateAsync(StatusEnumeration.ToArray());
        }

        public ValueTask ExecuteCommandAsync(
            OpenNettyProtocol protocol, OpenNettyCommand command, OpenNettyAddress? address = null,
            OpenNettyMedium? medium = null, OpenNettyMode? mode = null, OpenNettyGateway? gateway = null,
            OpenNettyTransmissionOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(new(ServiceMethod.ExecuteCommand)
            {
                Address = address, CancellationToken = cancellationToken, Command = command, Gateway = gateway,
                Medium = medium, Mode = mode, Options = options, Protocol = protocol
            });

            return CommandFailures.TryDequeue(out var failure) ? ValueTask.FromException(failure) : ValueTask.CompletedTask;
        }

        public ValueTask<ImmutableArray<string>> GetDimensionAsync(
            OpenNettyProtocol protocol, OpenNettyDimension dimension, OpenNettyAddress? address = null,
            OpenNettyMedium? medium = null, OpenNettyMode? mode = null, OpenNettyGateway? gateway = null,
            OpenNettyTransmissionOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(new(ServiceMethod.GetDimension)
            {
                Address = address, CancellationToken = cancellationToken, Dimension = dimension, Gateway = gateway,
                Medium = medium, Mode = mode, Options = options, Protocol = protocol
            });

            return DimensionValues.TryGetValue(dimension, out var values)
                ? ValueTask.FromResult(values)
                : ValueTask.FromException<ImmutableArray<string>>(new InvalidOperationException("No dimension values configured."));
        }

        public ValueTask<OpenNettyCommand> GetStatusAsync(
            OpenNettyProtocol protocol, OpenNettyCategory category, OpenNettyAddress? address = null,
            OpenNettyMedium? medium = null, OpenNettyMode? mode = null, Func<OpenNettyCommand, ValueTask<bool>>? filter = null,
            OpenNettyGateway? gateway = null, OpenNettyTransmissionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new(ServiceMethod.GetStatus)
            {
                Address = address, CancellationToken = cancellationToken, Category = category, Filter = filter,
                Gateway = gateway, Medium = medium, Mode = mode, Options = options, Protocol = protocol
            });

            return StatusReply is OpenNettyCommand reply
                ? ValueTask.FromResult(reply)
                : ValueTask.FromException<OpenNettyCommand>(new InvalidOperationException("No status reply configured."));
        }

        public IAsyncObservable<OpenNettyMessage> ObserveMessagesAsync(
            OpenNettyMessage message, OpenNettyGateway? gateway = null,
            OpenNettyTransmissionOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(new(ServiceMethod.ObserveMessages)
            {
                CancellationToken = cancellationToken, Gateway = gateway, Message = message, Options = options
            });

            return CreateObservable(ObservedMessages, completes: false);
        }

        public ValueTask SendMessageAsync(
            OpenNettyMessage message, OpenNettyGateway? gateway = null,
            OpenNettyTransmissionOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(new(ServiceMethod.SendMessage)
            {
                CancellationToken = cancellationToken, Gateway = gateway, Message = message, Options = options
            });

            return ValueTask.CompletedTask;
        }

        public ValueTask SetDimensionAsync(
            OpenNettyProtocol protocol, OpenNettyDimension dimension, ImmutableArray<string> values,
            OpenNettyAddress? address = null, OpenNettyMedium? medium = null, OpenNettyMode? mode = null,
            OpenNettyGateway? gateway = null, OpenNettyTransmissionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new(ServiceMethod.SetDimension)
            {
                Address = address, CancellationToken = cancellationToken, Dimension = dimension, Gateway = gateway,
                Medium = medium, Mode = mode, Options = options, Protocol = protocol, Values = values
            });

            return ValueTask.CompletedTask;
        }
    }
}
