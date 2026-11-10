/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Globalization;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyUnitDefinitionTests
{
    [Fact]
    public void NewInstance_HasDefaultValuesAndIsNotReadOnly()
    {
        var definition = new OpenNettyUnitDefinition();

        Assert.False(definition.IsReadOnly);
        Assert.Null(definition.AssociatedUnitId);
        Assert.Empty(definition.Capabilities);
        Assert.Empty(definition.Descriptions);
        Assert.Empty(definition.Settings);
        Assert.Equal(0, definition.Id);
    }

    [Fact]
    public void Properties_WhenMutable_CanBeSet()
    {
        var device = new OpenNettyDeviceDefinition();
        var capability = new OpenNettyCapability("Capability");

        var definition = new OpenNettyUnitDefinition
        {
            AssociatedUnitId = 3,
            Capabilities = [capability],
            Device = device,
            Id = 1,
            Settings = CreateSettings(("Name", "Value"))
        };

        Assert.Equal((byte) 3, definition.AssociatedUnitId);
        Assert.Contains(capability, definition.Capabilities);
        Assert.Same(device, definition.Device);
        Assert.Equal(1, definition.Id);
        Assert.Equal("Value", definition.GetStringSetting(new OpenNettySetting("Name")));
    }

    public static TheoryData<Action<OpenNettyUnitDefinition>> Mutations => new()
    {
        { definition => definition.AssociatedUnitId = 1 },
        { definition => definition.Capabilities = [] },
        { definition => definition.Descriptions = ImmutableDictionary<CultureInfo, string>.Empty },
        { definition => definition.Device = new OpenNettyDeviceDefinition() },
        { definition => definition.Id = 1 },
        { definition => definition.Settings = ImmutableDictionary<OpenNettySetting, string>.Empty }
    };

    [Theory]
    [MemberData(nameof(Mutations))]
    public void Properties_WhenReadOnly_ThrowInvalidOperationException(Action<OpenNettyUnitDefinition> mutation)
    {
        var definition = new OpenNettyUnitDefinition();
        definition.MakeReadOnly();

        var exception = Assert.Throws<InvalidOperationException>(() => mutation(definition));

        Assert.Equal(SR.GetResourceString(SR.ID2021), exception.Message);
    }

    [Fact]
    public void Equals_SameDeviceAndId_ReturnsTrueRegardlessOfOtherProperties()
    {
        var device = CreateDeviceDefinition();
        var left = new OpenNettyUnitDefinition { Device = device, Id = 1, AssociatedUnitId = 2 };
        var right = new OpenNettyUnitDefinition { Device = device, Id = 1, AssociatedUnitId = 3 };

        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object) right));
        Assert.True(left == right);
        Assert.False(left != right);
    }

    [Fact]
    public void Equals_DeviceDefinitionsWithSameId_ReturnsTrue()
    {
        var id = Guid.NewGuid();
        var left = new OpenNettyUnitDefinition { Device = CreateDeviceDefinition(id), Id = 1 };
        var right = new OpenNettyUnitDefinition { Device = CreateDeviceDefinition(id), Id = 1 };

        Assert.True(left.Equals(right));
    }

    [Fact]
    public void Equals_DifferentId_ReturnsFalse()
    {
        var device = CreateDeviceDefinition();
        var left = new OpenNettyUnitDefinition { Device = device, Id = 1 };
        var right = new OpenNettyUnitDefinition { Device = device, Id = 2 };

        Assert.False(left.Equals(right));
        Assert.True(left != right);
    }

    [Fact]
    public void Equals_DifferentDevice_ReturnsFalse()
    {
        var left = new OpenNettyUnitDefinition { Device = CreateDeviceDefinition(), Id = 1 };
        var right = new OpenNettyUnitDefinition { Device = CreateDeviceDefinition(), Id = 1 };

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var definition = new OpenNettyUnitDefinition { Device = CreateDeviceDefinition(), Id = 1 };
        var sameInstance = definition;

        Assert.True(definition.Equals(sameInstance));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var definition = new OpenNettyUnitDefinition { Device = CreateDeviceDefinition(), Id = 1 };

        Assert.False(definition.Equals((OpenNettyUnitDefinition?) null));
        Assert.False(definition.Equals((object?) null));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("False", false)]
    [InlineData("yes", null)]
    public void GetBooleanSetting_ExistingSetting_ReturnsParsedValueOrNull(string value, bool? expected)
    {
        var definition = new OpenNettyUnitDefinition { Settings = CreateSettings(("Setting", value)) };

        Assert.Equal(expected, definition.GetBooleanSetting(new OpenNettySetting("Setting")));
    }

    [Fact]
    public void GetSettings_MissingSetting_ReturnNull()
    {
        var definition = new OpenNettyUnitDefinition { Settings = CreateSettings(("Setting", "true")) };
        var missing = new OpenNettySetting("Missing");

        Assert.Null(definition.GetBooleanSetting(missing));
        Assert.Null(definition.GetIntegerSetting(missing));
        Assert.Null(definition.GetStringSetting(missing));
    }

    [Theory]
    [InlineData("en", "English")]
    [InlineData("fr", "Français")]
    [InlineData("fr-FR", "Français")]
    [InlineData("en-GB", "English")]
    [InlineData("de-DE", "English")]
    [InlineData("", "English")]
    public void GetDescription_AvailableDescriptions_ReturnsBestMatch(string culture, string expected)
    {
        var definition = new OpenNettyUnitDefinition { Descriptions = CreateDescriptions(("en", "English"), ("fr", "Français")) };

        Assert.Equal(expected, definition.GetDescription(CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void GetDescription_SpecificCultureDescription_IsPreferredOverParentCulture()
    {
        var definition = new OpenNettyUnitDefinition { Descriptions = CreateDescriptions(("fr", "Français"), ("fr-CA", "Canadien")) };

        Assert.Equal("Canadien", definition.GetDescription(CultureInfo.GetCultureInfo("fr-CA")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("de-DE")]
    public void GetDescription_NoMatchingDescriptionAndNoEnglishFallback_ReturnsNull(string culture)
    {
        var definition = new OpenNettyUnitDefinition { Descriptions = CreateDescriptions(("fr", "Français")) };

        Assert.Null(definition.GetDescription(CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void GetDescription_NullCulture_ThrowsArgumentNullException()
    {
        var definition = new OpenNettyUnitDefinition();

        var exception = Assert.Throws<ArgumentNullException>(() => definition.GetDescription(null!));

        Assert.Equal("culture", exception.ParamName);
    }

    [Fact]
    public void GetHashCode_EqualDefinitions_ReturnsSameHashCode()
    {
        var device = CreateDeviceDefinition();
        var left = new OpenNettyUnitDefinition { Device = device, Id = 1, AssociatedUnitId = 2 };
        var right = new OpenNettyUnitDefinition { Device = device, Id = 1 };

        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentIds_ReturnsDifferentHashCodes()
    {
        var device = CreateDeviceDefinition();
        var left = new OpenNettyUnitDefinition { Device = device, Id = 1 };
        var right = new OpenNettyUnitDefinition { Device = device, Id = 2 };

        Assert.NotEqual(left.GetHashCode(), right.GetHashCode());
    }

    [Theory]
    [InlineData("123", 123L)]
    [InlineData("-42", -42L)]
    [InlineData("abc", null)]
    [InlineData("1.5", null)]
    public void GetIntegerSetting_ExistingSetting_ReturnsParsedValueOrNull(string value, long? expected)
    {
        var definition = new OpenNettyUnitDefinition { Settings = CreateSettings(("Setting", value)) };

        Assert.Equal(expected, definition.GetIntegerSetting(new OpenNettySetting("Setting")));
    }

    [Fact]
    public void GetStringSetting_ExistingSetting_ReturnsValue()
    {
        var definition = new OpenNettyUnitDefinition { Settings = CreateSettings(("Setting", "Value")) };

        Assert.Equal("Value", definition.GetStringSetting(new OpenNettySetting("SETTING")));
    }

    [Theory]
    [InlineData("Capability", true)]
    [InlineData("capability", true)]
    [InlineData("Other capability", false)]
    public void HasCapability_ReturnsWhetherCapabilityIsSupported(string name, bool expected)
    {
        var definition = new OpenNettyUnitDefinition { Capabilities = [new OpenNettyCapability("Capability")] };

        Assert.Equal(expected, definition.HasCapability(new OpenNettyCapability(name)));
    }

    [Fact]
    public void MakeReadOnly_MarksInstanceAsReadOnly()
    {
        var definition = new OpenNettyUnitDefinition();

        definition.MakeReadOnly();
        definition.MakeReadOnly();

        Assert.True(definition.IsReadOnly);
    }

    [Fact]
    public void TryGetSetting_ExistingSetting_ReturnsTrueAndValue()
    {
        var definition = new OpenNettyUnitDefinition { Settings = CreateSettings(("Setting", "Value")) };

        Assert.True(definition.TryGetSetting(new OpenNettySetting("Setting"), out var value));
        Assert.Equal("Value", value);
    }

    [Fact]
    public void TryGetSetting_MissingSetting_ReturnsFalse()
    {
        var definition = new OpenNettyUnitDefinition();

        Assert.False(definition.TryGetSetting(new OpenNettySetting("Missing"), out var value));
        Assert.Null(value);
    }

    [Fact]
    public void TryGetSetting_SettingDefinedOnlyOnDeviceDefinition_ReturnsFalse()
    {
        var device = CreateDeviceDefinition();
        device.Settings = CreateSettings(("Setting", "Value"));
        var definition = new OpenNettyUnitDefinition { Device = device };

        Assert.False(definition.TryGetSetting(new OpenNettySetting("Setting"), out _));
    }

    private static OpenNettyDeviceDefinition CreateDeviceDefinition(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Medium = OpenNettyMedium.Powerline,
        Protocol = OpenNettyProtocol.Nitoo,
        Series = "Series"
    };

    private static ImmutableDictionary<OpenNettySetting, string> CreateSettings(params (string Name, string Value)[] settings)
        => settings.ToImmutableDictionary(setting => new OpenNettySetting(setting.Name), setting => setting.Value);

    private static ImmutableDictionary<CultureInfo, string> CreateDescriptions(params (string Culture, string Value)[] descriptions)
        => descriptions.ToImmutableDictionary(description => CultureInfo.GetCultureInfo(description.Culture), description => description.Value);
}
