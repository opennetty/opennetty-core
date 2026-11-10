/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyDevicesTests
{
    [Fact]
    public void GetDeviceDefinitionByModel_KnownModel_ReturnsSameDefinitionAsTryGet()
    {
        OpenNettyDevices.TryGetDeviceDefinitionByModel(OpenNettyBrand.Legrand, "03600", out var expected);

        var definition = OpenNettyDevices.GetDeviceDefinitionByModel(OpenNettyBrand.Legrand, "03600");

        Assert.Same(expected, definition);
    }

    [Fact]
    public void GetDeviceDefinitionByModel_KnownModel_ReturnsCachedInstance()
    {
        var first = OpenNettyDevices.GetDeviceDefinitionByModel(OpenNettyBrand.Legrand, "03600");
        var second = OpenNettyDevices.GetDeviceDefinitionByModel(OpenNettyBrand.Legrand, "03600");

        Assert.Same(first, second);
    }

    [Fact]
    public void GetDeviceDefinitionByModel_UnknownModel_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenNettyDevices.GetDeviceDefinitionByModel(OpenNettyBrand.BTicino, "unknown"));

        Assert.Equal(SR.FormatID0085(OpenNettyBrand.BTicino, "unknown"), exception.Message);
    }

    [Fact]
    public void GetDeviceDefinitionByModel_NullModel_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => OpenNettyDevices.GetDeviceDefinitionByModel(OpenNettyBrand.Legrand, null!));

        Assert.Equal("model", exception.ParamName);
    }

    [Fact]
    public void GetDeviceDefinitionByModel_UndefinedBrand_ThrowsInvalidEnumArgumentException()
    {
        var exception = Assert.Throws<InvalidEnumArgumentException>(
            () => OpenNettyDevices.GetDeviceDefinitionByModel((OpenNettyBrand) 42, "03600"));

        Assert.Equal("brand", exception.ParamName);
    }

    [Theory]
    [InlineData(OpenNettyBrand.BTicino, "F454", OpenNettyBrand.Legrand, "03598")]
    [InlineData(OpenNettyBrand.BTicino, "F411U1", OpenNettyBrand.Legrand, "03847")]
    [InlineData(OpenNettyBrand.Legrand, "67201", OpenNettyBrand.Legrand, "84531")]
    public void GetDeviceDefinitionByModel_ModelsSharingTheSameDevice_ReturnSameDefinition(
        OpenNettyBrand firstBrand, string firstModel, OpenNettyBrand secondBrand, string secondModel)
    {
        var first = OpenNettyDevices.GetDeviceDefinitionByModel(firstBrand, firstModel);
        var second = OpenNettyDevices.GetDeviceDefinitionByModel(secondBrand, secondModel);

        Assert.Same(first, second);
        Assert.Equal(2, first.Identities.Count(identity => identity.Model.Equals(firstModel, StringComparison.Ordinal) ||
                                                           identity.Model.Equals(secondModel, StringComparison.Ordinal)));
    }

    [Fact]
    public void GetDeviceDefinitionByModel_NitooSwitch_LoadsAllAttributes()
    {
        var definition = OpenNettyDevices.GetDeviceDefinitionByModel(OpenNettyBrand.Legrand, "03600");

        Assert.Equal(Guid.Parse("b9e508a1-4d2f-4f65-8c27-af27dc7a3a77"), definition.Id);
        Assert.Equal("In One by Legrand", definition.Series);
        Assert.Equal(OpenNettyProtocol.Nitoo, definition.Protocol);
        Assert.Equal(OpenNettyMedium.Powerline, definition.Medium);

        var identity = Assert.Single(definition.Identities);
        Assert.Equal(OpenNettyBrand.Legrand, identity.Brand);
        Assert.Equal("Lexic", identity.Collection);
        Assert.Equal("2-gang DIN rail switch", identity.GetDescription(CultureInfo.GetCultureInfo("en")));
        Assert.Equal("Interrupteur modulaire double", identity.GetDescription(CultureInfo.GetCultureInfo("fr")));
    }

    [Fact]
    public void GetDeviceDefinitionByModel_NitooSwitch_LoadsUnitDefinitions()
    {
        var definition = OpenNettyDevices.GetDeviceDefinitionByModel(OpenNettyBrand.Legrand, "03600");

        Assert.Equal(5, definition.Units.Length);

        var root = definition.GetUnitDefinition(0);
        Assert.Null(root.AssociatedUnitId);
        Assert.True(root.HasCapability(OpenNettyCapabilities.DeviceDescription));
        Assert.True(root.HasCapability(OpenNettyCapabilities.FirmwareVersion));
        Assert.True(root.HasCapability(OpenNettyCapabilities.OutgoingCommunication));
        Assert.Empty(root.Descriptions);

        var buttons = definition.GetUnitDefinition(1);
        Assert.Equal((byte) 3, buttons.AssociatedUnitId);
        Assert.True(buttons.HasCapability(OpenNettyCapabilities.OnOffScenarioActivation));
        Assert.False(buttons.HasCapability(OpenNettyCapabilities.OnOffSwitchControl));
        Assert.Equal("ON/OFF buttons 1", buttons.GetDescription(CultureInfo.GetCultureInfo("en")));
        Assert.Equal("Boutons ON/OFF 1", buttons.GetDescription(CultureInfo.GetCultureInfo("fr")));
        Assert.Equal("button", buttons.GetStringSetting(new OpenNettySetting("Home Assistant scenario device class")));

        var output = definition.GetUnitDefinition(3);
        Assert.True(output.HasCapability(OpenNettyCapabilities.OnOffSwitchControl));
        Assert.Equal("Output 1", output.GetDescription(CultureInfo.GetCultureInfo("en")));
    }

    [Theory]
    [InlineData(OpenNettyBrand.BTicino, "F454", OpenNettyProtocol.Scs, OpenNettyMedium.Bus)]
    [InlineData(OpenNettyBrand.Legrand, "88213", OpenNettyProtocol.Nitoo, OpenNettyMedium.Powerline)]
    [InlineData(OpenNettyBrand.Legrand, "88328", OpenNettyProtocol.Zigbee, OpenNettyMedium.Radio)]
    public void GetDeviceDefinitionByModel_Gateway_ExposesGatewayCapabilityOnItsSingleUnit(
        OpenNettyBrand brand, string model, OpenNettyProtocol protocol, OpenNettyMedium medium)
    {
        var definition = OpenNettyDevices.GetDeviceDefinitionByModel(brand, model);

        Assert.Equal(protocol, definition.Protocol);
        Assert.Equal(medium, definition.Medium);
        Assert.Single(definition.Units);
        Assert.True(definition.Units[0].HasCapability(OpenNettyCapabilities.OpenWebNetGateway));
    }

    [Fact]
    public void GetDeviceDefinitionByModel_ZigbeeDevice_LoadsDeviceLevelSettings()
    {
        var definition = OpenNettyDevices.GetDeviceDefinitionByModel(OpenNettyBrand.Legrand, "573862");

        Assert.NotEmpty(definition.Settings);
        Assert.NotNull(definition.GetStringSetting(new OpenNettySetting("Zigbee device type")));
    }

    [Theory]
    [InlineData(OpenNettyBrand.Legrand, "03600")]
    [InlineData(OpenNettyBrand.Legrand, "88213")]
    [InlineData(OpenNettyBrand.BTicino, "F454")]
    [InlineData(OpenNettyBrand.BTicino, "f454")]
    [InlineData(OpenNettyBrand.Legrand, "03598")]
    [InlineData(OpenNettyBrand.Legrand, "84531")]
    [InlineData(OpenNettyBrand.Legrand, "573862")]
    public void TryGetDeviceDefinitionByModel_KnownModel_ReturnsTrueAndDefinition(OpenNettyBrand brand, string model)
    {
        Assert.True(OpenNettyDevices.TryGetDeviceDefinitionByModel(brand, model, out var definition));
        Assert.NotNull(definition);
        Assert.True(definition.HasIdentity(brand, model));
    }

    [Theory]
    [InlineData(OpenNettyBrand.Legrand, "unknown")]
    [InlineData(OpenNettyBrand.Legrand, "0360")]
    [InlineData(OpenNettyBrand.BTicino, "03600")]
    [InlineData(OpenNettyBrand.Legrand, "F454")]
    public void TryGetDeviceDefinitionByModel_UnknownModel_ReturnsFalse(OpenNettyBrand brand, string model)
    {
        Assert.False(OpenNettyDevices.TryGetDeviceDefinitionByModel(brand, model, out var definition));
        Assert.Null(definition);
    }

    [Fact]
    public void TryGetDeviceDefinitionByModel_NullModel_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => OpenNettyDevices.TryGetDeviceDefinitionByModel(OpenNettyBrand.Legrand, null!, out _));

        Assert.Equal("model", exception.ParamName);
    }

    [Fact]
    public void TryGetDeviceDefinitionByModel_EmptyModel_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => OpenNettyDevices.TryGetDeviceDefinitionByModel(OpenNettyBrand.Legrand, string.Empty, out _));

        Assert.Equal("model", exception.ParamName);
    }

    [Fact]
    public void TryGetDeviceDefinitionByModel_UndefinedBrand_ThrowsInvalidEnumArgumentException()
    {
        var exception = Assert.Throws<InvalidEnumArgumentException>(
            () => OpenNettyDevices.TryGetDeviceDefinitionByModel((OpenNettyBrand) 42, "03600", out _));

        Assert.Equal("brand", exception.ParamName);
    }

    public static TheoryData<OpenNettyBrand, string> AllIdentities()
    {
        var data = new TheoryData<OpenNettyBrand, string>();

        foreach (var (brand, model) in LoadCatalog().SelectMany(device => device.Identities))
        {
            data.Add(brand, model);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllIdentities))]
    public void Catalog_EveryIdentity_ResolvesToADefinitionDeclaringIt(OpenNettyBrand brand, string model)
    {
        var definition = OpenNettyDevices.GetDeviceDefinitionByModel(brand, model);

        Assert.True(definition.HasIdentity(brand, model));
    }

    [Fact]
    public void Catalog_Identities_AreUnique()
    {
        var duplicates = LoadCatalog()
            .SelectMany(device => device.Identities)
            .GroupBy(identity => (identity.Brand, Model: identity.Model.ToUpperInvariant()))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Catalog_DeviceIds_AreUniqueAndNotEmpty()
    {
        var ids = LoadCatalog().Select(device => device.Id).ToList();

        Assert.DoesNotContain(Guid.Empty, ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    public static TheoryData<OpenNettyBrand, string> AllDefinitions()
    {
        var data = new TheoryData<OpenNettyBrand, string>();

        foreach (var device in LoadCatalog())
        {
            data.Add(device.Identities[0].Brand, device.Identities[0].Model);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllDefinitions))]
    public void Catalog_Definition_IsReadOnlyAndHasSeriesAndIdentities(OpenNettyBrand brand, string model)
    {
        var definition = OpenNettyDevices.GetDeviceDefinitionByModel(brand, model);

        Assert.True(definition.IsReadOnly);
        Assert.False(string.IsNullOrWhiteSpace(definition.Series));
        Assert.NotEmpty(definition.Identities);
        Assert.True(Enum.IsDefined(definition.Protocol));
        Assert.True(Enum.IsDefined(definition.Medium));
    }

    [Theory]
    [MemberData(nameof(AllDefinitions))]
    public void Catalog_Definition_HasContiguousReadOnlyUnitsLinkedToTheDefinition(OpenNettyBrand brand, string model)
    {
        var definition = OpenNettyDevices.GetDeviceDefinitionByModel(brand, model);

        Assert.NotEmpty(definition.Units);

        for (var index = 0; index < definition.Units.Length; index++)
        {
            Assert.Equal(index, definition.Units[index].Id);
            Assert.Same(definition, definition.Units[index].Device);
            Assert.True(definition.Units[index].IsReadOnly);
        }
    }

    [Theory]
    [MemberData(nameof(AllDefinitions))]
    public void Catalog_Definition_AssociatedUnitsReferenceAnotherExistingUnit(OpenNettyBrand brand, string model)
    {
        var definition = OpenNettyDevices.GetDeviceDefinitionByModel(brand, model);

        foreach (var unit in definition.Units.Where(unit => unit.AssociatedUnitId is not null))
        {
            Assert.True(definition.TryGetUnitDefinition(unit.AssociatedUnitId!.Value, out _));
            Assert.NotEqual(unit.Id, unit.AssociatedUnitId);
        }
    }

    [Theory]
    [MemberData(nameof(AllDefinitions))]
    public void Catalog_Definition_UsesAMediumSupportedByItsProtocol(OpenNettyBrand brand, string model)
    {
        var definition = OpenNettyDevices.GetDeviceDefinitionByModel(brand, model);

        var supportedMedia = definition.Protocol switch
        {
            OpenNettyProtocol.Scs => new[] { OpenNettyMedium.Bus },
            OpenNettyProtocol.Nitoo => [OpenNettyMedium.Infrared, OpenNettyMedium.Powerline, OpenNettyMedium.Radio],
            OpenNettyProtocol.Zigbee => [OpenNettyMedium.Radio],

            _ => []
        };

        Assert.Contains(definition.Medium, supportedMedia);
    }

    [Theory]
    [MemberData(nameof(AllDefinitions))]
    public void Catalog_Definition_IdentitiesHaveAnEnglishDescription(OpenNettyBrand brand, string model)
    {
        var definition = OpenNettyDevices.GetDeviceDefinitionByModel(brand, model);

        foreach (var identity in definition.Identities)
        {
            Assert.True(Enum.IsDefined(identity.Brand));
            Assert.False(string.IsNullOrEmpty(identity.Model));
            Assert.False(string.IsNullOrWhiteSpace(identity.GetDescription(CultureInfo.GetCultureInfo("en"))));
        }
    }

    [Theory]
    [MemberData(nameof(AllDefinitions))]
    public void Catalog_Definition_UsesOnlyKnownCapabilities(OpenNettyBrand brand, string model)
    {
        var definition = OpenNettyDevices.GetDeviceDefinitionByModel(brand, model);
        var knownCapabilities = GetDeclaredValues<OpenNettyCapability>(typeof(OpenNettyCapabilities));

        var unknownCapabilities = definition.Units.SelectMany(unit => unit.Capabilities).Except(knownCapabilities);

        Assert.Empty(unknownCapabilities);
    }

    [Theory]
    [MemberData(nameof(AllDefinitions))]
    public void Catalog_Definition_UsesOnlyKnownSettings(OpenNettyBrand brand, string model)
    {
        var definition = OpenNettyDevices.GetDeviceDefinitionByModel(brand, model);
        var knownSettings = GetDeclaredValues<OpenNettySetting>(typeof(OpenNettySettings));

        var unknownSettings = definition.Settings.Keys
            .Concat(definition.Units.SelectMany(unit => unit.Settings.Keys))
            .Except(knownSettings);

        Assert.Empty(unknownSettings);
    }

    [Theory]
    [MemberData(nameof(AllDefinitions))]
    public void Catalog_Definition_ExposesTheGatewayCapabilityOnTheRootUnitOnly(OpenNettyBrand brand, string model)
    {
        var definition = OpenNettyDevices.GetDeviceDefinitionByModel(brand, model);

        Assert.All(definition.Units.Skip(1), unit => Assert.False(unit.HasCapability(OpenNettyCapabilities.OpenWebNetGateway)));
    }

    private static IEnumerable<T> GetDeclaredValues<T>(Type type) => type
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(T))
        .Select(field => (T) field.GetValue(null)!);

    private static List<(Guid Id, List<(OpenNettyBrand Brand, string Model)> Identities)> LoadCatalog()
    {
        using var stream = typeof(OpenNettyDevices).Assembly.GetManifestResourceStream("OpenNetty.OpenNettyDevices.xml")!;
        var document = XDocument.Load(stream);

        return [.. document.Root!.Elements("Device").Select(device => (
            Id: (Guid) device.Attribute("Id")!,
            Identities: device.Elements("Identity")
                .Select(identity => (Enum.Parse<OpenNettyBrand>((string) identity.Attribute("Brand")!), (string) identity.Attribute("Model")!))
                .ToList()))];
    }
}
