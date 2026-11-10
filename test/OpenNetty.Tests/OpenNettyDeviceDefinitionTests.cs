/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyDeviceDefinitionTests
{
    [Fact]
    public void NewInstance_HasEmptyCollectionsAndIsNotReadOnly()
    {
        var definition = new OpenNettyDeviceDefinition();

        Assert.False(definition.IsReadOnly);
        Assert.Empty(definition.Identities);
        Assert.Empty(definition.Settings);
        Assert.Empty(definition.Units);
        Assert.Equal(Guid.Empty, definition.Id);
    }

    [Fact]
    public void Properties_WhenMutable_CanBeSet()
    {
        var id = Guid.NewGuid();
        var identity = CreateIdentity(OpenNettyBrand.Legrand, "03600");

        var definition = new OpenNettyDeviceDefinition
        {
            Id = id,
            Identities = [identity],
            Medium = OpenNettyMedium.Powerline,
            Protocol = OpenNettyProtocol.Nitoo,
            Series = "In One by Legrand",
            Settings = CreateSettings(("Name", "Value"))
        };

        Assert.Equal(id, definition.Id);
        Assert.Equal([identity], definition.Identities);
        Assert.Equal(OpenNettyMedium.Powerline, definition.Medium);
        Assert.Equal(OpenNettyProtocol.Nitoo, definition.Protocol);
        Assert.Equal("In One by Legrand", definition.Series);
        Assert.Equal("Value", definition.GetStringSetting(new OpenNettySetting("Name")));
    }

    public static TheoryData<Action<OpenNettyDeviceDefinition>> Mutations => new()
    {
        { definition => definition.Id = Guid.NewGuid() },
        { definition => definition.Identities = [] },
        { definition => definition.Medium = OpenNettyMedium.Radio },
        { definition => definition.Protocol = OpenNettyProtocol.Zigbee },
        { definition => definition.Series = "Series" },
        { definition => definition.Settings = ImmutableDictionary<OpenNettySetting, string>.Empty },
        { definition => definition.Units = [] }
    };

    [Theory]
    [MemberData(nameof(Mutations))]
    public void Properties_WhenReadOnly_ThrowInvalidOperationException(Action<OpenNettyDeviceDefinition> mutation)
    {
        var definition = new OpenNettyDeviceDefinition();
        definition.MakeReadOnly();

        var exception = Assert.Throws<InvalidOperationException>(() => mutation(definition));

        Assert.Equal(SR.GetResourceString(SR.ID2021), exception.Message);
    }

    [Fact]
    public void Series_NullValue_ThrowsArgumentNullException()
    {
        var definition = new OpenNettyDeviceDefinition();

        var exception = Assert.Throws<ArgumentNullException>(() => definition.Series = null!);

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Series_EmptyValue_ThrowsArgumentException()
    {
        var definition = new OpenNettyDeviceDefinition();

        var exception = Assert.Throws<ArgumentException>(() => definition.Series = string.Empty);

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Equals_SameId_ReturnsTrueRegardlessOfOtherProperties()
    {
        var id = Guid.NewGuid();
        var left = CreateDefinition(id, series: "First");
        var right = CreateDefinition(id, series: "Second");

        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object) right));
        Assert.True(left == right);
        Assert.False(left != right);
    }

    [Fact]
    public void Equals_DifferentId_ReturnsFalse()
    {
        var left = CreateDefinition(Guid.NewGuid());
        var right = CreateDefinition(Guid.NewGuid());

        Assert.False(left.Equals(right));
        Assert.True(left != right);
    }

    [Fact]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var definition = CreateDefinition(Guid.NewGuid());
        var sameInstance = definition;

        Assert.True(definition.Equals(sameInstance));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var definition = CreateDefinition(Guid.NewGuid());

        Assert.False(definition.Equals((OpenNettyDeviceDefinition?) null));
        Assert.False(definition.Equals((object?) null));
    }

    [Fact]
    public void GetHashCode_SameId_ReturnsSameHashCode()
    {
        var id = Guid.NewGuid();

        Assert.Equal(CreateDefinition(id).GetHashCode(), CreateDefinition(id, series: "Other").GetHashCode());
    }

    [Fact]
    public void GetHashCode_ReturnsIdHashCode()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id.GetHashCode(), CreateDefinition(id).GetHashCode());
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("yes", null)]
    [InlineData("", null)]
    public void GetBooleanSetting_ExistingSetting_ReturnsParsedValueOrNull(string value, bool? expected)
    {
        var definition = CreateDefinition(settings: CreateSettings(("Setting", value)));

        Assert.Equal(expected, definition.GetBooleanSetting(new OpenNettySetting("Setting")));
    }

    [Fact]
    public void GetSettings_MissingSetting_ReturnNull()
    {
        var definition = CreateDefinition(settings: CreateSettings(("Setting", "true")));
        var missing = new OpenNettySetting("Missing");

        Assert.Null(definition.GetBooleanSetting(missing));
        Assert.Null(definition.GetIntegerSetting(missing));
        Assert.Null(definition.GetStringSetting(missing));
    }

    [Fact]
    public void GetIdentity_MatchingBrandAndModel_ReturnsIdentity()
    {
        var definition = CreateDefinitionWithIdentities();

        var identity = definition.GetIdentity(OpenNettyBrand.BTicino, "f454");

        Assert.Equal(OpenNettyBrand.BTicino, identity.Brand);
        Assert.Equal("F454", identity.Model);
    }

    [Fact]
    public void GetIdentity_NoMatchingIdentity_ThrowsInvalidOperationException()
    {
        var definition = CreateDefinitionWithIdentities();

        var exception = Assert.Throws<InvalidOperationException>(() => definition.GetIdentity(OpenNettyBrand.BTicino, "03600"));

        Assert.Equal(SR.GetResourceString(SR.ID0127), exception.Message);
    }

    [Theory]
    [InlineData("123", 123L)]
    [InlineData("-42", -42L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("1.5", null)]
    [InlineData("abc", null)]
    [InlineData("9223372036854775808", null)]
    public void GetIntegerSetting_ExistingSetting_ReturnsParsedValueOrNull(string value, long? expected)
    {
        var definition = CreateDefinition(settings: CreateSettings(("Setting", value)));

        Assert.Equal(expected, definition.GetIntegerSetting(new OpenNettySetting("Setting")));
    }

    [Fact]
    public void GetStringSetting_ExistingSetting_ReturnsValue()
    {
        var definition = CreateDefinition(settings: CreateSettings(("Setting", "Value")));

        Assert.Equal("Value", definition.GetStringSetting(new OpenNettySetting("setting")));
    }

    [Fact]
    public void GetUnitDefinition_ExistingUnit_ReturnsDefinition()
    {
        var definition = CreateDefinitionWithUnits(3);

        Assert.Same(definition.Units[2], definition.GetUnitDefinition(2));
    }

    [Fact]
    public void GetUnitDefinition_MissingUnit_ThrowsInvalidOperationException()
    {
        var definition = CreateDefinitionWithUnits(3);

        var exception = Assert.Throws<InvalidOperationException>(() => definition.GetUnitDefinition(3));

        Assert.Equal(SR.FormatID0087((byte) 3), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyBrand.Legrand, "03600", true)]
    [InlineData(OpenNettyBrand.Legrand, "ABC", true)]
    [InlineData(OpenNettyBrand.Legrand, "abc", true)]
    [InlineData(OpenNettyBrand.BTicino, "F454", true)]
    [InlineData(OpenNettyBrand.BTicino, "f454", true)]
    [InlineData(OpenNettyBrand.BTicino, "03600", false)]
    [InlineData(OpenNettyBrand.Legrand, "F454", false)]
    [InlineData(OpenNettyBrand.Legrand, "0360", false)]
    public void HasIdentity_ReturnsWhetherBrandAndModelMatchAnIdentity(OpenNettyBrand brand, string model, bool expected)
    {
        var definition = CreateDefinitionWithIdentities();

        Assert.Equal(expected, definition.HasIdentity(brand, model));
    }

    [Fact]
    public void HasIdentity_UndefinedBrand_ThrowsInvalidEnumArgumentException()
    {
        var definition = CreateDefinitionWithIdentities();

        var exception = Assert.Throws<InvalidEnumArgumentException>(() => definition.HasIdentity((OpenNettyBrand) 42, "03600"));

        Assert.Equal("brand", exception.ParamName);
    }

    [Fact]
    public void HasIdentity_NullModel_ThrowsArgumentNullException()
    {
        var definition = CreateDefinitionWithIdentities();

        var exception = Assert.Throws<ArgumentNullException>(() => definition.HasIdentity(OpenNettyBrand.Legrand, null!));

        Assert.Equal("model", exception.ParamName);
    }

    [Fact]
    public void HasIdentity_EmptyModel_ThrowsArgumentException()
    {
        var definition = CreateDefinitionWithIdentities();

        var exception = Assert.Throws<ArgumentException>(() => definition.HasIdentity(OpenNettyBrand.Legrand, string.Empty));

        Assert.Equal("model", exception.ParamName);
    }

    [Fact]
    public void MakeReadOnly_MarksInstanceAsReadOnly()
    {
        var definition = new OpenNettyDeviceDefinition();

        definition.MakeReadOnly();

        Assert.True(definition.IsReadOnly);
    }

    [Fact]
    public void MakeReadOnly_CalledTwice_IsIdempotent()
    {
        var definition = new OpenNettyDeviceDefinition();

        definition.MakeReadOnly();
        definition.MakeReadOnly();

        Assert.True(definition.IsReadOnly);
    }

    [Theory]
    [InlineData(OpenNettyBrand.Legrand, "03600")]
    [InlineData(OpenNettyBrand.Legrand, "abc")]
    [InlineData(OpenNettyBrand.BTicino, "F454")]
    public void TryGetIdentity_MatchingBrandAndModel_ReturnsTrueAndIdentity(OpenNettyBrand brand, string model)
    {
        var definition = CreateDefinitionWithIdentities();

        Assert.True(definition.TryGetIdentity(brand, model, out var identity));
        Assert.Equal(brand, identity!.Value.Brand);
        Assert.Equal(model, identity.Value.Model, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(OpenNettyBrand.BTicino, "03600")]
    [InlineData(OpenNettyBrand.Legrand, "F454")]
    public void TryGetIdentity_NoMatchingIdentity_ReturnsFalse(OpenNettyBrand brand, string model)
    {
        var definition = CreateDefinitionWithIdentities();

        Assert.False(definition.TryGetIdentity(brand, model, out var identity));
        Assert.Null(identity);
    }

    [Fact]
    public void TryGetIdentity_UndefinedBrand_ThrowsInvalidEnumArgumentException()
    {
        var definition = CreateDefinitionWithIdentities();

        var exception = Assert.Throws<InvalidEnumArgumentException>(
            () => definition.TryGetIdentity((OpenNettyBrand) 42, "03600", out _));

        Assert.Equal("brand", exception.ParamName);
    }

    [Fact]
    public void TryGetIdentity_NullModel_ThrowsArgumentNullException()
    {
        var definition = CreateDefinitionWithIdentities();

        var exception = Assert.Throws<ArgumentNullException>(
            () => definition.TryGetIdentity(OpenNettyBrand.Legrand, null!, out _));

        Assert.Equal("model", exception.ParamName);
    }

    [Fact]
    public void TryGetIdentity_EmptyModel_ThrowsArgumentException()
    {
        var definition = CreateDefinitionWithIdentities();

        var exception = Assert.Throws<ArgumentException>(
            () => definition.TryGetIdentity(OpenNettyBrand.Legrand, string.Empty, out _));

        Assert.Equal("model", exception.ParamName);
    }

    [Fact]
    public void TryGetSetting_ExistingSetting_ReturnsTrueAndValue()
    {
        var definition = CreateDefinition(settings: CreateSettings(("Setting", "Value")));

        Assert.True(definition.TryGetSetting(new OpenNettySetting("Setting"), out var value));
        Assert.Equal("Value", value);
    }

    [Fact]
    public void TryGetSetting_MissingSetting_ReturnsFalse()
    {
        var definition = CreateDefinition();

        Assert.False(definition.TryGetSetting(new OpenNettySetting("Missing"), out var value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TryGetUnitDefinition_ExistingUnit_ReturnsTrueAndDefinition(byte identifier)
    {
        var definition = CreateDefinitionWithUnits(3);

        Assert.True(definition.TryGetUnitDefinition(identifier, out var unit));
        Assert.Same(definition.Units[identifier], unit);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(255)]
    public void TryGetUnitDefinition_MissingUnit_ReturnsFalse(byte identifier)
    {
        var definition = CreateDefinitionWithUnits(3);

        Assert.False(definition.TryGetUnitDefinition(identifier, out var unit));
        Assert.Null(unit);
    }

    private static OpenNettyDeviceDefinition CreateDefinition(
        Guid? id = null,
        string series = "Series",
        ImmutableDictionary<OpenNettySetting, string>? settings = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            Medium = OpenNettyMedium.Powerline,
            Protocol = OpenNettyProtocol.Nitoo,
            Series = series,
            Settings = settings ?? ImmutableDictionary<OpenNettySetting, string>.Empty
        };

    private static OpenNettyDeviceDefinition CreateDefinitionWithIdentities()
    {
        var definition = CreateDefinition();
        definition.Identities =
        [
            CreateIdentity(OpenNettyBrand.Legrand, "03600"),
            CreateIdentity(OpenNettyBrand.Legrand, "ABC"),
            CreateIdentity(OpenNettyBrand.BTicino, "F454")
        ];

        return definition;
    }

    private static OpenNettyDeviceDefinition CreateDefinitionWithUnits(byte count)
    {
        var definition = CreateDefinition();
        definition.Units =
        [
            .. Enumerable.Range(0, count).Select(index => new OpenNettyUnitDefinition { Device = definition, Id = (byte) index })
        ];

        return definition;
    }

    private static OpenNettyDeviceIdentity CreateIdentity(OpenNettyBrand brand, string model) => new()
    {
        Brand = brand,
        Collection = null,
        Descriptions = ImmutableDictionary<CultureInfo, string>.Empty,
        Model = model
    };

    private static ImmutableDictionary<OpenNettySetting, string> CreateSettings(params (string Name, string Value)[] settings)
        => settings.ToImmutableDictionary(setting => new OpenNettySetting(setting.Name), setting => setting.Value);
}
