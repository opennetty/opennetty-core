/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Text;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyParameterTests
{
    [Fact]
    public void Constructor_NullValue_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("value", () => new OpenNettyParameter(null!));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("1a")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData(" 1")]
    [InlineData("1#2")]
    [InlineData("1*2")]
    [InlineData("\u0661")]
    public void Constructor_ValueContainingNonAsciiDigits_ThrowsArgumentException(string value)
    {
        var exception = Assert.Throws<ArgumentException>("value", () => new OpenNettyParameter(value));

        Assert.StartsWith(SR.GetResourceString(SR.ID0004), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0123456789")]
    public void Constructor_ValidValue_SetsValue(string value)
    {
        var parameter = new OpenNettyParameter(value);

        Assert.Equal(value, parameter.Value);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("0", false)]
    [InlineData("12", false)]
    public void IsEmpty_ReturnsTrueOnlyForEmptyValues(string value, bool expected)
    {
        Assert.Equal(expected, new OpenNettyParameter(value).IsEmpty);
    }

    [Fact]
    public void IsEmpty_DefaultParameter_ReturnsTrue()
    {
        Assert.True(default(OpenNettyParameter).IsEmpty);
    }

    [Fact]
    public void Empty_HasEmptyValue()
    {
        Assert.Equal(string.Empty, OpenNettyParameter.Empty.Value);
        Assert.True(OpenNettyParameter.Empty.IsEmpty);
    }

    [Fact]
    public void Parse_EmptyBuffer_ReturnsEmptyParameter()
    {
        var parameter = OpenNettyParameter.Parse(ReadOnlySpan<byte>.Empty);

        Assert.Equal(OpenNettyParameter.Empty, parameter);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("12")]
    [InlineData("007")]
    public void Parse_AsciiDigits_ReturnsParameterWithMatchingValue(string value)
    {
        var parameter = OpenNettyParameter.Parse(Encoding.ASCII.GetBytes(value));

        Assert.Equal(value, parameter.Value);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("1*")]
    [InlineData("1#")]
    public void Parse_InvalidCharacters_ThrowsArgumentException(string value)
    {
        var buffer = Encoding.ASCII.GetBytes(value);

        Assert.Throws<ArgumentException>("value", () => OpenNettyParameter.Parse(buffer));
    }

    [Fact]
    public void Parse_NonAsciiBytes_ThrowsArgumentException()
    {
        var buffer = new byte[] { 0xFF };

        Assert.Throws<ArgumentException>("value", () => OpenNettyParameter.Parse(buffer));
    }

    [Fact]
    public void ImplicitConversion_FromString_CreatesParameter()
    {
        OpenNettyParameter parameter = "42";

        Assert.Equal("42", parameter.Value);
    }

    [Fact]
    public void ImplicitConversion_FromInvalidString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>("value", () => (OpenNettyParameter) "x");
    }

    [Theory]
    [InlineData("1", "1", true)]
    [InlineData("1", "2", false)]
    [InlineData("", "", true)]
    [InlineData("01", "1", false)]
    public void Equals_ComparesValues(string left, string right, bool expected)
    {
        var first = new OpenNettyParameter(left);
        var second = new OpenNettyParameter(right);

        Assert.Equal(expected, first.Equals(second));
        Assert.Equal(expected, first.Equals((object) second));
        Assert.Equal(expected, first == second);
        Assert.Equal(!expected, first != second);
    }

    [Fact]
    public void Equals_DefaultParameters_ReturnsTrue()
    {
        Assert.True(default(OpenNettyParameter) == default);
    }

    [Fact]
    public void Equals_DefaultAndEmptyParameters_ReturnsFalse()
    {
        Assert.NotEqual(default, OpenNettyParameter.Empty);
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        Assert.False(new OpenNettyParameter("1").Equals((object?) null));
    }

    [Fact]
    public void Equals_ObjectOfAnotherType_ReturnsFalse()
    {
        Assert.False(new OpenNettyParameter("1").Equals((object) "1"));
    }

    [Fact]
    public void GetHashCode_EqualParameters_ReturnsSameHashCode()
    {
        Assert.Equal(new OpenNettyParameter("12").GetHashCode(), new OpenNettyParameter("12").GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentParameters_ReturnsDifferentHashCodes()
    {
        Assert.NotEqual(new OpenNettyParameter("12").GetHashCode(), new OpenNettyParameter("21").GetHashCode());
    }

    [Fact]
    public void GetHashCode_DefaultParameter_ReturnsZero()
    {
        Assert.Equal(0, default(OpenNettyParameter).GetHashCode());
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0042")]
    public void ToString_ReturnsValue(string value)
    {
        Assert.Equal(value, new OpenNettyParameter(value).ToString());
    }

    [Fact]
    public void ToString_DefaultParameter_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, default(OpenNettyParameter).ToString());
    }
}
