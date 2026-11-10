/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Net;
using Microsoft.Extensions.Options;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyConfigurationTests
{
    [Fact]
    public void PostConfigure_NullOptions_ThrowsArgumentNullException()
    {
        var configuration = new OpenNettyConfiguration();

        var exception = Assert.Throws<ArgumentNullException>(() => configuration.PostConfigure(name: null, options: null!));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void PostConfigure_Gateways_AttachesTheGatewayToItsOwnDevice()
    {
        var configuration = new OpenNettyConfiguration();
        var gateway = CreateGateway(OpenNettyProtocol.Scs);
        var options = new OpenNettyOptions { Gateways = { gateway } };

        configuration.PostConfigure(name: null, options);

        Assert.Same(gateway, gateway.Device.Gateway);
    }

    [Fact]
    public void PostConfigure_DeviceWithoutGateway_UsesFirstGatewayWithSameProtocol()
    {
        var configuration = new OpenNettyConfiguration();
        var scsGateway = CreateGateway(OpenNettyProtocol.Scs);
        var firstNitooGateway = CreateGateway(OpenNettyProtocol.Nitoo, "First Nitoo gateway");
        var secondNitooGateway = CreateGateway(OpenNettyProtocol.Nitoo, "Second Nitoo gateway");
        var device = CreateNitooDevice();
        var options = new OpenNettyOptions
        {
            Devices = { device },
            Gateways = { scsGateway, firstNitooGateway, secondNitooGateway }
        };

        configuration.PostConfigure(name: null, options);

        Assert.Same(firstNitooGateway, device.Gateway);
    }

    [Fact]
    public void PostConfigure_ScsDeviceWithoutMatchingGateway_LeavesGatewayUnset()
    {
        var configuration = new OpenNettyConfiguration();
        var device = CreateScsDevice("Switch", gateway: null);
        var options = new OpenNettyOptions
        {
            Devices = { device },
            Gateways = { CreateGateway(OpenNettyProtocol.Nitoo) }
        };

        configuration.PostConfigure(name: null, options);

        Assert.Null(device.Gateway);
    }

    [Fact]
    public void PostConfigure_DeviceWithoutMatchingGateway_LeavesGatewayUnset()
    {
        var configuration = new OpenNettyConfiguration();
        var device = CreateNitooDevice();
        var options = new OpenNettyOptions
        {
            Devices = { device },
            Gateways = { CreateGateway(OpenNettyProtocol.Scs) }
        };

        configuration.PostConfigure(name: null, options);

        Assert.Null(device.Gateway);
    }

    [Fact]
    public void PostConfigure_DeviceWithExplicitGateway_KeepsTheExplicitGateway()
    {
        var configuration = new OpenNettyConfiguration();
        var firstGateway = CreateGateway(OpenNettyProtocol.Nitoo, "First gateway");
        var secondGateway = CreateGateway(OpenNettyProtocol.Nitoo, "Second gateway");
        var device = CreateNitooDevice();
        device.Gateway = secondGateway;
        var options = new OpenNettyOptions
        {
            Devices = { device },
            Gateways = { firstGateway, secondGateway }
        };

        configuration.PostConfigure(name: null, options);

        Assert.Same(secondGateway, device.Gateway);
    }

    [Fact]
    public void PostConfigure_NitooDeviceWithIdentifier_AddsImplicitEndpointsForAllUnits()
    {
        var configuration = new OpenNettyConfiguration();
        var gateway = CreateGateway(OpenNettyProtocol.Nitoo);
        var device = CreateNitooDevice();
        var options = new OpenNettyOptions { Devices = { device }, Gateways = { gateway } };

        configuration.PostConfigure(name: null, options);

        Assert.Equal(["Nitoo/000001/0", "Nitoo/000001/1", "Nitoo/000001/2"], options.Endpoints.Select(static endpoint => endpoint.Name), StringComparer.Ordinal);

        var endpoint = options.Endpoints[1];
        Assert.Equal(new OpenNettyAddress(OpenNettyAddressType.Nitoo, "17"), endpoint.Address);
        Assert.Same(gateway, endpoint.Gateway);
        Assert.Equal(OpenNettyMedium.Powerline, endpoint.Medium);
        Assert.Equal(OpenNettyProtocol.Nitoo, endpoint.Protocol);
        Assert.Same(device.GetUnit(1), endpoint.Unit);
    }

    [Fact]
    public void PostConfigure_ZigbeeDeviceWithIdentifier_AddsImplicitEndpointsForAllUnits()
    {
        var configuration = new OpenNettyConfiguration();
        var gateway = CreateGateway(OpenNettyProtocol.Zigbee);
        var device = OpenNettyDevice.Create(OpenNettyBrand.Legrand, "67223", "Sensor", OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(0x1Au));
        var options = new OpenNettyOptions { Devices = { device }, Gateways = { gateway } };

        configuration.PostConfigure(name: null, options);

        Assert.Equal(["Zigbee/0000001A/0", "Zigbee/0000001A/1"], options.Endpoints.Select(static endpoint => endpoint.Name), StringComparer.Ordinal);

        var endpoint = options.Endpoints[1];
        Assert.Equal(new OpenNettyAddress(OpenNettyAddressType.Zigbee, "2601"), endpoint.Address);
        Assert.Same(gateway, endpoint.Gateway);
        Assert.Equal(OpenNettyMedium.Radio, endpoint.Medium);
        Assert.Equal(OpenNettyProtocol.Zigbee, endpoint.Protocol);
        Assert.Same(device.GetUnit(1), endpoint.Unit);
    }

    [Fact]
    public void PostConfigure_NitooDeviceWithoutIdentifier_DoesNotAddImplicitEndpoints()
    {
        var configuration = new OpenNettyConfiguration();
        var device = OpenNettyDevice.Create(OpenNettyBrand.Legrand, "67201", "Switch", identifier: null);
        var options = new OpenNettyOptions { Devices = { device }, Gateways = { CreateGateway(OpenNettyProtocol.Nitoo) } };

        configuration.PostConfigure(name: null, options);

        Assert.Empty(options.Endpoints);
    }

    [Fact]
    public void PostConfigure_ScsDevice_DoesNotAddImplicitEndpoints()
    {
        var configuration = new OpenNettyConfiguration();
        var device = OpenNettyDevice.Create(OpenNettyBrand.BTicino, "F411U1", "Switch", identifier: null);
        var options = new OpenNettyOptions { Devices = { device }, Gateways = { CreateGateway(OpenNettyProtocol.Scs) } };

        configuration.PostConfigure(name: null, options);

        Assert.Empty(options.Endpoints);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs, "SCS/Gateway/0")]
    [InlineData(OpenNettyProtocol.Nitoo, "Nitoo/Gateway/0")]
    [InlineData(OpenNettyProtocol.Zigbee, "Zigbee/Gateway/0")]
    public void PostConfigure_GatewayDevice_AddsImplicitEndpointWithoutAddress(OpenNettyProtocol protocol, string expectedName)
    {
        var configuration = new OpenNettyConfiguration();
        var gateway = CreateGateway(protocol);
        var options = new OpenNettyOptions { Devices = { gateway.Device }, Gateways = { gateway } };

        configuration.PostConfigure(name: null, options);

        var endpoint = Assert.Single(options.Endpoints);
        Assert.Equal(expectedName, endpoint.Name);
        Assert.Null(endpoint.Address);
        Assert.Same(gateway, endpoint.Gateway);
        Assert.Equal(protocol, endpoint.Protocol);
        Assert.Same(gateway.Device.GetUnit(0), endpoint.Unit);
    }

    [Fact]
    public void PostConfigure_GatewayDeviceWithoutRegisteredGateway_LeavesGatewayUnset()
    {
        var configuration = new OpenNettyConfiguration();
        var device = OpenNettyDevice.Create(OpenNettyBrand.Legrand, "88213", "Gateway", identifier: null);
        var options = new OpenNettyOptions { Devices = { device } };

        configuration.PostConfigure(name: null, options);

        Assert.Null(device.Gateway);
        Assert.Null(Assert.Single(options.Endpoints).Gateway);
    }

    [Fact]
    public void PostConfigure_GatewayDeviceWithoutRegisteredGateway_DoesNotUseAnotherGatewayWithSameProtocol()
    {
        var configuration = new OpenNettyConfiguration();
        var otherGateway = CreateGateway(OpenNettyProtocol.Nitoo, "Other gateway");
        var device = OpenNettyDevice.Create(OpenNettyBrand.Legrand, "88213", "Gateway", identifier: null);
        var options = new OpenNettyOptions { Devices = { device }, Gateways = { otherGateway } };

        configuration.PostConfigure(name: null, options);

        Assert.Null(device.Gateway);
        Assert.Null(Assert.Single(options.Endpoints).Gateway);
    }

    [Fact]
    public void PostConfigure_UnitWithUserDefinedEndpoint_DoesNotOverrideTheEndpoint()
    {
        var configuration = new OpenNettyConfiguration();
        var gateway = CreateGateway(OpenNettyProtocol.Nitoo);
        var device = CreateNitooDevice();
        var endpoint = OpenNettyEndpoint.Create(device.GetUnit(1), "Custom endpoint");
        var options = new OpenNettyOptions { Devices = { device }, Gateways = { gateway }, Endpoints = { endpoint } };

        configuration.PostConfigure(name: null, options);

        Assert.Equal(["Custom endpoint", "Nitoo/000001/0", "Nitoo/000001/2"], options.Endpoints.Select(static endpoint => endpoint.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void PostConfigure_EndpointWithoutGateway_UsesTheGatewayOfItsDevice()
    {
        var configuration = new OpenNettyConfiguration();
        var firstGateway = CreateGateway(OpenNettyProtocol.Nitoo, "First gateway");
        var secondGateway = CreateGateway(OpenNettyProtocol.Nitoo, "Second gateway");
        var device = CreateNitooDevice();
        var endpoint = OpenNettyEndpoint.Create(device.GetUnit(1), "Endpoint");
        device.Gateway = secondGateway;
        var options = new OpenNettyOptions { Gateways = { firstGateway, secondGateway }, Endpoints = { endpoint } };

        configuration.PostConfigure(name: null, options);

        Assert.Same(secondGateway, endpoint.Gateway);
    }

    [Fact]
    public void PostConfigure_EndpointWithoutUnit_UsesFirstGatewayWithSameProtocol()
    {
        var configuration = new OpenNettyConfiguration();
        var scsGateway = CreateGateway(OpenNettyProtocol.Scs);
        var zigbeeGateway = CreateGateway(OpenNettyProtocol.Zigbee);
        var endpoint = new OpenNettyEndpoint { Name = "Endpoint", Protocol = OpenNettyProtocol.Zigbee };
        var options = new OpenNettyOptions { Gateways = { scsGateway, zigbeeGateway }, Endpoints = { endpoint } };

        configuration.PostConfigure(name: null, options);

        Assert.Same(zigbeeGateway, endpoint.Gateway);
    }

    [Fact]
    public void PostConfigure_EndpointWithoutMatchingGateway_LeavesGatewayUnset()
    {
        var configuration = new OpenNettyConfiguration();
        var endpoint = new OpenNettyEndpoint { Name = "Endpoint", Protocol = OpenNettyProtocol.Nitoo };
        var options = new OpenNettyOptions { Gateways = { CreateGateway(OpenNettyProtocol.Scs) }, Endpoints = { endpoint } };

        configuration.PostConfigure(name: null, options);

        Assert.Null(endpoint.Gateway);
    }

    [Fact]
    public void PostConfigure_EndpointWithExplicitGateway_KeepsTheExplicitGateway()
    {
        var configuration = new OpenNettyConfiguration();
        var firstGateway = CreateGateway(OpenNettyProtocol.Scs, "First gateway");
        var secondGateway = CreateGateway(OpenNettyProtocol.Scs, "Second gateway");
        var endpoint = new OpenNettyEndpoint { Name = "Endpoint", Protocol = OpenNettyProtocol.Scs, Gateway = secondGateway };
        var options = new OpenNettyOptions { Gateways = { firstGateway, secondGateway }, Endpoints = { endpoint } };

        configuration.PostConfigure(name: null, options);

        Assert.Same(secondGateway, endpoint.Gateway);
    }

    [Fact]
    public void Validate_NullOptions_ThrowsArgumentNullException()
    {
        var configuration = new OpenNettyConfiguration();

        var exception = Assert.Throws<ArgumentNullException>(() => configuration.Validate(name: null, options: null!));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void Validate_ConfiguredOptions_ReturnsSuccess()
    {
        var configuration = new OpenNettyConfiguration();
        var gateway = CreateGateway(OpenNettyProtocol.Nitoo);
        var options = new OpenNettyOptions
        {
            Devices = { gateway.Device, CreateNitooDevice() },
            Gateways = { gateway }
        };
        configuration.PostConfigure(name: null, options);

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_AnyOptions_MarksDevicesUnitsAndEndpointsAsReadOnly()
    {
        var configuration = new OpenNettyConfiguration();
        var gateway = CreateGateway(OpenNettyProtocol.Nitoo);
        var device = CreateNitooDevice();
        var endpoint = CreateEndpoint(gateway);
        var options = new OpenNettyOptions { Devices = { device }, Gateways = { gateway }, Endpoints = { endpoint } };

        configuration.Validate(name: null, options);

        Assert.True(device.IsReadOnly);
        Assert.All(device.Units, static unit => Assert.True(unit.IsReadOnly));
        Assert.True(endpoint.IsReadOnly);
    }

    [Fact]
    public void Validate_NoGateway_ReturnsFailure()
    {
        var configuration = new OpenNettyConfiguration();

        var result = configuration.Validate(name: null, new OpenNettyOptions());

        AssertSingleFailure(SR.GetResourceString(SR.ID2017), result);
    }

    [Fact]
    public void Validate_DevicesWithSameIdentifier_ReturnsFailure()
    {
        var configuration = new OpenNettyConfiguration();
        var gateway = CreateGateway(OpenNettyProtocol.Nitoo);
        var identifier = OpenNettyDeviceIdentifier.FromNitooSerialNumber(1);
        var options = new OpenNettyOptions
        {
            Devices =
            {
                CreateNitooDevice("First device", gateway, identifier),
                CreateNitooDevice("Second device", gateway, identifier)
            },
            Gateways = { gateway }
        };

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.FormatID2012("000001"), result);
    }

    [Fact]
    public void Validate_DevicesWithoutIdentifier_AreNotConsideredDuplicates()
    {
        var configuration = new OpenNettyConfiguration();
        var gateway = CreateGateway(OpenNettyProtocol.Scs);
        var options = new OpenNettyOptions
        {
            Devices =
            {
                CreateScsDevice("First device", gateway),
                CreateScsDevice("Second device", gateway)
            },
            Gateways = { gateway }
        };

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_DevicesWithSameNameIgnoringCase_ReturnsFailure()
    {
        var configuration = new OpenNettyConfiguration();
        var gateway = CreateGateway(OpenNettyProtocol.Scs);
        var options = new OpenNettyOptions
        {
            Devices =
            {
                CreateScsDevice("Device", gateway),
                CreateScsDevice("DEVICE", gateway)
            },
            Gateways = { gateway }
        };

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.FormatID2019("Device"), result);
    }

    [Fact]
    public void Validate_EndpointsWithSameNameIgnoringCase_ReturnsFailure()
    {
        var configuration = new OpenNettyConfiguration();
        var gateway = CreateGateway(OpenNettyProtocol.Scs);
        var options = new OpenNettyOptions
        {
            Gateways = { gateway },
            Endpoints = { CreateEndpoint(gateway, "Endpoint"), CreateEndpoint(gateway, "ENDPOINT") }
        };

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.FormatID2013("Endpoint"), result);
    }

    [Fact]
    public void Validate_DeviceWithoutGateway_ReturnsFailure()
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        options.Devices.Add(CreateScsDevice("Device", gateway: null));

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.FormatID2022("Device"), result);
    }

    [Theory]
    [InlineData("Device+1")]
    [InlineData("Device*1")]
    [InlineData("+")]
    [InlineData("*")]
    public void Validate_DeviceNameWithReservedCharacter_ReturnsFailure(string name)
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        options.Devices.Add(CreateScsDevice(name, gateway));

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.FormatID2020(name), result);
    }

    [Fact]
    public void Validate_ScsDeviceWithActionValidationSetting_ReturnsFailure()
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var device = CreateScsDevice("Device", gateway);
        device.Settings = Settings((OpenNettySettings.ActionValidation, "true"));
        options.Devices.Add(device);

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.GetResourceString(SR.ID2010), result);
    }

    [Fact]
    public void Validate_NitooDeviceWithActionValidationSetting_ReturnsSuccess()
    {
        var configuration = new OpenNettyConfiguration();
        var gateway = CreateGateway(OpenNettyProtocol.Nitoo);
        var device = CreateNitooDevice();
        device.Gateway = gateway;
        device.Settings = Settings((OpenNettySettings.ActionValidation, "true"));
        var options = new OpenNettyOptions { Devices = { device }, Gateways = { gateway } };

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_ScsDeviceWithInvalidActionValidationSetting_ReturnsSuccess()
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var device = CreateScsDevice("Device", gateway);
        device.Settings = Settings((OpenNettySettings.ActionValidation, "not a boolean"));
        options.Devices.Add(device);

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_ValidEndpoint_ReturnsSuccess()
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        options.Endpoints.Add(CreateEndpoint(gateway));

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_EndpointWithoutGateway_ReturnsFailure()
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Gateway = null;
        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.FormatID2023("Endpoint"), result);
    }

    [Fact]
    public void Validate_EndpointWithNonGatewayDeviceAsGateway_ReturnsFailure()
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Gateway = OpenNettyGateway.Create(CreateScsDevice("Not a gateway", gateway: null), new DnsEndPoint("localhost", 20_000));
        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.FormatID2023("Endpoint"), result);
    }

    [Fact]
    public void Validate_EndpointWithoutAddressAndUnit_ReturnsFailure()
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Address = null;
        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.FormatID2024("Endpoint"), result);
    }

    [Fact]
    public void Validate_EndpointWithoutAddressAndWithNonGatewayUnit_ReturnsFailure()
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Address = null;
        endpoint.Unit = CreateNitooDevice().GetUnit(1);
        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.FormatID2024("Endpoint"), result);
    }

    [Fact]
    public void Validate_EndpointWithoutAddressAndWithGatewayUnit_ReturnsSuccess()
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Address = null;
        endpoint.Unit = gateway.Device.GetUnit(0);
        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Endpoint+1")]
    [InlineData("Endpoint*1")]
    [InlineData("+")]
    [InlineData("*")]
    public void Validate_EndpointNameWithReservedCharacter_ReturnsFailure(string name)
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        options.Endpoints.Add(CreateEndpoint(gateway, name));

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.FormatID2000(name), result);
    }

    [Fact]
    public void Validate_NonNitooEndpointWithActionValidationSetting_ReturnsFailure()
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Settings = Settings((OpenNettySettings.ActionValidation, "false"));
        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.GetResourceString(SR.ID2010), result);
    }

    [Fact]
    public void Validate_NitooEndpointWithActionValidationSettingAndWithoutAddress_ReturnsFailure()
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Protocol = OpenNettyProtocol.Nitoo;
        endpoint.Address = null;
        endpoint.Unit = gateway.Device.GetUnit(0);
        endpoint.Settings = Settings((OpenNettySettings.ActionValidation, "true"));
        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.GetResourceString(SR.ID2011), result);
    }

    [Fact]
    public void Validate_NitooEndpointWithActionValidationSettingAndAddress_ReturnsSuccess()
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Protocol = OpenNettyProtocol.Nitoo;
        endpoint.Address = new OpenNettyAddress(OpenNettyAddressType.Nitoo, "17");
        endpoint.Settings = Settings((OpenNettySettings.ActionValidation, "true"));
        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_EndpointWithConfigurableFunctionTypeAndMissingSetting_ReturnsFailure(string? functionType)
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Capabilities = [OpenNettyCapabilities.ConfigurableFunctionType];

        if (functionType is not null)
        {
            endpoint.Settings = Settings((OpenNettySettings.FunctionType, functionType));
        }

        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.FormatID2002("Endpoint"), result);
    }

    [Theory]
    [InlineData(OpenNettySettings.FunctionTypes.AutomationActuator)]
    [InlineData(OpenNettySettings.FunctionTypes.ContactState)]
    [InlineData(OpenNettySettings.FunctionTypes.LightActuator)]
    [InlineData(OpenNettySettings.FunctionTypes.ScheduledScenario)]
    [InlineData(OpenNettySettings.FunctionTypes.ScheduledScenarioPlus)]
    public void Validate_EndpointWithSupportedFunctionType_ReturnsSuccess(string functionType)
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Capabilities = [OpenNettyCapabilities.ConfigurableFunctionType];
        endpoint.Settings = Settings((OpenNettySettings.FunctionType, functionType));
        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Unsupported", false)]
    [InlineData("Unsupported", true)]
    [InlineData("", false)]
    [InlineData("light actuator", true)]
    public void Validate_EndpointWithUnsupportedFunctionType_ReturnsFailure(string functionType, bool configurable)
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Settings = Settings((OpenNettySettings.FunctionType, functionType));

        if (configurable)
        {
            endpoint.Capabilities = [OpenNettyCapabilities.ConfigurableFunctionType];
        }

        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.FormatID2003("Endpoint", functionType), result);
    }

    [Theory]
    [InlineData(OpenNettySettings.SwitchModes.Default)]
    [InlineData(OpenNettySettings.SwitchModes.PushButton)]
    public void Validate_EndpointWithSupportedSwitchMode_ReturnsSuccess(string switchMode)
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Settings = Settings((OpenNettySettings.SwitchMode, switchMode));
        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Toggle")]
    [InlineData("default")]
    [InlineData("")]
    public void Validate_EndpointWithUnsupportedSwitchMode_ReturnsFailure(string switchMode)
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Settings = Settings((OpenNettySettings.SwitchMode, switchMode));
        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.FormatID2004("Endpoint", switchMode), result);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(null, OpenNettySettings.FunctionTypes.ScheduledScenario)]
    [InlineData("", OpenNettySettings.FunctionTypes.ScheduledScenario)]
    [InlineData(null, OpenNettySettings.FunctionTypes.ScheduledScenarioPlus)]
    [InlineData("", OpenNettySettings.FunctionTypes.ScheduledScenarioPlus)]
    public void Validate_EndpointWithConfigurablePushButtonNumbersAndMissingSetting_ReturnsFailure(string? numbers, string? functionType)
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Capabilities = [OpenNettyCapabilities.ConfigurablePushButtonNumbers];
        endpoint.Settings = CreateSettings(numbers, functionType);
        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.FormatID2015("Endpoint"), result);
    }

    [Fact]
    public void Validate_EndpointWithConfigurablePushButtonNumbersAndNonScenarioFunctionType_DoesNotRequirePushButtonNumbers()
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Capabilities = [OpenNettyCapabilities.ConfigurablePushButtonNumbers];
        endpoint.Settings = Settings((OpenNettySettings.FunctionType, OpenNettySettings.FunctionTypes.LightActuator));
        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_EndpointWithoutPushButtonNumbersCapability_DoesNotRequirePushButtonNumbers()
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        options.Endpoints.Add(CreateEndpoint(gateway));

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1,2,3")]
    [InlineData("0,255")]
    [InlineData("1,,2")]
    [InlineData("1,2,")]
    public void Validate_EndpointWithValidPushButtonNumbers_ReturnsSuccess(string numbers)
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Settings = Settings((OpenNettySettings.PushButtonNumbers, numbers));
        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",")]
    [InlineData("a")]
    [InlineData("1,a")]
    [InlineData("256")]
    [InlineData("-1")]
    [InlineData("1.5")]
    public void Validate_EndpointWithInvalidPushButtonNumbers_ReturnsFailure(string numbers)
    {
        var configuration = new OpenNettyConfiguration();
        var options = CreateOptionsWithGateway(out var gateway);
        var endpoint = CreateEndpoint(gateway);
        endpoint.Settings = Settings((OpenNettySettings.PushButtonNumbers, numbers));
        options.Endpoints.Add(endpoint);

        var result = configuration.Validate(name: null, options);

        AssertSingleFailure(SR.FormatID2016("Endpoint"), result);
    }

    [Fact]
    public void Validate_MultipleProblems_ReturnsAllFailures()
    {
        var configuration = new OpenNettyConfiguration();
        var endpoint = CreateEndpoint(gateway: null, "Endpoint+");
        endpoint.Address = null;
        var options = new OpenNettyOptions { Endpoints = { endpoint } };

        var result = configuration.Validate(name: null, options);

        Assert.Equal(
        [
            SR.GetResourceString(SR.ID2017),
            SR.FormatID2023("Endpoint+"),
            SR.FormatID2024("Endpoint+"),
            SR.FormatID2000("Endpoint+")
        ], result.Failures, StringComparer.Ordinal);
    }

    private static void AssertSingleFailure(string expected, ValidateOptionsResult result)
    {
        Assert.True(result.Failed);
        Assert.Equal(expected, Assert.Single(result.Failures!));
    }

    private static OpenNettyOptions CreateOptionsWithGateway(out OpenNettyGateway gateway)
    {
        gateway = CreateGateway(OpenNettyProtocol.Scs);

        return new OpenNettyOptions { Gateways = { gateway } };
    }

    private static ImmutableDictionary<OpenNettySetting, string> Settings(params (OpenNettySetting Setting, string Value)[] settings)
        => settings.ToImmutableDictionary(static setting => setting.Setting, static setting => setting.Value);

    private static ImmutableDictionary<OpenNettySetting, string> CreateSettings(string? numbers, string? functionType)
    {
        var settings = ImmutableDictionary<OpenNettySetting, string>.Empty;

        if (numbers is not null)
        {
            settings = settings.Add(OpenNettySettings.PushButtonNumbers, numbers);
        }

        if (functionType is not null)
        {
            settings = settings.Add(OpenNettySettings.FunctionType, functionType);
        }

        return settings;
    }

    private static OpenNettyEndpoint CreateEndpoint(OpenNettyGateway? gateway, string name = "Endpoint") => new()
    {
        Address = OpenNettyAddress.FromScsLightPointAddress(extension: 0, general: false, group: null, area: 1, point: 1),
        Gateway = gateway,
        Name = name,
        Protocol = OpenNettyProtocol.Scs
    };

    private static OpenNettyGateway CreateGateway(OpenNettyProtocol protocol, string name = "Gateway")
    {
        var device = protocol switch
        {
            OpenNettyProtocol.Scs    => OpenNettyDevice.Create(OpenNettyBrand.BTicino, "F454", name, identifier: null),
            OpenNettyProtocol.Nitoo  => OpenNettyDevice.Create(OpenNettyBrand.Legrand, "88213", name, identifier: null),
            OpenNettyProtocol.Zigbee => OpenNettyDevice.Create(OpenNettyBrand.Legrand, "88328", name, identifier: null),

            _ => throw new ArgumentOutOfRangeException(nameof(protocol))
        };

        return OpenNettyGateway.Create(device, new DnsEndPoint("localhost", 20_000));
    }

    private static OpenNettyDevice CreateNitooDevice(
        string name = "Switch", OpenNettyGateway? gateway = null, OpenNettyDeviceIdentifier? identifier = null)
    {
        var device = OpenNettyDevice.Create(OpenNettyBrand.Legrand, "67201", name, identifier ?? OpenNettyDeviceIdentifier.FromNitooSerialNumber(1));
        device.Gateway = gateway;

        return device;
    }

    private static OpenNettyDevice CreateScsDevice(string name, OpenNettyGateway? gateway)
    {
        var device = OpenNettyDevice.Create(OpenNettyBrand.BTicino, "F411U1", name, identifier: null);
        device.Gateway = gateway;

        return device;
    }
}
