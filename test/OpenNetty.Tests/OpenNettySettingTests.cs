/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettySettingTests
{
    [Fact]
    public void Constructor_NullName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("name", () => new OpenNettySetting(null!));
    }

    [Fact]
    public void Constructor_EmptyName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>("name", () => new OpenNettySetting(string.Empty));
    }

    [Fact]
    public void Constructor_ValidName_SetsName()
    {
        var setting = new OpenNettySetting("Clock synchronization");

        Assert.Equal("Clock synchronization", setting.Name);
    }

    [Theory]
    [InlineData("Clock synchronization", "Clock synchronization", true)]
    [InlineData("Clock synchronization", "CLOCK SYNCHRONIZATION", true)]
    [InlineData("Clock synchronization", "clock synchronization", true)]
    [InlineData("Clock synchronization", "Function type", false)]
    [InlineData("Clock synchronization", " Clock synchronization", false)]
    public void Equals_ComparesNamesIgnoringCase(string left, string right, bool expected)
    {
        var first = new OpenNettySetting(left);
        var second = new OpenNettySetting(right);

        Assert.Equal(expected, first.Equals(second));
        Assert.Equal(expected, first.Equals((object) second));
        Assert.Equal(expected, first == second);
        Assert.Equal(!expected, first != second);
    }

    [Fact]
    public void Equals_DefaultSettings_ReturnsTrue()
    {
        Assert.True(default(OpenNettySetting).Equals(default));
        Assert.True(default(OpenNettySetting) == default);
    }

    [Fact]
    public void Equals_DefaultAndNamedSettings_ReturnsFalse()
    {
        var setting = new OpenNettySetting("Clock synchronization");

        Assert.False(setting.Equals(default));
        Assert.False(default(OpenNettySetting).Equals(setting));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var setting = new OpenNettySetting("Clock synchronization");

        Assert.False(setting.Equals(null));
    }

    [Fact]
    public void Equals_ObjectOfAnotherType_ReturnsFalse()
    {
        var setting = new OpenNettySetting("Clock synchronization");

        Assert.False(setting.Equals("Clock synchronization"));
    }

    [Fact]
    public void GetHashCode_NamesDifferingByCase_ReturnsSameHashCode()
    {
        var first = new OpenNettySetting("Clock synchronization");
        var second = new OpenNettySetting("CLOCK SYNCHRONIZATION");

        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentNames_ReturnsDifferentHashCodes()
    {
        var first = new OpenNettySetting("Clock synchronization");
        var second = new OpenNettySetting("Function type");

        Assert.NotEqual(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DefaultSetting_ReturnsZero()
    {
        Assert.Equal(0, default(OpenNettySetting).GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsName()
    {
        var setting = new OpenNettySetting("Clock synchronization");

        Assert.Equal("Clock synchronization", setting.ToString());
    }

    [Fact]
    public void ToString_DefaultSetting_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, default(OpenNettySetting).ToString());
    }
}
