/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyDimensionTests
{
    [Fact]
    public void Constructor_NullValue_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("value", () => new OpenNettyDimension(OpenNettyCategories.Lighting, null!));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("1a")]
    [InlineData("-1")]
    [InlineData(" 1")]
    [InlineData("1#2")]
    [InlineData("\u0661")]
    public void Constructor_ValueContainingNonAsciiDigits_ThrowsArgumentException(string value)
    {
        var exception = Assert.Throws<ArgumentException>("value", () => new OpenNettyDimension(OpenNettyCategories.Lighting, value));

        Assert.StartsWith(SR.GetResourceString(SR.ID0004), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_ParameterContainingNonAsciiDigits_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new OpenNettyDimension(OpenNettyCategories.Lighting, "1", ["a"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("13")]
    public void Constructor_ValidValue_SetsCategoryAndValueAndNoParameters(string value)
    {
        var dimension = new OpenNettyDimension(OpenNettyCategories.Automation, value);

        Assert.Equal(OpenNettyCategories.Automation, dimension.Category);
        Assert.Equal(value, dimension.Value);
        Assert.Empty(dimension.Parameters);
    }

    [Fact]
    public void Constructor_ValueAndParameters_SetsCategoryValueAndParameters()
    {
        var dimension = new OpenNettyDimension(OpenNettyCategories.Lighting, "1", ["2", "03"]);

        Assert.Equal(OpenNettyCategories.Lighting, dimension.Category);
        Assert.Equal("1", dimension.Value);
        Assert.Equal(["2", "03"], dimension.Parameters);
    }

    [Fact]
    public void Default_HasNoValueAndNoParameters()
    {
        var dimension = default(OpenNettyDimension);

        Assert.Null(dimension.Value);
        Assert.Equal(default, dimension.Category);
        Assert.True(dimension.Parameters.IsDefaultOrEmpty);
    }

    [Theory]
    [InlineData("1", "1", true)]
    [InlineData("1", "2", false)]
    [InlineData("", "", true)]
    [InlineData("1#2", "1#2", true)]
    [InlineData("1#2", "1#3", false)]
    [InlineData("1#2", "1#2#3", false)]
    [InlineData("1#2#3", "1#2", false)]
    [InlineData("1", "1#2", false)]
    [InlineData("1#2", "1", false)]
    [InlineData(null, null, true)]
    [InlineData(null, "1", false)]
    [InlineData("1", null, false)]
    public void Equals_ReturnsExpectedResult(string? left, string? right, bool expected)
    {
        var first = Create(OpenNettyCategories.Lighting, left);
        var second = Create(OpenNettyCategories.Lighting, right);

        Assert.Equal(expected, first.Equals(second));
        Assert.Equal(expected, first.Equals((object) second));
        Assert.Equal(expected, first == second);
        Assert.Equal(!expected, first != second);
    }

    [Fact]
    public void Equals_SameValueInDifferentCategories_ReturnsFalse()
    {
        var first = new OpenNettyDimension(OpenNettyCategories.Lighting, "1");
        var second = new OpenNettyDimension(OpenNettyCategories.Automation, "1");

        Assert.False(first.Equals(second));
        Assert.True(first != second);
    }

    [Fact]
    public void Equals_EmptyAndDefaultParameters_ReturnsTrue()
    {
        var first = new OpenNettyDimension(OpenNettyCategories.Lighting, "1", ImmutableArray<string>.Empty);
        var second = new OpenNettyDimension(OpenNettyCategories.Lighting, "1", default);

        Assert.True(first.Equals(second));
        Assert.True(second.Equals(first));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var dimension = new OpenNettyDimension(OpenNettyCategories.Lighting, "1");

        Assert.False(dimension.Equals(null));
    }

    [Fact]
    public void Equals_ObjectOfAnotherType_ReturnsFalse()
    {
        var dimension = new OpenNettyDimension(OpenNettyCategories.Lighting, "1");

        Assert.False(dimension.Equals("1"));
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("", "")]
    [InlineData("1#2", "1#2")]
    [InlineData("13#04#5", "13#04#5")]
    [InlineData(null, null)]
    public void GetHashCode_EqualDimensions_ReturnsSameHashCode(string? left, string? right)
    {
        var first = Create(OpenNettyCategories.Lighting, left);
        var second = Create(OpenNettyCategories.Lighting, right);

        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DefaultDimension_ReturnsZero()
    {
        Assert.Equal(0, default(OpenNettyDimension).GetHashCode());
    }

    [Theory]
    [InlineData("1", "2")]
    [InlineData("1", "1#2")]
    [InlineData("1#2", "1#3")]
    public void GetHashCode_DifferentDimensions_ReturnsDifferentHashCodes(string left, string right)
    {
        var first = Create(OpenNettyCategories.Lighting, left);
        var second = Create(OpenNettyCategories.Lighting, right);

        Assert.NotEqual(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void GetHashCode_SameValueInDifferentCategories_ReturnsDifferentHashCodes()
    {
        var first = new OpenNettyDimension(OpenNettyCategories.Lighting, "1");
        var second = new OpenNettyDimension(OpenNettyCategories.Automation, "1");

        Assert.NotEqual(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void ToString_DefaultDimension_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, default(OpenNettyDimension).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1#2")]
    [InlineData("13#04#5")]
    [InlineData("#2")]
    public void ToString_ReturnsValueFollowedByHashSeparatedParameters(string text)
    {
        var dimension = Create(OpenNettyCategories.Lighting, text);

        Assert.Equal(text, dimension.ToString());
    }

    [Fact]
    public void ToParameters_DefaultDimension_ReturnsEmptyArray()
    {
        Assert.Empty(default(OpenNettyDimension).ToParameters());
    }

    [Theory]
    [InlineData("1", new[] { "1" })]
    [InlineData("1#2", new[] { "1", "2" })]
    [InlineData("13#04#5", new[] { "13", "04", "5" })]
    public void ToParameters_ReturnsValueFollowedByParameters(string text, string[] expected)
    {
        var parameters = Create(OpenNettyCategories.Lighting, text).ToParameters();

        Assert.Equal(expected, parameters.Select(parameter => parameter.Value), StringComparer.Ordinal);
    }

    [Fact]
    public void WithParameters_ReturnsCopyWithSpecifiedParameters()
    {
        var dimension = new OpenNettyDimension(OpenNettyCategories.Lighting, "1", ["1"]);

        var result = dimension.WithParameters(["2", "3"]);

        Assert.Equal(OpenNettyCategories.Lighting, result.Category);
        Assert.Equal("1", result.Value);
        Assert.Equal(["2", "3"], result.Parameters);
        Assert.Equal(["1"], dimension.Parameters);
    }

    [Fact]
    public void WithParameters_DefaultDimension_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("value", () => default(OpenNettyDimension).WithParameters(["1"]));
    }

    [Fact]
    public void WithoutParameters_ReturnsCopyWithoutParameters()
    {
        var dimension = new OpenNettyDimension(OpenNettyCategories.Lighting, "1", ["1", "2"]);

        var result = dimension.WithoutParameters();

        Assert.Equal(OpenNettyCategories.Lighting, result.Category);
        Assert.Equal("1", result.Value);
        Assert.Empty(result.Parameters);
        Assert.Equal(new OpenNettyDimension(OpenNettyCategories.Lighting, "1"), result);
    }

    private static OpenNettyDimension Create(OpenNettyCategory category, string? text)
    {
        if (text is null)
        {
            return default;
        }

        var parts = text.Split('#');

        return new OpenNettyDimension(category, parts[0], [.. parts.Skip(1)]);
    }
}
