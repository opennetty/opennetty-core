/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyCategoryTests
{
    [Fact]
    public void Constructor_NullValue_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("value", () => new OpenNettyCategory(null!));
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
        var exception = Assert.Throws<ArgumentException>("value", () => new OpenNettyCategory(value));

        Assert.StartsWith(SR.GetResourceString(SR.ID0004), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_ParameterContainingNonAsciiDigits_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new OpenNettyCategory("1", ["a"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("13")]
    [InlineData("1000")]
    public void Constructor_ValidValue_SetsValueAndNoParameters(string value)
    {
        var category = new OpenNettyCategory(value);

        Assert.Equal(value, category.Value);
        Assert.Empty(category.Parameters);
    }

    [Fact]
    public void Constructor_ValueAndParameters_SetsValueAndParameters()
    {
        var category = new OpenNettyCategory("4", ["1", "02"]);

        Assert.Equal("4", category.Value);
        Assert.Equal(["1", "02"], category.Parameters);
    }

    [Fact]
    public void Default_HasNoValueAndNoParameters()
    {
        var category = default(OpenNettyCategory);

        Assert.Null(category.Value);
        Assert.True(category.Parameters.IsDefaultOrEmpty);
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
    [InlineData("1#2", "3#2", false)]
    [InlineData(null, null, true)]
    [InlineData(null, "1", false)]
    [InlineData("1", null, false)]
    public void Equals_ReturnsExpectedResult(string? left, string? right, bool expected)
    {
        var first = Create(left);
        var second = Create(right);

        Assert.Equal(expected, first.Equals(second));
        Assert.Equal(expected, first == second);
        Assert.Equal(!expected, first != second);
    }

    [Fact]
    public void Equals_EmptyAndDefaultParameters_ReturnsTrue()
    {
        var first = new OpenNettyCategory("1", ImmutableArray<string>.Empty);
        var second = new OpenNettyCategory("1", default);

        Assert.True(first.Equals(second));
        Assert.True(second.Equals(first));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_BoxedEqualCategory_ReturnsTrue()
    {
        var category = new OpenNettyCategory("1");

        Assert.True(category.Equals((object) new OpenNettyCategory("1")));
    }

    [Fact]
    public void Equals_BoxedDifferentCategory_ReturnsFalse()
    {
        var category = new OpenNettyCategory("1");

        Assert.False(category.Equals((object) new OpenNettyCategory("2")));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var category = new OpenNettyCategory("1");

        Assert.False(category.Equals(null));
    }

    [Fact]
    public void Equals_ObjectOfAnotherType_ReturnsFalse()
    {
        var category = new OpenNettyCategory("1");

        Assert.False(category.Equals("1"));
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("", "")]
    [InlineData("1#2", "1#2")]
    [InlineData("13#04#5", "13#04#5")]
    [InlineData(null, null)]
    public void GetHashCode_EqualCategories_ReturnsSameHashCode(string? left, string? right)
    {
        Assert.Equal(Create(left).GetHashCode(), Create(right).GetHashCode());
    }

    [Fact]
    public void GetHashCode_DefaultCategory_ReturnsZero()
    {
        Assert.Equal(0, default(OpenNettyCategory).GetHashCode());
    }

    [Theory]
    [InlineData("1", "2")]
    [InlineData("1", "1#2")]
    [InlineData("1#2", "1#3")]
    public void GetHashCode_DifferentCategories_ReturnsDifferentHashCodes(string left, string right)
    {
        Assert.NotEqual(Create(left).GetHashCode(), Create(right).GetHashCode());
    }

    [Fact]
    public void ToString_DefaultCategory_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, default(OpenNettyCategory).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1000")]
    [InlineData("1#2")]
    [InlineData("13#04#5")]
    [InlineData("#2")]
    public void ToString_ReturnsValueFollowedByHashSeparatedParameters(string text)
    {
        var category = Create(text);

        Assert.Equal(text, category.ToString());
    }

    [Fact]
    public void ToParameters_DefaultCategory_ReturnsEmptyArray()
    {
        Assert.Empty(default(OpenNettyCategory).ToParameters());
    }

    [Theory]
    [InlineData("1", new[] { "1" })]
    [InlineData("1#2", new[] { "1", "2" })]
    [InlineData("13#04#5", new[] { "13", "04", "5" })]
    public void ToParameters_ReturnsValueFollowedByParameters(string text, string[] expected)
    {
        var parameters = Create(text).ToParameters();

        Assert.Equal(expected, parameters.Select(parameter => parameter.Value), StringComparer.Ordinal);
    }

    [Fact]
    public void WithParameters_ReturnsCopyWithSpecifiedParameters()
    {
        var category = new OpenNettyCategory("4", ["1"]);

        var result = category.WithParameters(["2", "3"]);

        Assert.Equal("4", result.Value);
        Assert.Equal(["2", "3"], result.Parameters);
        Assert.Equal(["1"], category.Parameters);
    }

    [Fact]
    public void WithParameters_DefaultCategory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("value", () => default(OpenNettyCategory).WithParameters(["1"]));
    }

    [Fact]
    public void WithoutParameters_ReturnsCopyWithoutParameters()
    {
        var category = new OpenNettyCategory("4", ["1", "2"]);

        var result = category.WithoutParameters();

        Assert.Equal("4", result.Value);
        Assert.Empty(result.Parameters);
        Assert.Equal(new OpenNettyCategory("4"), result);
    }

    private static OpenNettyCategory Create(string? text)
    {
        if (text is null)
        {
            return default;
        }

        var parts = text.Split('#');

        return new OpenNettyCategory(parts[0], [.. parts.Skip(1)]);
    }
}
