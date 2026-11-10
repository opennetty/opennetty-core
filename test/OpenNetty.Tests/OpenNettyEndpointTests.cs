/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyEndpointTests
{
    [Fact]
    public void NewInstance_HasDefaultValuesAndIsNotReadOnly()
    {
        var endpoint = CreateEndpoint();

        Assert.False(endpoint.IsReadOnly);
        Assert.Null(endpoint.Address);
        Assert.Empty(endpoint.Capabilities);
        Assert.Null(endpoint.Gateway);
        Assert.Null(endpoint.Medium);
        Assert.Empty(endpoint.Settings);
        Assert.Null(endpoint.Unit);
    }

    [Fact]
    public void Properties_WhenMutable_CanBeSet()
    {
        var address = OpenNettyAddress.FromNitooAddress(1234, 5);
        var unit = CreateUnit();

        var endpoint = new OpenNettyEndpoint
        {
            Address = address,
            Capabilities = [new OpenNettyCapability("Capability")],
            Medium = OpenNettyMedium.Powerline,
            Name = "Endpoint",
            Protocol = OpenNettyProtocol.Nitoo,
            Settings = CreateSettings(("Setting", "Value")),
            Unit = unit
        };

        Assert.Equal(address, endpoint.Address);
        Assert.Contains(new OpenNettyCapability("Capability"), endpoint.Capabilities);
        Assert.Equal(OpenNettyMedium.Powerline, endpoint.Medium);
        Assert.Equal("Endpoint", endpoint.Name);
        Assert.Equal(OpenNettyProtocol.Nitoo, endpoint.Protocol);
        Assert.Equal("Value", endpoint.GetStringSetting(new OpenNettySetting("Setting")));
        Assert.Same(unit, endpoint.Unit);
    }

    public static TheoryData<Action<OpenNettyEndpoint>> Mutations => new()
    {
        { endpoint => endpoint.Address = null },
        { endpoint => endpoint.Capabilities = [] },
        { endpoint => endpoint.Gateway = null },
        { endpoint => endpoint.Medium = null },
        { endpoint => endpoint.Name = "Name" },
        { endpoint => endpoint.Protocol = OpenNettyProtocol.Nitoo },
        { endpoint => endpoint.Settings = ImmutableDictionary<OpenNettySetting, string>.Empty },
        { endpoint => endpoint.Unit = null }
    };

    [Theory]
    [MemberData(nameof(Mutations))]
    public void Properties_WhenReadOnly_ThrowInvalidOperationException(Action<OpenNettyEndpoint> mutation)
    {
        var endpoint = CreateEndpoint();
        endpoint.MakeReadOnly();

        var exception = Assert.Throws<InvalidOperationException>(() => mutation(endpoint));

        Assert.Equal(SR.GetResourceString(SR.ID2021), exception.Message);
    }

    [Fact]
    public void Name_NullValue_ThrowsArgumentNullException()
    {
        var endpoint = CreateEndpoint();

        var exception = Assert.Throws<ArgumentNullException>(() => endpoint.Name = null!);

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Name_EmptyValue_ThrowsArgumentException()
    {
        var endpoint = CreateEndpoint();

        var exception = Assert.Throws<ArgumentException>(() => endpoint.Name = string.Empty);

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Equals_SameName_ReturnsTrueRegardlessOfOtherProperties()
    {
        var left = CreateEndpoint("Endpoint");
        var right = CreateEndpoint("Endpoint", OpenNettyProtocol.Zigbee);
        right.Address = OpenNettyAddress.FromNitooAddress(1, 1);

        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object) right));
        Assert.True(left == right);
        Assert.False(left != right);
    }

    [Theory]
    [InlineData("Endpoint", "endpoint")]
    [InlineData("Endpoint", "Other")]
    public void Equals_DifferentName_ReturnsFalse(string leftName, string rightName)
    {
        var left = CreateEndpoint(leftName);
        var right = CreateEndpoint(rightName);

        Assert.False(left.Equals(right));
        Assert.True(left != right);
    }

    [Fact]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var endpoint = CreateEndpoint();
        var sameInstance = endpoint;

        Assert.True(endpoint.Equals(sameInstance));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var endpoint = CreateEndpoint();

        Assert.False(endpoint.Equals((OpenNettyEndpoint?) null));
        Assert.False(endpoint.Equals((object?) null));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("False", false)]
    [InlineData("yes", null)]
    public void GetBooleanSetting_ExistingSetting_ReturnsParsedValueOrNull(string value, bool? expected)
    {
        var endpoint = CreateEndpoint();
        endpoint.Settings = CreateSettings(("Setting", value));

        Assert.Equal(expected, endpoint.GetBooleanSetting(new OpenNettySetting("Setting")));
    }

    [Fact]
    public void GetSettings_MissingSetting_ReturnNull()
    {
        var endpoint = CreateEndpoint();
        endpoint.Unit = CreateUnit();
        var missing = new OpenNettySetting("Missing");

        Assert.Null(endpoint.GetBooleanSetting(missing));
        Assert.Null(endpoint.GetIntegerSetting(missing));
        Assert.Null(endpoint.GetStringSetting(missing));
    }

    [Fact]
    public void GetHashCode_SameName_ReturnsSameHashCode()
    {
        Assert.Equal(CreateEndpoint("Endpoint").GetHashCode(), CreateEndpoint("Endpoint", OpenNettyProtocol.Scs).GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentNames_ReturnsDifferentHashCodes()
    {
        Assert.NotEqual(CreateEndpoint("First").GetHashCode(), CreateEndpoint("Second").GetHashCode());
    }

    [Theory]
    [InlineData("123", 123L)]
    [InlineData("-42", -42L)]
    [InlineData("abc", null)]
    public void GetIntegerSetting_ExistingSetting_ReturnsParsedValueOrNull(string value, long? expected)
    {
        var endpoint = CreateEndpoint();
        endpoint.Settings = CreateSettings(("Setting", value));

        Assert.Equal(expected, endpoint.GetIntegerSetting(new OpenNettySetting("Setting")));
    }

    [Fact]
    public void GetStringSetting_ExistingSetting_ReturnsValue()
    {
        var endpoint = CreateEndpoint();
        endpoint.Settings = CreateSettings(("Setting", "Value"));

        Assert.Equal("Value", endpoint.GetStringSetting(new OpenNettySetting("SETTING")));
    }

    [Fact]
    public void HasCapability_CapabilityDefinedOnEndpoint_ReturnsTrue()
    {
        var endpoint = CreateEndpoint();
        endpoint.Capabilities = [new OpenNettyCapability("Capability")];

        Assert.True(endpoint.HasCapability(new OpenNettyCapability("capability")));
    }

    [Fact]
    public void HasCapability_CapabilityDefinedOnUnit_ReturnsTrue()
    {
        var endpoint = CreateEndpoint(unit: CreateUnit(unitCapability: "Capability"));

        Assert.True(endpoint.HasCapability(new OpenNettyCapability("Capability")));
    }

    [Fact]
    public void HasCapability_UnknownCapability_ReturnsFalse()
    {
        var endpoint = CreateEndpoint(unit: CreateUnit(unitCapability: "Capability"));
        endpoint.Capabilities = [new OpenNettyCapability("Other capability")];

        Assert.False(endpoint.HasCapability(new OpenNettyCapability("Missing")));
    }

    [Fact]
    public void HasCapability_NoUnitAndNoCapabilities_ReturnsFalse()
    {
        var endpoint = CreateEndpoint();

        Assert.False(endpoint.HasCapability(new OpenNettyCapability("Capability")));
    }

    [Fact]
    public void MakeReadOnly_CalledTwice_MarksInstanceAsReadOnly()
    {
        var endpoint = CreateEndpoint();

        endpoint.MakeReadOnly();
        endpoint.MakeReadOnly();

        Assert.True(endpoint.IsReadOnly);
    }

    [Fact]
    public void TryGetSetting_SettingDefinedOnEndpointWithoutUnit_ReturnsEndpointValue()
    {
        var endpoint = CreateEndpoint();
        endpoint.Settings = CreateSettings(("Setting", "Endpoint value"));

        Assert.True(endpoint.TryGetSetting(new OpenNettySetting("Setting"), out var value));
        Assert.Equal("Endpoint value", value);
    }

    [Fact]
    public void TryGetSetting_MissingSettingWithoutUnit_ReturnsFalse()
    {
        var endpoint = CreateEndpoint();

        Assert.False(endpoint.TryGetSetting(new OpenNettySetting("Missing"), out var value));
        Assert.Null(value);
    }

    [Fact]
    public void TryGetSetting_SettingDefinedOnUnit_ReturnsUnitValue()
    {
        var unit = CreateUnit();
        unit.Settings = CreateSettings(("Setting", "Unit value"));
        var endpoint = CreateEndpoint(unit: unit);

        Assert.True(endpoint.TryGetSetting(new OpenNettySetting("Setting"), out var value));
        Assert.Equal("Unit value", value);
    }

    [Fact]
    public void TryGetSetting_SettingDefinedOnUnitDefinition_ReturnsUnitDefinitionValue()
    {
        var endpoint = CreateEndpoint(unit: CreateUnit(unitDefinitionSettings: [("Setting", "Unit definition value")]));

        Assert.True(endpoint.TryGetSetting(new OpenNettySetting("Setting"), out var value));
        Assert.Equal("Unit definition value", value);
    }

    [Fact]
    public void TryGetSetting_SettingDefinedOnDevice_ReturnsDeviceValue()
    {
        var unit = CreateUnit();
        unit.Device.Settings = CreateSettings(("Setting", "Device value"));
        var endpoint = CreateEndpoint(unit: unit);

        Assert.True(endpoint.TryGetSetting(new OpenNettySetting("Setting"), out var value));
        Assert.Equal("Device value", value);
    }

    [Fact]
    public void TryGetSetting_SettingDefinedOnDeviceDefinition_ReturnsDeviceDefinitionValue()
    {
        var endpoint = CreateEndpoint(unit: CreateUnit(deviceDefinitionSettings: [("Setting", "Device definition value")]));

        Assert.True(endpoint.TryGetSetting(new OpenNettySetting("Setting"), out var value));
        Assert.Equal("Device definition value", value);
    }

    [Fact]
    public void TryGetSetting_SettingDefinedAtSeveralLevels_ReturnsMostSpecificValue()
    {
        var unit = CreateUnit(
            deviceDefinitionSettings: [("Setting", "Device definition value")],
            unitDefinitionSettings: [("Setting", "Unit definition value")]);
        unit.Device.Settings = CreateSettings(("Setting", "Device value"));
        var endpoint = CreateEndpoint(unit: unit);
        var setting = new OpenNettySetting("Setting");

        Assert.Equal("Unit definition value", endpoint.GetStringSetting(setting));

        unit.Settings = CreateSettings(("Setting", "Unit value"));
        Assert.Equal("Unit value", endpoint.GetStringSetting(setting));

        endpoint.Settings = CreateSettings(("Setting", "Endpoint value"));
        Assert.Equal("Endpoint value", endpoint.GetStringSetting(setting));
    }

    [Fact]
    public void ToString_ReturnsName()
    {
        Assert.Equal("Living room", CreateEndpoint("Living room").ToString());
    }

    [Fact]
    public void Create_NullUnit_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => OpenNettyEndpoint.Create(null!, "Endpoint"));

        Assert.Equal("unit", exception.ParamName);
    }

    [Fact]
    public void Create_NitooUnitWithIdentifier_InfersAddressAndDefaultName()
    {
        var device = CreateCatalogDevice(OpenNettyBrand.Legrand, "03600", OpenNettyDeviceIdentifier.FromNitooSerialNumber(1234));

        var endpoint = OpenNettyEndpoint.Create(device.GetUnit(3), name: null);

        Assert.Equal(OpenNettyAddress.FromNitooAddress(1234, 3), endpoint.Address);
        Assert.Equal("Nitoo/001234/3", endpoint.Name);
    }

    [Fact]
    public void Create_ZigbeeUnitWithIdentifier_InfersAddressAndDefaultName()
    {
        var device = CreateCatalogDevice(OpenNettyBrand.Legrand, "573862", OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(255));

        var endpoint = OpenNettyEndpoint.Create(device.GetUnit(1), name: string.Empty);

        Assert.Equal(OpenNettyAddress.FromZigbeeAddress(OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(255), 1), endpoint.Address);
        Assert.Equal("Zigbee/000000FF/1", endpoint.Name);
    }

    [Fact]
    public void Create_NitooUnitWithoutIdentifier_ThrowsArgumentException()
    {
        var device = CreateCatalogDevice(OpenNettyBrand.Legrand, "03600", identifier: null);

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyEndpoint.Create(device.GetUnit(3), "Endpoint"));

        Assert.Equal("address", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0136), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ScsUnitWithoutAddress_ThrowsArgumentException()
    {
        var device = CreateCatalogDevice(OpenNettyBrand.Legrand, "03847", identifier: null);

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyEndpoint.Create(device.GetUnit(1), "Endpoint"));

        Assert.Equal("address", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0136), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ScsUnitWithExplicitAddress_UsesAddressAndComputedName()
    {
        var device = CreateCatalogDevice(OpenNettyBrand.Legrand, "03847", identifier: null);
        var unit = device.GetUnit(1);
        var address = OpenNettyAddress.FromScsLightPointAddress(extension: 0, general: false, group: null, area: 5, point: 3);

        var endpoint = OpenNettyEndpoint.Create(unit, name: null, address);

        Assert.Equal(address, endpoint.Address);
        Assert.Equal(OpenNettyUtilities.ComputeDefaultEndpointName(OpenNettyProtocol.Scs, address, unit), endpoint.Name);
    }

    [Fact]
    public void Create_GatewayUnitWithoutAddress_DoesNotInferAddress()
    {
        var device = CreateCatalogDevice(OpenNettyBrand.Legrand, "88213", identifier: null, name: "Gateway");

        var endpoint = OpenNettyEndpoint.Create(device.GetUnit(0), name: null);

        Assert.Null(endpoint.Address);
        Assert.Equal("Nitoo/Gateway/0", endpoint.Name);
    }

    [Fact]
    public void Create_ExplicitAddressAndName_UsesThem()
    {
        var device = CreateCatalogDevice(OpenNettyBrand.Legrand, "03600", OpenNettyDeviceIdentifier.FromNitooSerialNumber(1234));
        var address = OpenNettyAddress.FromNitooAddress(1, 1);

        var endpoint = OpenNettyEndpoint.Create(device.GetUnit(3), "Living room light", address);

        Assert.Equal(address, endpoint.Address);
        Assert.Equal("Living room light", endpoint.Name);
    }

    [Fact]
    public void Create_ValidUnit_CopiesGatewayMediumProtocolAndUnitFromDevice()
    {
        var device = CreateCatalogDevice(OpenNettyBrand.Legrand, "03600", OpenNettyDeviceIdentifier.FromNitooSerialNumber(1234));
        var gatewayDevice = CreateCatalogDevice(OpenNettyBrand.Legrand, "88213", identifier: null, name: "Gateway");
        device.Gateway = OpenNettyGateway.Create(gatewayDevice, new IPEndPoint(IPAddress.Loopback, 20000));
        var unit = device.GetUnit(3);

        var endpoint = OpenNettyEndpoint.Create(unit, "Endpoint");

        Assert.Same(device.Gateway, endpoint.Gateway);
        Assert.Equal(OpenNettyMedium.Powerline, endpoint.Medium);
        Assert.Equal(OpenNettyProtocol.Nitoo, endpoint.Protocol);
        Assert.Same(unit, endpoint.Unit);
        Assert.False(endpoint.IsReadOnly);
    }

    private static OpenNettyEndpoint CreateEndpoint(
        string name = "Endpoint", OpenNettyProtocol protocol = OpenNettyProtocol.Nitoo, OpenNettyUnit? unit = null)
        => new() { Name = name, Protocol = protocol, Unit = unit };

    private static OpenNettyDevice CreateCatalogDevice(
        OpenNettyBrand brand, string model, OpenNettyDeviceIdentifier? identifier, string? name = null)
        => OpenNettyDevice.Create(brand, model, name ?? (identifier is null ? "Device" : null), identifier);

    private static OpenNettyUnit CreateUnit(
        (string Name, string Value)[]? deviceDefinitionSettings = null,
        (string Name, string Value)[]? unitDefinitionSettings = null,
        string? unitCapability = null)
    {
        var identity = new OpenNettyDeviceIdentity
        {
            Brand = OpenNettyBrand.Legrand,
            Collection = null,
            Descriptions = ImmutableDictionary<CultureInfo, string>.Empty,
            Model = "03600"
        };

        var definition = new OpenNettyDeviceDefinition
        {
            Id = Guid.NewGuid(),
            Identities = [identity],
            Medium = OpenNettyMedium.Powerline,
            Protocol = OpenNettyProtocol.Nitoo,
            Series = "Series",
            Settings = CreateSettings(deviceDefinitionSettings ?? [])
        };

        definition.Units =
        [
            new OpenNettyUnitDefinition
            {
                Capabilities = unitCapability is null ? [] : [new OpenNettyCapability(unitCapability)],
                Device = definition,
                Id = 0,
                Settings = CreateSettings(unitDefinitionSettings ?? [])
            }
        ];

        var device = new OpenNettyDevice { Definition = definition, Identity = identity, Name = "Device" };
        device.Units = [.. definition.Units.Select(unit => new OpenNettyUnit { Definition = unit, Device = device })];

        return device.Units[0];
    }

    private static ImmutableDictionary<OpenNettySetting, string> CreateSettings(params (string Name, string Value)[] settings)
        => settings.ToImmutableDictionary(setting => new OpenNettySetting(setting.Name), setting => setting.Value);
}
