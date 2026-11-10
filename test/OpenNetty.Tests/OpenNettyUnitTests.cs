/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Globalization;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyUnitTests
{
    [Fact]
    public void NewInstance_HasDefaultValuesAndIsNotReadOnly()
    {
        var unit = new OpenNettyUnit();

        Assert.False(unit.IsReadOnly);
        Assert.Empty(unit.Scenarios);
        Assert.Empty(unit.Settings);
    }

    [Fact]
    public void Properties_WhenMutable_CanBeSet()
    {
        var device = CreateDevice();
        var definition = device.Units[1].Definition;
        var scenario = new OpenNettyScenario { EndpointName = "Endpoint", FunctionCode = 1 };

        var unit = new OpenNettyUnit
        {
            Definition = definition,
            Device = device,
            Scenarios = [scenario],
            Settings = CreateSettings(("Setting", "Value"))
        };

        Assert.Same(definition, unit.Definition);
        Assert.Same(device, unit.Device);
        Assert.Equal([scenario], unit.Scenarios);
        Assert.Equal("Value", unit.GetStringSetting(new OpenNettySetting("Setting")));
    }

    public static TheoryData<Action<OpenNettyUnit>> Mutations => new()
    {
        { unit => unit.Definition = new OpenNettyUnitDefinition() },
        { unit => unit.Device = new OpenNettyDevice() },
        { unit => unit.Scenarios = [] },
        { unit => unit.Settings = ImmutableDictionary<OpenNettySetting, string>.Empty }
    };

    [Theory]
    [MemberData(nameof(Mutations))]
    public void Properties_WhenReadOnly_ThrowInvalidOperationException(Action<OpenNettyUnit> mutation)
    {
        var unit = new OpenNettyUnit();
        unit.MakeReadOnly();

        var exception = Assert.Throws<InvalidOperationException>(() => mutation(unit));

        Assert.Equal(SR.GetResourceString(SR.ID2021), exception.Message);
    }

    [Fact]
    public void Equals_SameDefinitionAndDevice_ReturnsTrueRegardlessOfOtherProperties()
    {
        var device = CreateDevice();
        var left = new OpenNettyUnit { Definition = device.Units[1].Definition, Device = device };
        var right = new OpenNettyUnit { Definition = device.Units[1].Definition, Device = device };
        right.Settings = CreateSettings(("Setting", "Value"));

        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object) right));
        Assert.True(left == right);
        Assert.False(left != right);
    }

    [Fact]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var unit = CreateDevice().Units[1];
        var sameInstance = unit;

        Assert.True(unit.Equals(sameInstance));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var unit = CreateDevice().Units[1];

        Assert.False(unit.Equals((OpenNettyUnit?) null));
        Assert.False(unit.Equals((object?) null));
    }

    [Fact]
    public void Equals_DifferentDefinition_ReturnsFalse()
    {
        var device = CreateDevice();

        Assert.False(device.Units[0].Equals(device.Units[1]));
        Assert.True(device.Units[0] != device.Units[1]);
    }

    [Fact]
    public void Equals_SameDefinitionOnDifferentDevices_ReturnsFalse()
    {
        var first = CreateDevice(name: "First");
        var second = CreateDevice(first.Definition, name: "Second");

        var left = new OpenNettyUnit { Definition = first.Units[1].Definition, Device = first };
        var right = new OpenNettyUnit { Definition = first.Units[1].Definition, Device = second };

        Assert.False(left.Equals(right));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("False", false)]
    [InlineData("yes", null)]
    public void GetBooleanSetting_ExistingSetting_ReturnsParsedValueOrNull(string value, bool? expected)
    {
        var unit = CreateDevice().Units[1];
        unit.Settings = CreateSettings(("Setting", value));

        Assert.Equal(expected, unit.GetBooleanSetting(new OpenNettySetting("Setting")));
    }

    [Fact]
    public void GetSettings_MissingSetting_ReturnNull()
    {
        var unit = CreateDevice().Units[1];
        var missing = new OpenNettySetting("Missing");

        Assert.Null(unit.GetBooleanSetting(missing));
        Assert.Null(unit.GetIntegerSetting(missing));
        Assert.Null(unit.GetStringSetting(missing));
    }

    [Fact]
    public void GetHashCode_EqualUnits_ReturnsSameHashCode()
    {
        var device = CreateDevice();
        var left = new OpenNettyUnit { Definition = device.Units[1].Definition, Device = device };
        var right = new OpenNettyUnit { Definition = device.Units[1].Definition, Device = device };

        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentDefinitions_ReturnsDifferentHashCodes()
    {
        var device = CreateDevice();

        Assert.NotEqual(device.Units[0].GetHashCode(), device.Units[1].GetHashCode());
    }

    [Theory]
    [InlineData("123", 123L)]
    [InlineData("-42", -42L)]
    [InlineData("abc", null)]
    public void GetIntegerSetting_ExistingSetting_ReturnsParsedValueOrNull(string value, long? expected)
    {
        var unit = CreateDevice().Units[1];
        unit.Settings = CreateSettings(("Setting", value));

        Assert.Equal(expected, unit.GetIntegerSetting(new OpenNettySetting("Setting")));
    }

    [Fact]
    public void GetStringSetting_ExistingSetting_ReturnsValue()
    {
        var unit = CreateDevice().Units[1];
        unit.Settings = CreateSettings(("Setting", "Value"));

        Assert.Equal("Value", unit.GetStringSetting(new OpenNettySetting("SETTING")));
    }

    [Theory]
    [InlineData("Capability", true)]
    [InlineData("capability", true)]
    [InlineData("Other capability", false)]
    public void HasCapability_ReturnsWhetherUnitDefinitionSupportsCapability(string name, bool expected)
    {
        var unit = CreateDevice().Units[1];

        Assert.Equal(expected, unit.HasCapability(new OpenNettyCapability(name)));
    }

    [Fact]
    public void TryGetSetting_SettingDefinedOnUnit_ReturnsUnitValue()
    {
        var unit = CreateDevice(unitSettings: [("Setting", "Definition value")]).Units[1];
        unit.Settings = CreateSettings(("Setting", "Unit value"));

        Assert.True(unit.TryGetSetting(new OpenNettySetting("Setting"), out var value));
        Assert.Equal("Unit value", value);
    }

    [Fact]
    public void TryGetSetting_SettingDefinedOnUnitDefinitionOnly_ReturnsDefinitionValue()
    {
        var unit = CreateDevice(unitSettings: [("Setting", "Definition value")]).Units[1];

        Assert.True(unit.TryGetSetting(new OpenNettySetting("Setting"), out var value));
        Assert.Equal("Definition value", value);
    }

    [Fact]
    public void TryGetSetting_MissingSetting_ReturnsFalse()
    {
        var unit = CreateDevice().Units[1];

        Assert.False(unit.TryGetSetting(new OpenNettySetting("Missing"), out var value));
        Assert.Null(value);
    }

    [Fact]
    public void TryGetSetting_SettingDefinedOnDevice_ReturnsFalse()
    {
        var device = CreateDevice();
        device.Settings = CreateSettings(("Setting", "Device value"));

        Assert.False(device.Units[1].TryGetSetting(new OpenNettySetting("Setting"), out _));
    }

    [Fact]
    public void MakeReadOnly_CalledTwice_MarksInstanceAsReadOnly()
    {
        var unit = new OpenNettyUnit();

        unit.MakeReadOnly();
        unit.MakeReadOnly();

        Assert.True(unit.IsReadOnly);
    }

    private static OpenNettyDevice CreateDevice(
        OpenNettyDeviceDefinition? definition = null,
        string name = "Device",
        (string Name, string Value)[]? unitSettings = null)
    {
        var identity = new OpenNettyDeviceIdentity
        {
            Brand = OpenNettyBrand.Legrand,
            Collection = null,
            Descriptions = ImmutableDictionary<CultureInfo, string>.Empty,
            Model = "03600"
        };

        if (definition is null)
        {
            definition = new OpenNettyDeviceDefinition
            {
                Id = Guid.NewGuid(),
                Identities = [identity],
                Medium = OpenNettyMedium.Powerline,
                Protocol = OpenNettyProtocol.Nitoo,
                Series = "Series"
            };

            definition.Units =
            [
                new OpenNettyUnitDefinition { Device = definition, Id = 0 },
                new OpenNettyUnitDefinition
                {
                    Capabilities = [new OpenNettyCapability("Capability")],
                    Device = definition,
                    Id = 1,
                    Settings = CreateSettings(unitSettings ?? [])
                }
            ];
        }

        var device = new OpenNettyDevice { Definition = definition, Identity = identity, Name = name };
        device.Units = [.. definition.Units.Select(unit => new OpenNettyUnit { Definition = unit, Device = device })];

        return device;
    }

    private static ImmutableDictionary<OpenNettySetting, string> CreateSettings(params (string Name, string Value)[] settings)
        => settings.ToImmutableDictionary(setting => new OpenNettySetting(setting.Name), setting => setting.Value);
}
