/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Globalization;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyDeviceIdentityTests
{
    [Fact]
    public void Initializer_ValidValues_SetsProperties()
    {
        var descriptions = CreateDescriptions(("en", "Switch"));

        var identity = new OpenNettyDeviceIdentity
        {
            Brand = OpenNettyBrand.BTicino,
            Collection = "Axolute",
            Descriptions = descriptions,
            Model = "F411/4"
        };

        Assert.Equal(OpenNettyBrand.BTicino, identity.Brand);
        Assert.Equal("Axolute", identity.Collection);
        Assert.Same(descriptions, identity.Descriptions);
        Assert.Equal("F411/4", identity.Model);
    }

    [Fact]
    public void Model_NullValue_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => CreateIdentity(model: null!));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Model_EmptyValue_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => CreateIdentity(model: string.Empty));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Equals_IdenticalIdentities_ReturnsTrue()
    {
        var left = CreateIdentity();
        var right = CreateIdentity();

        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object) right));
        Assert.True(left == right);
        Assert.False(left != right);
    }

    [Fact]
    public void Equals_CollectionAndModelDifferingByCase_ReturnsTrue()
    {
        var left = CreateIdentity(collection: "Lexic", model: "abc");
        var right = CreateIdentity(collection: "LEXIC", model: "ABC");

        Assert.True(left.Equals(right));
    }

    [Fact]
    public void Equals_DifferentBrand_ReturnsFalse()
    {
        var left = CreateIdentity(brand: OpenNettyBrand.Legrand);
        var right = CreateIdentity(brand: OpenNettyBrand.BTicino);

        Assert.False(left.Equals(right));
        Assert.True(left != right);
    }

    [Theory]
    [InlineData("Lexic", "Axolute")]
    [InlineData("Lexic", null)]
    [InlineData(null, "Lexic")]
    public void Equals_DifferentCollection_ReturnsFalse(string? leftCollection, string? rightCollection)
    {
        var left = CreateIdentity(collection: leftCollection);
        var right = CreateIdentity(collection: rightCollection);

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void Equals_DifferentModel_ReturnsFalse()
    {
        var left = CreateIdentity(model: "03600");
        var right = CreateIdentity(model: "03648");

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void Equals_DifferentDescriptionCount_ReturnsFalse()
    {
        var left = CreateIdentity(descriptions: CreateDescriptions(("en", "Switch")));
        var right = CreateIdentity(descriptions: CreateDescriptions(("en", "Switch"), ("fr", "Interrupteur")));

        Assert.False(left.Equals(right));
    }

    [Theory]
    [InlineData("en", "Switch", "en", "Dimmer")]
    [InlineData("en", "Switch", "fr", "Switch")]
    public void Equals_DifferentDescriptions_ReturnsFalse(
        string leftCulture, string leftValue, string rightCulture, string rightValue)
    {
        var left = CreateIdentity(descriptions: CreateDescriptions((leftCulture, leftValue)));
        var right = CreateIdentity(descriptions: CreateDescriptions((rightCulture, rightValue)));

        Assert.False(left.Equals(right));
    }

    [Theory]
    [InlineData("en", "English")]
    [InlineData("fr", "Français")]
    [InlineData("fr-FR", "Français")]
    [InlineData("fr-CA", "Français")]
    [InlineData("en-US", "English")]
    [InlineData("de", "English")]
    [InlineData("de-DE", "English")]
    [InlineData("", "English")]
    public void GetDescription_AvailableDescriptions_ReturnsBestMatch(string culture, string expected)
    {
        var identity = CreateIdentity(descriptions: CreateDescriptions(("en", "English"), ("fr", "Français")));

        Assert.Equal(expected, identity.GetDescription(CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void GetDescription_SpecificCultureDescription_IsPreferredOverParentCulture()
    {
        var identity = CreateIdentity(descriptions: CreateDescriptions(("fr", "Français"), ("fr-CA", "Canadien")));

        Assert.Equal("Canadien", identity.GetDescription(CultureInfo.GetCultureInfo("fr-CA")));
    }

    [Fact]
    public void GetDescription_InvariantCultureWithNoMatch_ReturnsNull()
    {
        var identity = CreateIdentity(descriptions: CreateDescriptions(("fr", "Français")));

        Assert.Null(identity.GetDescription(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GetDescription_NoEnglishFallback_ReturnsNull()
    {
        var identity = CreateIdentity(descriptions: CreateDescriptions(("fr", "Français")));

        Assert.Null(identity.GetDescription(CultureInfo.GetCultureInfo("de-DE")));
    }

    [Fact]
    public void GetDescription_NoDescriptions_ReturnsNull()
    {
        var identity = CreateIdentity(descriptions: ImmutableDictionary<CultureInfo, string>.Empty);

        Assert.Null(identity.GetDescription(CultureInfo.GetCultureInfo("en")));
    }

    [Fact]
    public void GetDescription_NullCulture_ThrowsArgumentNullException()
    {
        var identity = CreateIdentity();

        var exception = Assert.Throws<ArgumentNullException>(() => identity.GetDescription(null!));

        Assert.Equal("culture", exception.ParamName);
    }

    [Fact]
    public void GetHashCode_EqualIdentities_ReturnsSameHashCode()
    {
        var left = CreateIdentity(collection: "Lexic", model: "abc");
        var right = CreateIdentity(collection: "LEXIC", model: "ABC");

        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentModels_ReturnsDifferentHashCodes()
    {
        var left = CreateIdentity(model: "03600");
        var right = CreateIdentity(model: "03648");

        Assert.NotEqual(left.GetHashCode(), right.GetHashCode());
    }

    [Theory]
    [InlineData(OpenNettyBrand.Legrand, "Lexic", "03600", "Legrand Lexic (03600)")]
    [InlineData(OpenNettyBrand.BTicino, "Axolute", "F411/4", "BTicino Axolute (F411/4)")]
    [InlineData(OpenNettyBrand.BTicino, null, "F454", "BTicino  (F454)")]
    public void ToString_ReturnsBrandCollectionAndModel(OpenNettyBrand brand, string? collection, string model, string expected)
    {
        var identity = CreateIdentity(brand: brand, collection: collection, model: model);

        Assert.Equal(expected, identity.ToString());
    }

    private static OpenNettyDeviceIdentity CreateIdentity(
        OpenNettyBrand brand = OpenNettyBrand.Legrand,
        string? collection = "Lexic",
        ImmutableDictionary<CultureInfo, string>? descriptions = null,
        string model = "03600") => new()
        {
            Brand = brand,
            Collection = collection,
            Descriptions = descriptions ?? CreateDescriptions(("en", "Switch"), ("fr", "Interrupteur")),
            Model = model
        };

    private static ImmutableDictionary<CultureInfo, string> CreateDescriptions(params (string Culture, string Value)[] descriptions)
        => descriptions.ToImmutableDictionary(description => CultureInfo.GetCultureInfo(description.Culture), description => description.Value);
}
