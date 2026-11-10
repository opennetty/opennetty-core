/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyCapabilityTests
{
    [Fact]
    public void Constructor_NullName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("name", () => new OpenNettyCapability(null!));
    }

    [Fact]
    public void Constructor_EmptyName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>("name", () => new OpenNettyCapability(string.Empty));
    }

    [Fact]
    public void Constructor_ValidName_SetsName()
    {
        var capability = new OpenNettyCapability("Basic dimming control");

        Assert.Equal("Basic dimming control", capability.Name);
    }

    [Theory]
    [InlineData("Basic dimming control", "Basic dimming control", true)]
    [InlineData("Basic dimming control", "BASIC DIMMING CONTROL", true)]
    [InlineData("Basic dimming control", "basic dimming control", true)]
    [InlineData("Basic dimming control", "Basic dimming state", false)]
    [InlineData("Basic dimming control", " Basic dimming control", false)]
    public void Equals_ComparesNamesIgnoringCase(string left, string right, bool expected)
    {
        var first = new OpenNettyCapability(left);
        var second = new OpenNettyCapability(right);

        Assert.Equal(expected, first.Equals(second));
        Assert.Equal(expected, first.Equals((object) second));
        Assert.Equal(expected, first == second);
        Assert.Equal(!expected, first != second);
    }

    [Fact]
    public void Equals_DefaultCapabilities_ReturnsTrue()
    {
        Assert.True(default(OpenNettyCapability).Equals(default));
        Assert.True(default(OpenNettyCapability) == default);
    }

    [Fact]
    public void Equals_DefaultAndNamedCapabilities_ReturnsFalse()
    {
        var capability = new OpenNettyCapability("Basic dimming control");

        Assert.False(capability.Equals(default));
        Assert.False(default(OpenNettyCapability).Equals(capability));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var capability = new OpenNettyCapability("Basic dimming control");

        Assert.False(capability.Equals(null));
    }

    [Fact]
    public void Equals_ObjectOfAnotherType_ReturnsFalse()
    {
        var capability = new OpenNettyCapability("Basic dimming control");

        Assert.False(capability.Equals("Basic dimming control"));
    }

    [Fact]
    public void GetHashCode_NamesDifferingByCase_ReturnsSameHashCode()
    {
        var first = new OpenNettyCapability("Basic dimming control");
        var second = new OpenNettyCapability("BASIC DIMMING CONTROL");

        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentNames_ReturnsDifferentHashCodes()
    {
        var first = new OpenNettyCapability("Basic dimming control");
        var second = new OpenNettyCapability("Basic dimming state");

        Assert.NotEqual(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DefaultCapability_ReturnsZero()
    {
        Assert.Equal(0, default(OpenNettyCapability).GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsName()
    {
        var capability = new OpenNettyCapability("Basic dimming control");

        Assert.Equal("Basic dimming control", capability.ToString());
    }

    [Fact]
    public void ToString_DefaultCapability_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, default(OpenNettyCapability).ToString());
    }
}
