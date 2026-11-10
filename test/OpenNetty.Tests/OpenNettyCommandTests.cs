/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyCommandTests
{
    [Fact]
    public void Constructor_NullValue_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("value", () => new OpenNettyCommand(OpenNettyCategories.Lighting, null!));
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
        var exception = Assert.Throws<ArgumentException>("value", () => new OpenNettyCommand(OpenNettyCategories.Lighting, value));

        Assert.StartsWith(SR.GetResourceString(SR.ID0004), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_ParameterContainingNonAsciiDigits_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new OpenNettyCommand(OpenNettyCategories.Lighting, "1", ["a"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("13")]
    public void Constructor_ValidValue_SetsCategoryAndValueAndNoParameters(string value)
    {
        var command = new OpenNettyCommand(OpenNettyCategories.Automation, value);

        Assert.Equal(OpenNettyCategories.Automation, command.Category);
        Assert.Equal(value, command.Value);
        Assert.Empty(command.Parameters);
    }

    [Fact]
    public void Constructor_ValueAndParameters_SetsCategoryValueAndParameters()
    {
        var command = new OpenNettyCommand(OpenNettyCategories.Lighting, "1", ["2", "03"]);

        Assert.Equal(OpenNettyCategories.Lighting, command.Category);
        Assert.Equal("1", command.Value);
        Assert.Equal(["2", "03"], command.Parameters);
    }

    [Fact]
    public void Default_HasNoValueAndNoParameters()
    {
        var command = default(OpenNettyCommand);

        Assert.Null(command.Value);
        Assert.Equal(default, command.Category);
        Assert.True(command.Parameters.IsDefaultOrEmpty);
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
        var first = new OpenNettyCommand(OpenNettyCategories.Lighting, "1");
        var second = new OpenNettyCommand(OpenNettyCategories.Automation, "1");

        Assert.False(first.Equals(second));
        Assert.True(first != second);
    }

    [Fact]
    public void Equals_EmptyAndDefaultParameters_ReturnsTrue()
    {
        var first = new OpenNettyCommand(OpenNettyCategories.Lighting, "1", ImmutableArray<string>.Empty);
        var second = new OpenNettyCommand(OpenNettyCategories.Lighting, "1", default);

        Assert.True(first.Equals(second));
        Assert.True(second.Equals(first));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var command = new OpenNettyCommand(OpenNettyCategories.Lighting, "1");

        Assert.False(command.Equals(null));
    }

    [Fact]
    public void Equals_ObjectOfAnotherType_ReturnsFalse()
    {
        var command = new OpenNettyCommand(OpenNettyCategories.Lighting, "1");

        Assert.False(command.Equals("1"));
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("", "")]
    [InlineData("1#2", "1#2")]
    [InlineData("13#04#5", "13#04#5")]
    [InlineData(null, null)]
    public void GetHashCode_EqualCommands_ReturnsSameHashCode(string? left, string? right)
    {
        var first = Create(OpenNettyCategories.Lighting, left);
        var second = Create(OpenNettyCategories.Lighting, right);

        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DefaultCommand_ReturnsZero()
    {
        Assert.Equal(0, default(OpenNettyCommand).GetHashCode());
    }

    [Theory]
    [InlineData("1", "2")]
    [InlineData("1", "1#2")]
    [InlineData("1#2", "1#3")]
    public void GetHashCode_DifferentCommands_ReturnsDifferentHashCodes(string left, string right)
    {
        var first = Create(OpenNettyCategories.Lighting, left);
        var second = Create(OpenNettyCategories.Lighting, right);

        Assert.NotEqual(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void GetHashCode_SameValueInDifferentCategories_ReturnsDifferentHashCodes()
    {
        var first = new OpenNettyCommand(OpenNettyCategories.Lighting, "1");
        var second = new OpenNettyCommand(OpenNettyCategories.Automation, "1");

        Assert.NotEqual(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void ToString_DefaultCommand_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, default(OpenNettyCommand).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1#2")]
    [InlineData("13#04#5")]
    [InlineData("#2")]
    public void ToString_ReturnsValueFollowedByHashSeparatedParameters(string text)
    {
        var command = Create(OpenNettyCategories.Lighting, text);

        Assert.Equal(text, command.ToString());
    }

    [Fact]
    public void ToParameters_DefaultCommand_ReturnsEmptyArray()
    {
        Assert.Empty(default(OpenNettyCommand).ToParameters());
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
        var command = new OpenNettyCommand(OpenNettyCategories.Lighting, "1", ["1"]);

        var result = command.WithParameters(["2", "3"]);

        Assert.Equal(OpenNettyCategories.Lighting, result.Category);
        Assert.Equal("1", result.Value);
        Assert.Equal(["2", "3"], result.Parameters);
        Assert.Equal(["1"], command.Parameters);
    }

    [Fact]
    public void WithParameters_DefaultCommand_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("value", () => default(OpenNettyCommand).WithParameters(["1"]));
    }

    [Fact]
    public void WithoutParameters_ReturnsCopyWithoutParameters()
    {
        var command = new OpenNettyCommand(OpenNettyCategories.Lighting, "1", ["1", "2"]);

        var result = command.WithoutParameters();

        Assert.Equal(OpenNettyCategories.Lighting, result.Category);
        Assert.Equal("1", result.Value);
        Assert.Empty(result.Parameters);
        Assert.Equal(new OpenNettyCommand(OpenNettyCategories.Lighting, "1"), result);
    }

    private static OpenNettyCommand Create(OpenNettyCategory category, string? text)
    {
        if (text is null)
        {
            return default;
        }

        var parts = text.Split('#');

        return new OpenNettyCommand(category, parts[0], [.. parts.Skip(1)]);
    }
}
