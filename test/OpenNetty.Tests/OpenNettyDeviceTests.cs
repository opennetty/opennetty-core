/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyDeviceTests
{
    [Fact]
    public void NewInstance_HasDefaultValuesAndIsNotReadOnly()
    {
        var device = new OpenNettyDevice();

        Assert.False(device.IsReadOnly);
        Assert.Null(device.Gateway);
        Assert.Null(device.Identifier);
        Assert.Empty(device.Settings);
        Assert.Empty(device.Units);
    }

    [Fact]
    public void Properties_WhenMutable_CanBeSet()
    {
        var definition = CreateDefinition();
        var identifier = OpenNettyDeviceIdentifier.FromNitooSerialNumber(1234);

        var device = new OpenNettyDevice
        {
            Definition = definition,
            Identifier = identifier,
            Identity = definition.Identities[0],
            Name = "Device",
            Settings = CreateSettings(("Setting", "Value"))
        };

        Assert.Same(definition, device.Definition);
        Assert.Equal(identifier, device.Identifier);
        Assert.Equal(definition.Identities[0], device.Identity);
        Assert.Equal("Device", device.Name);
        Assert.Equal("Value", device.GetStringSetting(new OpenNettySetting("Setting")));
    }

    public static TheoryData<Action<OpenNettyDevice>> Mutations => new()
    {
        { device => device.Definition = new OpenNettyDeviceDefinition() },
        { device => device.Gateway = null },
        { device => device.Identifier = null },
        { device => device.Identity = default },
        { device => device.Name = "Name" },
        { device => device.Settings = ImmutableDictionary<OpenNettySetting, string>.Empty },
        { device => device.Units = [] }
    };

    [Theory]
    [MemberData(nameof(Mutations))]
    public void Properties_WhenReadOnly_ThrowInvalidOperationException(Action<OpenNettyDevice> mutation)
    {
        var device = new OpenNettyDevice();
        device.MakeReadOnly();

        var exception = Assert.Throws<InvalidOperationException>(() => mutation(device));

        Assert.Equal(SR.GetResourceString(SR.ID2021), exception.Message);
    }

    [Fact]
    public void Gateway_CanBeAssigned()
    {
        var device = CreateDevice();
        var gateway = CreateGateway();

        device.Gateway = gateway;

        Assert.Same(gateway, device.Gateway);
    }

    [Fact]
    public void Name_NullValue_ThrowsArgumentNullException()
    {
        var device = new OpenNettyDevice();

        var exception = Assert.Throws<ArgumentNullException>(() => device.Name = null!);

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Name_EmptyValue_ThrowsArgumentException()
    {
        var device = new OpenNettyDevice();

        var exception = Assert.Throws<ArgumentException>(() => device.Name = string.Empty);

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Equals_SameDefinitionIdentifierIdentityAndName_ReturnsTrue()
    {
        var definition = CreateDefinition();
        var left = CreateDevice(definition, name: "Device");
        var right = CreateDevice(definition, name: "Device");
        right.Gateway = CreateGateway();
        right.Settings = CreateSettings(("Setting", "Value"));

        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object) right));
        Assert.True(left == right);
        Assert.False(left != right);
    }

    [Fact]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var device = CreateDevice();
        var sameInstance = device;

        Assert.True(device.Equals(sameInstance));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var device = CreateDevice();

        Assert.False(device.Equals((OpenNettyDevice?) null));
        Assert.False(device.Equals((object?) null));
    }

    [Fact]
    public void Equals_DifferentDefinition_ReturnsFalse()
    {
        var left = CreateDevice(CreateDefinition());
        var right = CreateDevice(CreateDefinition());

        Assert.False(left.Equals(right));
        Assert.True(left != right);
    }

    [Fact]
    public void Equals_DifferentIdentifier_ReturnsFalse()
    {
        var definition = CreateDefinition();
        var left = CreateDevice(definition, OpenNettyDeviceIdentifier.FromNitooSerialNumber(1));
        var right = CreateDevice(definition, OpenNettyDeviceIdentifier.FromNitooSerialNumber(2));

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void Equals_IdentifierSetOnOneSideOnly_ReturnsFalse()
    {
        var definition = CreateDefinition();
        var left = CreateDevice(definition, OpenNettyDeviceIdentifier.FromNitooSerialNumber(1));
        var right = CreateDevice(definition, identifier: null);

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void Equals_DifferentIdentity_ReturnsFalse()
    {
        var definition = CreateDefinition();
        var left = CreateDevice(definition);
        var right = CreateDevice(definition);
        right.Identity = CreateIdentity("OTHER");

        Assert.False(left.Equals(right));
    }

    [Theory]
    [InlineData("Device", "device")]
    [InlineData("Device", "Other")]
    public void Equals_DifferentName_ReturnsFalse(string leftName, string rightName)
    {
        var definition = CreateDefinition();
        var left = CreateDevice(definition, name: leftName);
        var right = CreateDevice(definition, name: rightName);

        Assert.False(left.Equals(right));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("False", false)]
    [InlineData("yes", null)]
    public void GetBooleanSetting_ExistingSetting_ReturnsParsedValueOrNull(string value, bool? expected)
    {
        var device = CreateDevice();
        device.Settings = CreateSettings(("Setting", value));

        Assert.Equal(expected, device.GetBooleanSetting(new OpenNettySetting("Setting")));
    }

    [Fact]
    public void GetSettings_MissingSetting_ReturnNull()
    {
        var device = CreateDevice();
        var missing = new OpenNettySetting("Missing");

        Assert.Null(device.GetBooleanSetting(missing));
        Assert.Null(device.GetIntegerSetting(missing));
        Assert.Null(device.GetStringSetting(missing));
    }

    [Fact]
    public void GetHashCode_EqualDevices_ReturnsSameHashCode()
    {
        var definition = CreateDefinition();

        Assert.Equal(CreateDevice(definition).GetHashCode(), CreateDevice(definition).GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentNames_ReturnsDifferentHashCodes()
    {
        var definition = CreateDefinition();

        Assert.NotEqual(CreateDevice(definition, name: "First").GetHashCode(), CreateDevice(definition, name: "Second").GetHashCode());
    }

    [Theory]
    [InlineData("123", 123L)]
    [InlineData("-42", -42L)]
    [InlineData("abc", null)]
    public void GetIntegerSetting_ExistingSetting_ReturnsParsedValueOrNull(string value, long? expected)
    {
        var device = CreateDevice();
        device.Settings = CreateSettings(("Setting", value));

        Assert.Equal(expected, device.GetIntegerSetting(new OpenNettySetting("Setting")));
    }

    [Fact]
    public void GetStringSetting_ExistingSetting_ReturnsValue()
    {
        var device = CreateDevice();
        device.Settings = CreateSettings(("Setting", "Value"));

        Assert.Equal("Value", device.GetStringSetting(new OpenNettySetting("setting")));
    }

    [Fact]
    public void GetUnit_ExistingUnit_ReturnsUnit()
    {
        var device = CreateDevice(unitCount: 2);

        Assert.Same(device.Units[1], device.GetUnit(1));
    }

    [Fact]
    public void GetUnit_MissingUnit_ThrowsInvalidOperationException()
    {
        var device = CreateDevice(unitCount: 2);

        var exception = Assert.Throws<InvalidOperationException>(() => device.GetUnit(2));

        Assert.Equal(SR.FormatID0087((byte) 2), exception.Message);
    }

    [Fact]
    public void MakeReadOnly_CalledTwice_MarksInstanceAsReadOnly()
    {
        var device = new OpenNettyDevice();

        device.MakeReadOnly();
        device.MakeReadOnly();

        Assert.True(device.IsReadOnly);
    }

    [Fact]
    public void ToString_ReturnsName()
    {
        Assert.Equal("Living room", CreateDevice(name: "Living room").ToString());
    }

    [Fact]
    public void ToString_NoName_ReturnsEmptyString()
    {
        Assert.Empty(new OpenNettyDevice().ToString());
    }

    [Fact]
    public void TryGetSetting_SettingDefinedOnDevice_ReturnsDeviceValue()
    {
        var device = CreateDevice(CreateDefinition(("Setting", "Definition value")));
        device.Settings = CreateSettings(("Setting", "Device value"));

        Assert.True(device.TryGetSetting(new OpenNettySetting("Setting"), out var value));
        Assert.Equal("Device value", value);
    }

    [Fact]
    public void TryGetSetting_SettingDefinedOnDefinitionOnly_ReturnsDefinitionValue()
    {
        var device = CreateDevice(CreateDefinition(("Setting", "Definition value")));

        Assert.True(device.TryGetSetting(new OpenNettySetting("Setting"), out var value));
        Assert.Equal("Definition value", value);
    }

    [Fact]
    public void TryGetSetting_MissingSetting_ReturnsFalse()
    {
        var device = CreateDevice();

        Assert.False(device.TryGetSetting(new OpenNettySetting("Missing"), out var value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TryGetUnit_ExistingUnit_ReturnsTrueAndUnit(byte identifier)
    {
        var device = CreateDevice(unitCount: 2);

        Assert.True(device.TryGetUnit(identifier, out var unit));
        Assert.Same(device.Units[identifier], unit);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(255)]
    public void TryGetUnit_MissingUnit_ReturnsFalse(byte identifier)
    {
        var device = CreateDevice(unitCount: 2);

        Assert.False(device.TryGetUnit(identifier, out var unit));
        Assert.Null(unit);
    }

    [Fact]
    public void Create_NullModel_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => OpenNettyDevice.Create(OpenNettyBrand.Legrand, null!, "Device", null));

        Assert.Equal("model", exception.ParamName);
    }

    [Fact]
    public void Create_EmptyModel_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => OpenNettyDevice.Create(OpenNettyBrand.Legrand, string.Empty, "Device", null));

        Assert.Equal("model", exception.ParamName);
    }

    [Fact]
    public void Create_UndefinedBrand_ThrowsInvalidEnumArgumentException()
    {
        var exception = Assert.Throws<InvalidEnumArgumentException>(
            () => OpenNettyDevice.Create((OpenNettyBrand) 42, "03600", "Device", null));

        Assert.Equal("brand", exception.ParamName);
    }

    [Fact]
    public void Create_UnknownModel_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenNettyDevice.Create(OpenNettyBrand.Legrand, "unknown", "Device", null));

        Assert.Equal(SR.FormatID0085(OpenNettyBrand.Legrand, "unknown"), exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Create_NoNameAndNoIdentifier_ThrowsInvalidOperationException(string? name)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenNettyDevice.Create(OpenNettyBrand.Legrand, "03600", name, identifier: null));

        Assert.Equal(SR.GetResourceString(SR.ID0108), exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Create_NoNameButIdentifier_ComputesDefaultName(string? name)
    {
        var identifier = OpenNettyDeviceIdentifier.FromNitooSerialNumber(1234);

        var device = OpenNettyDevice.Create(OpenNettyBrand.Legrand, "03600", name, identifier);

        Assert.Equal("Legrand 03600 (001234)", device.Name);
    }

    [Fact]
    public void Create_ExplicitName_UsesName()
    {
        var device = OpenNettyDevice.Create(OpenNettyBrand.Legrand, "03600", "Living room", identifier: null);

        Assert.Equal("Living room", device.Name);
        Assert.Null(device.Identifier);
    }

    [Fact]
    public void Create_ValidModel_ResolvesDefinitionAndIdentity()
    {
        var identifier = OpenNettyDeviceIdentifier.FromNitooSerialNumber(1234);

        var device = OpenNettyDevice.Create(OpenNettyBrand.Legrand, "03600", "Device", identifier);

        Assert.Same(OpenNettyDevices.GetDeviceDefinitionByModel(OpenNettyBrand.Legrand, "03600"), device.Definition);
        Assert.Equal(device.Definition.GetIdentity(OpenNettyBrand.Legrand, "03600"), device.Identity);
        Assert.Equal(identifier, device.Identifier);
        Assert.False(device.IsReadOnly);
    }

    [Fact]
    public void Create_ModelAlias_UsesIdentityMatchingTheSpecifiedModel()
    {
        var device = OpenNettyDevice.Create(OpenNettyBrand.Legrand, "84531", "Device", identifier: null);

        Assert.Equal("84531", device.Identity.Model);
        Assert.Same(OpenNettyDevices.GetDeviceDefinitionByModel(OpenNettyBrand.Legrand, "67201"), device.Definition);
    }

    [Fact]
    public void Create_ValidModel_CreatesOneUnitPerUnitDefinitionLinkedToTheDevice()
    {
        var device = OpenNettyDevice.Create(OpenNettyBrand.Legrand, "03600", "Device", identifier: null);

        Assert.Equal(device.Definition.Units.Length, device.Units.Length);

        for (var index = 0; index < device.Units.Length; index++)
        {
            Assert.Same(device.Definition.Units[index], device.Units[index].Definition);
            Assert.Same(device, device.Units[index].Device);
        }
    }

    private static OpenNettyDevice CreateDevice(
        OpenNettyDeviceDefinition? definition = null,
        OpenNettyDeviceIdentifier? identifier = null,
        string name = "Device",
        int unitCount = 1)
    {
        definition ??= CreateDefinition();

        var device = new OpenNettyDevice
        {
            Definition = definition,
            Identifier = identifier,
            Identity = definition.Identities[0],
            Name = name
        };

        device.Units =
        [
            .. Enumerable.Range(0, unitCount).Select(index => new OpenNettyUnit
            {
                Definition = new OpenNettyUnitDefinition { Device = definition, Id = (byte) index },
                Device = device
            })
        ];

        return device;
    }

    private static OpenNettyDeviceDefinition CreateDefinition(params (string Name, string Value)[] settings) => new()
    {
        Id = Guid.NewGuid(),
        Identities = [CreateIdentity("03600")],
        Medium = OpenNettyMedium.Powerline,
        Protocol = OpenNettyProtocol.Nitoo,
        Series = "Series",
        Settings = CreateSettings(settings)
    };

    private static OpenNettyDeviceIdentity CreateIdentity(string model) => new()
    {
        Brand = OpenNettyBrand.Legrand,
        Collection = null,
        Descriptions = ImmutableDictionary<CultureInfo, string>.Empty,
        Model = model
    };

    private static OpenNettyGateway CreateGateway()
    {
        var device = OpenNettyDevice.Create(OpenNettyBrand.Legrand, "88213", "Gateway", identifier: null);

        return OpenNettyGateway.Create(device, new IPEndPoint(IPAddress.Loopback, 20000));
    }

    private static ImmutableDictionary<OpenNettySetting, string> CreateSettings(params (string Name, string Value)[] settings)
        => settings.ToImmutableDictionary(setting => new OpenNettySetting(setting.Name), setting => setting.Value);
}
