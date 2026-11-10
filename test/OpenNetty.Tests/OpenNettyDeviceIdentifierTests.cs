/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyDeviceIdentifierTests
{
    [Fact]
    public void Constructor_NullValue_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new OpenNettyDeviceIdentifier(OpenNettyDeviceIdentifierType.MacAddress, null!));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Constructor_UndefinedType_ThrowsInvalidEnumArgumentException()
    {
        var exception = Assert.Throws<InvalidEnumArgumentException>(
            () => new OpenNettyDeviceIdentifier((OpenNettyDeviceIdentifierType) 42, "value"));

        Assert.Equal("type", exception.ParamName);
    }

    [Theory]
    [InlineData(OpenNettyDeviceIdentifierType.Unknown)]
    [InlineData(OpenNettyDeviceIdentifierType.MacAddress)]
    [InlineData(OpenNettyDeviceIdentifierType.NitooSerialNumber)]
    [InlineData(OpenNettyDeviceIdentifierType.ScsSerialNumber)]
    [InlineData(OpenNettyDeviceIdentifierType.ZigbeeSerialNumber)]
    public void Constructor_ValidArguments_SetsProperties(OpenNettyDeviceIdentifierType type)
    {
        var identifier = new OpenNettyDeviceIdentifier(type, "value");

        Assert.Equal(type, identifier.Type);
        Assert.Equal("value", identifier.Value);
    }

    [Fact]
    public void Constructor_EmptyValue_IsAllowed()
    {
        var identifier = new OpenNettyDeviceIdentifier(OpenNettyDeviceIdentifierType.Unknown, string.Empty);

        Assert.Empty(identifier.Value);
    }

    [Theory]
    [InlineData(OpenNettyDeviceIdentifierType.ScsSerialNumber, "0000001A", OpenNettyDeviceIdentifierType.ScsSerialNumber, "0000001A", true)]
    [InlineData(OpenNettyDeviceIdentifierType.ScsSerialNumber, "0000001A", OpenNettyDeviceIdentifierType.ScsSerialNumber, "0000001a", true)]
    [InlineData(OpenNettyDeviceIdentifierType.ScsSerialNumber, "0000001A", OpenNettyDeviceIdentifierType.ScsSerialNumber, "0000001B", false)]
    [InlineData(OpenNettyDeviceIdentifierType.ScsSerialNumber, "0000001A", OpenNettyDeviceIdentifierType.ZigbeeSerialNumber, "0000001A", false)]
    public void Equals_Identifier_ComparesTypeAndValueIgnoringCase(
        OpenNettyDeviceIdentifierType leftType, string leftValue,
        OpenNettyDeviceIdentifierType rightType, string rightValue, bool expected)
    {
        var left = new OpenNettyDeviceIdentifier(leftType, leftValue);
        var right = new OpenNettyDeviceIdentifier(rightType, rightValue);

        Assert.Equal(expected, left.Equals(right));
        Assert.Equal(expected, left.Equals((object) right));
        Assert.Equal(expected, left == right);
        Assert.Equal(!expected, left != right);
    }

    [Fact]
    public void Equals_UnrelatedObject_ReturnsFalse()
    {
        var identifier = new OpenNettyDeviceIdentifier(OpenNettyDeviceIdentifierType.ScsSerialNumber, "0000001A");

        Assert.False(identifier.Equals("0000001A"));
        Assert.False(identifier.Equals(null));
    }

    [Fact]
    public void GetHashCode_IdentifiersDifferingByCase_ReturnsSameHashCode()
    {
        var left = new OpenNettyDeviceIdentifier(OpenNettyDeviceIdentifierType.ScsSerialNumber, "0000001A");
        var right = new OpenNettyDeviceIdentifier(OpenNettyDeviceIdentifierType.ScsSerialNumber, "0000001a");

        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void GetHashCode_IdentifiersOfDifferentTypes_ReturnsDifferentHashCodes()
    {
        var left = new OpenNettyDeviceIdentifier(OpenNettyDeviceIdentifierType.ScsSerialNumber, "0000001A");
        var right = new OpenNettyDeviceIdentifier(OpenNettyDeviceIdentifierType.ZigbeeSerialNumber, "0000001A");

        Assert.NotEqual(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var identifier = new OpenNettyDeviceIdentifier(OpenNettyDeviceIdentifierType.ScsSerialNumber, "0000001A");

        Assert.Equal("0000001A", identifier.ToString());
    }

    [Theory]
    [InlineData("aa:bb:cc:dd:ee:ff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("AA:BB:CC:DD:EE:FF", "AA:BB:CC:DD:EE:FF")]
    [InlineData("aa-bb-cc-dd-ee-ff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("aabbccddeeff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("  aabbccddeeff  ", "AA:BB:CC:DD:EE:FF")]
    [InlineData("00:04:74:00:00:11:22:33", "00:04:74:00:00:11:22:33")]
    [InlineData("00-04-74-00-00-11-22-33", "00:04:74:00:00:11:22:33")]
    [InlineData("0004740000112233", "00:04:74:00:00:11:22:33")]
    public void FromMacAddress_ValidAddress_ReturnsNormalizedMacAddress(string address, string expected)
    {
        var identifier = OpenNettyDeviceIdentifier.FromMacAddress(address);

        Assert.Equal(OpenNettyDeviceIdentifierType.MacAddress, identifier.Type);
        Assert.Equal(expected, identifier.Value);
    }

    [Fact]
    public void FromMacAddress_NullAddress_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => OpenNettyDeviceIdentifier.FromMacAddress(null!));

        Assert.Equal("address", exception.ParamName);
    }

    [Fact]
    public void FromMacAddress_EmptyAddress_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => OpenNettyDeviceIdentifier.FromMacAddress(string.Empty));

        Assert.Equal("address", exception.ParamName);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("aa:bb-cc:dd:ee:ff")]
    [InlineData("aa:bb:cc:dd:ee")]
    [InlineData("aa:bb:cc:dd:ee:ff:00")]
    [InlineData("aa:bb:cc:dd:ee:f")]
    [InlineData("aa:bb:cc:dd:ee:fff")]
    [InlineData("aa:bb:cc:dd:ee:gg")]
    [InlineData("aa-bb-cc-dd-ee")]
    [InlineData("aabbccddeef")]
    [InlineData("aabbccddeeffa")]
    [InlineData("aabbccddeegg")]
    public void FromMacAddress_InvalidAddress_ThrowsArgumentException(string address)
    {
        var exception = Assert.Throws<ArgumentException>(() => OpenNettyDeviceIdentifier.FromMacAddress(address));

        Assert.Equal("address", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0109), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScsSerialNumber_RoundTripsThroughMacAddress()
    {
        var identifier = OpenNettyDeviceIdentifier.FromScsSerialNumber("12AB34CD");

        var macAddress = OpenNettyDeviceIdentifier.FromMacAddress(OpenNettyDeviceIdentifier.ToMacAddress(identifier));

        Assert.Equal(identifier, OpenNettyDeviceIdentifier.FromScsSerialNumber(OpenNettyDeviceIdentifier.ToScsSerialNumber(macAddress)));
    }

    [Fact]
    public void ZigbeeSerialNumber_RoundTripsThroughMacAddress()
    {
        var identifier = OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(0x12AB34CD);

        var macAddress = OpenNettyDeviceIdentifier.FromMacAddress(OpenNettyDeviceIdentifier.ToMacAddress(identifier));

        Assert.Equal(identifier, OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(OpenNettyDeviceIdentifier.ToZigbeeSerialNumber(macAddress)));
    }

    [Theory]
    [InlineData(0u, "000000")]
    [InlineData(1234u, "001234")]
    [InlineData(999999u, "999999")]
    [InlineData(1048576u, "1048576")]
    public void FromNitooSerialNumber_ValidIdentifier_ReturnsZeroPaddedSerialNumber(uint serialNumber, string expected)
    {
        var identifier = OpenNettyDeviceIdentifier.FromNitooSerialNumber(serialNumber);

        Assert.Equal(OpenNettyDeviceIdentifierType.NitooSerialNumber, identifier.Type);
        Assert.Equal(expected, identifier.Value);
    }

    [Fact]
    public void FromNitooSerialNumber_IdentifierTooLarge_ThrowsArgumentOutOfRangeException()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => OpenNettyDeviceIdentifier.FromNitooSerialNumber(1048577));

        Assert.Equal("identifier", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0040), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1a", "0000001A")]
    [InlineData("0000001A", "0000001A")]
    [InlineData("ffffffff", "FFFFFFFF")]
    [InlineData("0", "00000000")]
    public void FromScsSerialNumber_ValidIdentifier_ReturnsNormalizedSerialNumber(string serialNumber, string expected)
    {
        var identifier = OpenNettyDeviceIdentifier.FromScsSerialNumber(serialNumber);

        Assert.Equal(OpenNettyDeviceIdentifierType.ScsSerialNumber, identifier.Type);
        Assert.Equal(expected, identifier.Value);
    }

    [Fact]
    public void FromScsSerialNumber_NullIdentifier_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => OpenNettyDeviceIdentifier.FromScsSerialNumber(null!));

        Assert.Equal("identifier", exception.ParamName);
    }

    [Fact]
    public void FromScsSerialNumber_EmptyIdentifier_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => OpenNettyDeviceIdentifier.FromScsSerialNumber(string.Empty));

        Assert.Equal("identifier", exception.ParamName);
    }

    [Theory]
    [InlineData("xyz")]
    [InlineData("0x1A")]
    [InlineData("1FFFFFFFF")]
    public void FromScsSerialNumber_InvalidHexadecimalIdentifier_ThrowsArgumentException(string serialNumber)
    {
        var exception = Assert.Throws<ArgumentException>(() => OpenNettyDeviceIdentifier.FromScsSerialNumber(serialNumber));

        Assert.Equal("identifier", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0047), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0u, "00000000")]
    [InlineData(255u, "000000FF")]
    [InlineData(uint.MaxValue, "FFFFFFFF")]
    public void FromZigbeeSerialNumber_NumericIdentifier_ReturnsHexadecimalSerialNumber(uint serialNumber, string expected)
    {
        var identifier = OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(serialNumber);

        Assert.Equal(OpenNettyDeviceIdentifierType.ZigbeeSerialNumber, identifier.Type);
        Assert.Equal(expected, identifier.Value);
    }

    [Theory]
    [InlineData("ff", "000000FF")]
    [InlineData("0000001a", "0000001A")]
    public void FromZigbeeSerialNumber_ValidStringIdentifier_ReturnsNormalizedSerialNumber(string serialNumber, string expected)
    {
        var identifier = OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(serialNumber);

        Assert.Equal(OpenNettyDeviceIdentifierType.ZigbeeSerialNumber, identifier.Type);
        Assert.Equal(expected, identifier.Value);
    }

    [Fact]
    public void FromZigbeeSerialNumber_NullStringIdentifier_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(null!));

        Assert.Equal("identifier", exception.ParamName);
    }

    [Fact]
    public void FromZigbeeSerialNumber_EmptyStringIdentifier_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(string.Empty));

        Assert.Equal("identifier", exception.ParamName);
    }

    [Theory]
    [InlineData("xyz")]
    [InlineData("1FFFFFFFF")]
    public void FromZigbeeSerialNumber_InvalidHexadecimalStringIdentifier_ThrowsArgumentException(string serialNumber)
    {
        var exception = Assert.Throws<ArgumentException>(() => OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(serialNumber));

        Assert.Equal("identifier", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0047), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OpenNettyDeviceIdentifierType.ScsSerialNumber)]
    [InlineData(OpenNettyDeviceIdentifierType.ZigbeeSerialNumber)]
    public void ToMacAddress_SerialNumber_ReturnsMacAddressWithLegrandPrefix(OpenNettyDeviceIdentifierType type)
    {
        var identifier = new OpenNettyDeviceIdentifier(type, "0000001A");

        Assert.Equal("00:04:74:00:00:00:00:1A", OpenNettyDeviceIdentifier.ToMacAddress(identifier));
    }

    [Fact]
    public void ToMacAddress_MacAddress_ReturnsSameValue()
    {
        var identifier = OpenNettyDeviceIdentifier.FromMacAddress("aabbccddeeff");

        Assert.Equal("AA:BB:CC:DD:EE:FF", OpenNettyDeviceIdentifier.ToMacAddress(identifier));
    }

    [Theory]
    [InlineData(OpenNettyDeviceIdentifierType.Unknown)]
    [InlineData(OpenNettyDeviceIdentifierType.NitooSerialNumber)]
    public void ToMacAddress_UnsupportedType_ThrowsArgumentException(OpenNettyDeviceIdentifierType type)
    {
        var identifier = new OpenNettyDeviceIdentifier(type, "000001");

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyDeviceIdentifier.ToMacAddress(identifier));

        Assert.Equal("identifier", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0110), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToNitooSerialNumber_NitooSerialNumber_ReturnsNumericValue()
    {
        var identifier = OpenNettyDeviceIdentifier.FromNitooSerialNumber(1234);

        Assert.Equal(1234u, OpenNettyDeviceIdentifier.ToNitooSerialNumber(identifier));
    }

    [Theory]
    [InlineData(OpenNettyDeviceIdentifierType.Unknown)]
    [InlineData(OpenNettyDeviceIdentifierType.MacAddress)]
    [InlineData(OpenNettyDeviceIdentifierType.ScsSerialNumber)]
    [InlineData(OpenNettyDeviceIdentifierType.ZigbeeSerialNumber)]
    public void ToNitooSerialNumber_UnsupportedType_ThrowsArgumentException(OpenNettyDeviceIdentifierType type)
    {
        var identifier = new OpenNettyDeviceIdentifier(type, "000001");

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyDeviceIdentifier.ToNitooSerialNumber(identifier));

        Assert.Equal("identifier", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0111), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToScsSerialNumber_ScsSerialNumber_ReturnsSameValue()
    {
        var identifier = OpenNettyDeviceIdentifier.FromScsSerialNumber("1a");

        Assert.Equal("0000001A", OpenNettyDeviceIdentifier.ToScsSerialNumber(identifier));
    }

    [Theory]
    [InlineData("00:04:74:00:00:00:00:1A", "0000001A")]
    [InlineData("00:04:74:00:12:AB:34:CD", "12AB34CD")]
    [InlineData("00:04:74:00:12:ab:34:cd", "12ab34cd")]
    public void ToScsSerialNumber_MacAddressWithLegrandPrefix_ReturnsSerialNumber(string address, string expected)
    {
        var identifier = new OpenNettyDeviceIdentifier(OpenNettyDeviceIdentifierType.MacAddress, address);

        Assert.Equal(expected, OpenNettyDeviceIdentifier.ToScsSerialNumber(identifier));
    }

    [Fact]
    public void ToScsSerialNumber_MacAddressWithOtherPrefix_ThrowsArgumentException()
    {
        var identifier = OpenNettyDeviceIdentifier.FromMacAddress("aabbccddeeff");

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyDeviceIdentifier.ToScsSerialNumber(identifier));

        Assert.Equal("identifier", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0112), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OpenNettyDeviceIdentifierType.Unknown)]
    [InlineData(OpenNettyDeviceIdentifierType.NitooSerialNumber)]
    [InlineData(OpenNettyDeviceIdentifierType.ZigbeeSerialNumber)]
    public void ToScsSerialNumber_UnsupportedType_ThrowsArgumentException(OpenNettyDeviceIdentifierType type)
    {
        var identifier = new OpenNettyDeviceIdentifier(type, "000001");

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyDeviceIdentifier.ToScsSerialNumber(identifier));

        Assert.Equal("identifier", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0112), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToZigbeeSerialNumber_ZigbeeSerialNumber_ReturnsSameValue()
    {
        var identifier = OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(26);

        Assert.Equal("0000001A", OpenNettyDeviceIdentifier.ToZigbeeSerialNumber(identifier));
    }

    [Fact]
    public void ToZigbeeSerialNumber_MacAddressWithLegrandPrefix_ReturnsSerialNumber()
    {
        var identifier = new OpenNettyDeviceIdentifier(OpenNettyDeviceIdentifierType.MacAddress, "00:04:74:00:00:00:00:1A");

        Assert.Equal("0000001A", OpenNettyDeviceIdentifier.ToZigbeeSerialNumber(identifier));
    }

    [Fact]
    public void ToZigbeeSerialNumber_MacAddressWithOtherPrefix_ThrowsArgumentException()
    {
        var identifier = OpenNettyDeviceIdentifier.FromMacAddress("aabbccddeeff");

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyDeviceIdentifier.ToZigbeeSerialNumber(identifier));

        Assert.Equal("identifier", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0113), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OpenNettyDeviceIdentifierType.Unknown)]
    [InlineData(OpenNettyDeviceIdentifierType.NitooSerialNumber)]
    [InlineData(OpenNettyDeviceIdentifierType.ScsSerialNumber)]
    public void ToZigbeeSerialNumber_UnsupportedType_ThrowsArgumentException(OpenNettyDeviceIdentifierType type)
    {
        var identifier = new OpenNettyDeviceIdentifier(type, "000001");

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyDeviceIdentifier.ToZigbeeSerialNumber(identifier));

        Assert.Equal("identifier", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0113), exception.Message, StringComparison.Ordinal);
    }
}
