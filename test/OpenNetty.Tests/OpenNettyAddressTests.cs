/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyAddressTests
{
    [Fact]
    public void Constructor_NullValue_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new OpenNettyAddress(OpenNettyAddressType.Nitoo, null!));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Constructor_UndefinedType_ThrowsInvalidEnumArgumentException()
    {
        var exception = Assert.Throws<InvalidEnumArgumentException>(
            () => new OpenNettyAddress((OpenNettyAddressType) 42, "1"));

        Assert.Equal("type", exception.ParamName);
    }

    [Theory]
    [InlineData("12a")]
    [InlineData("a")]
    [InlineData(" 1")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("١٢")]
    public void Constructor_ValueWithNonAsciiDigits_ThrowsArgumentException(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() => new OpenNettyAddress(OpenNettyAddressType.Nitoo, value));

        Assert.Equal("value", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0004), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_ParametersWithNonAsciiDigits_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, "0", ["4", "a"]));

        Assert.StartsWith(SR.GetResourceString(SR.ID0004), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OpenNettyAddressType.Unknown)]
    [InlineData(OpenNettyAddressType.Nitoo)]
    [InlineData(OpenNettyAddressType.Zigbee)]
    [InlineData(OpenNettyAddressType.ScsLightPoint)]
    [InlineData(OpenNettyAddressType.ScsScenarioPlus)]
    [InlineData(OpenNettyAddressType.ScsDryContact)]
    public void Constructor_ValidTypeAndValue_SetsPropertiesAndNoParameters(OpenNettyAddressType type)
    {
        var address = new OpenNettyAddress(type, "123");

        Assert.Equal(type, address.Type);
        Assert.Equal("123", address.Value);
        Assert.Empty(address.Parameters);
    }

    [Fact]
    public void Constructor_EmptyValue_IsAllowed()
    {
        var address = new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, string.Empty, ["5"]);

        Assert.Empty(address.Value);
    }

    [Fact]
    public void Constructor_Parameters_AreExposed()
    {
        var address = new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, "0", ["4", "05"]);

        Assert.Equal(["4", "05"], address.Parameters, StringComparer.Ordinal);
    }

    [Fact]
    public void Default_HasUnknownTypeAndNullValue()
    {
        var address = default(OpenNettyAddress);

        Assert.Equal(OpenNettyAddressType.Unknown, address.Type);
        Assert.Null(address.Value);
        Assert.True(address.Parameters.IsDefaultOrEmpty);
    }

    public static TheoryData<OpenNettyAddress, OpenNettyAddress> EqualAddresses => new()
    {
        { default, default },
        { Address("1"), Address("1") },
        { Address("0", "4", "05"), Address("0", "4", "05") },
        { Address("0"), new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, "0", default) },
        { new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, "0", default), Address("0") }
    };

    [Theory]
    [MemberData(nameof(EqualAddresses))]
    public void Equals_EqualAddresses_ReturnsTrue(OpenNettyAddress left, OpenNettyAddress right)
    {
        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object) right));
        Assert.True(left == right);
        Assert.False(left != right);
    }

    public static TheoryData<OpenNettyAddress, OpenNettyAddress> DifferentAddresses => new()
    {
        { Address("1"), Address("2") },
        { Address("1"), new OpenNettyAddress(OpenNettyAddressType.Nitoo, "1") },
        { Address("0", "4", "05"), Address("0", "4", "06") },
        { Address("0", "4", "05"), Address("0", "4") },
        { Address("0", "4"), Address("0", "4", "05") },
        { Address("0"), Address("0", "4", "05") },
        { Address("0", "4", "05"), Address("0") },
        { Address("0"), default }
    };

    [Theory]
    [MemberData(nameof(DifferentAddresses))]
    public void Equals_DifferentAddresses_ReturnsFalse(OpenNettyAddress left, OpenNettyAddress right)
    {
        Assert.False(left.Equals(right));
        Assert.False(left.Equals((object) right));
        Assert.False(left == right);
        Assert.True(left != right);
    }

    [Fact]
    public void Equals_UnrelatedObject_ReturnsFalse()
    {
        var address = Address("1");

        Assert.False(address.Equals("1"));
        Assert.False(address.Equals(null));
    }

    [Theory]
    [MemberData(nameof(EqualAddresses))]
    public void GetHashCode_EqualAddresses_ReturnsSameHashCode(OpenNettyAddress left, OpenNettyAddress right)
    {
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Theory]
    [MemberData(nameof(DifferentAddresses))]
    public void GetHashCode_DifferentAddresses_ReturnsDifferentHashCodes(OpenNettyAddress left, OpenNettyAddress right)
    {
        Assert.NotEqual(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DefaultAddress_ReturnsZero()
    {
        Assert.Equal(0, default(OpenNettyAddress).GetHashCode());
    }

    [Theory]
    [InlineData("123", new string[0], "123")]
    [InlineData("0", new[] { "4", "05" }, "0#4#05")]
    [InlineData("", new[] { "5" }, "#5")]
    [InlineData("", new[] { "255", "4", "03" }, "#255#4#03")]
    public void ToString_ReturnsValueFollowedByHashSeparatedParameters(string value, string[] parameters, string expected)
    {
        var address = new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, value, [.. parameters]);

        Assert.Equal(expected, address.ToString());
    }

    [Fact]
    public void ToString_DefaultAddress_ReturnsEmptyString()
    {
        Assert.Empty(default(OpenNettyAddress).ToString());
    }

    [Fact]
    public void ToString_DefaultParameters_ReturnsValue()
    {
        var address = new OpenNettyAddress(OpenNettyAddressType.Nitoo, "123", default);

        Assert.Equal("123", address.ToString());
    }

    [Fact]
    public void ToParameters_DefaultAddress_ReturnsEmptyArray()
    {
        Assert.Empty(default(OpenNettyAddress).ToParameters());
    }

    [Fact]
    public void ToParameters_AddressWithoutParameters_ReturnsValueOnly()
    {
        var address = new OpenNettyAddress(OpenNettyAddressType.Nitoo, "123");

        OpenNettyParameter[] expected = ["123"];

        Assert.Equal(expected, address.ToParameters());
    }

    [Fact]
    public void ToParameters_AddressWithDefaultParameters_ReturnsValueOnly()
    {
        var address = new OpenNettyAddress(OpenNettyAddressType.Nitoo, "123", default);

        OpenNettyParameter[] expected = ["123"];

        Assert.Equal(expected, address.ToParameters());
    }

    [Fact]
    public void ToParameters_AddressWithParameters_ReturnsValueFollowedByParameters()
    {
        var address = Address("0", "4", "05");

        OpenNettyParameter[] expected = ["0", "4", "05"];

        Assert.Equal(expected, address.ToParameters());
    }

    [Fact]
    public void WithParameters_ReturnsCopyWithSpecifiedParameters()
    {
        var address = new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, "0");

        var result = address.WithParameters(["4", "05"]);

        Assert.Equal(OpenNettyAddressType.ScsLightPoint, result.Type);
        Assert.Equal("0", result.Value);
        Assert.Equal(["4", "05"], result.Parameters, StringComparer.Ordinal);
        Assert.Empty(address.Parameters);
    }

    [Fact]
    public void WithParameters_ReplacesExistingParameters()
    {
        var address = Address("0", "4", "05");

        var result = address.WithParameters(["4", "06"]);

        Assert.Equal(Address("0", "4", "06"), result);
    }

    [Fact]
    public void WithoutParameters_ReturnsCopyWithoutParameters()
    {
        var address = Address("0", "4", "05");

        var result = address.WithoutParameters();

        Assert.Equal(OpenNettyAddressType.ScsLightPoint, result.Type);
        Assert.Equal("0", result.Value);
        Assert.Empty(result.Parameters);
        Assert.Equal(Address("0"), result);
        Assert.Equal(2, address.Parameters.Length);
    }

    [Theory]
    [InlineData(0u, (byte) 0, "00")]
    [InlineData(0u, (byte) 5, "05")]
    [InlineData(0u, (byte) 99, "99")]
    [InlineData(1234u, (byte) 0, "123400")]
    [InlineData(1234u, (byte) 5, "123405")]
    [InlineData(1234u, (byte) 99, "123499")]
    [InlineData(uint.MaxValue, (byte) 99, "429496729599")]
    public void FromDecimalZigbeeAddress_ValidIdentifierAndUnit_ReturnsZigbeeAddress(uint identifier, byte unit, string expected)
    {
        var address = OpenNettyAddress.FromDecimalZigbeeAddress(identifier, unit);

        Assert.Equal(OpenNettyAddressType.Zigbee, address.Type);
        Assert.Equal(expected, address.Value);
        Assert.Empty(address.Parameters);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1234u)]
    public void FromDecimalZigbeeAddress_UnitTooLarge_ThrowsArgumentOutOfRangeException(uint identifier)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => OpenNettyAddress.FromDecimalZigbeeAddress(identifier, 100));

        Assert.Equal("unit", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0046), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0u, (byte) 0)]
    [InlineData(0u, (byte) 99)]
    [InlineData(1u, (byte) 1)]
    [InlineData(1234u, (byte) 5)]
    [InlineData(uint.MaxValue, (byte) 99)]
    public void ZigbeeAddress_RoundTrips(uint identifier, byte unit)
    {
        var address = OpenNettyAddress.FromDecimalZigbeeAddress(identifier, unit);

        Assert.Equal((identifier, unit), OpenNettyAddress.ToZigbeeAddress(address));
    }

    [Theory]
    [InlineData(null, (byte) 0, "00")]
    [InlineData("", (byte) 5, "05")]
    [InlineData("ff", (byte) 0, "25500")]
    [InlineData("FF", (byte) 5, "25505")]
    [InlineData("000000FF", (byte) 5, "25505")]
    [InlineData("FFFFFFFF", (byte) 99, "429496729599")]
    public void FromHexadecimalZigbeeAddress_ValidIdentifier_ReturnsZigbeeAddressWithDecimalIdentifier(
        string? identifier, byte unit, string expected)
    {
        var address = OpenNettyAddress.FromHexadecimalZigbeeAddress(identifier, unit);

        Assert.Equal(OpenNettyAddressType.Zigbee, address.Type);
        Assert.Equal(expected, address.Value);
    }

    [Theory]
    [InlineData("xyz")]
    [InlineData("0xFF")]
    [InlineData("1FFFFFFFF")]
    public void FromHexadecimalZigbeeAddress_InvalidIdentifier_ThrowsArgumentException(string identifier)
    {
        var exception = Assert.Throws<ArgumentException>(() => OpenNettyAddress.FromHexadecimalZigbeeAddress(identifier, 0));

        Assert.Equal("identifier", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0047), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromHexadecimalZigbeeAddress_UnitTooLarge_ThrowsArgumentOutOfRangeException()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => OpenNettyAddress.FromHexadecimalZigbeeAddress("FF", 100));

        Assert.Equal("unit", exception.ParamName);
    }

    [Theory]
    [InlineData(0u, (byte) 0, "0")]
    [InlineData(0u, (byte) 1, "1")]
    [InlineData(1u, (byte) 0, "16")]
    [InlineData(1234u, (byte) 5, "19749")]
    [InlineData(1234u, (byte) 15, "19759")]
    [InlineData(1048576u, (byte) 0, "16777216")]
    public void FromNitooAddress_ValidIdentifierAndUnit_ReturnsNitooAddress(uint identifier, byte unit, string expected)
    {
        var address = OpenNettyAddress.FromNitooAddress(identifier, unit);

        Assert.Equal(OpenNettyAddressType.Nitoo, address.Type);
        Assert.Equal(expected, address.Value);
        Assert.Empty(address.Parameters);
    }

    [Fact]
    public void FromNitooAddress_IdentifierTooLarge_ThrowsArgumentOutOfRangeException()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => OpenNettyAddress.FromNitooAddress(1048577u, 0));

        Assert.Equal("identifier", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0040), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((byte) 16)]
    [InlineData((byte) 255)]
    public void FromNitooAddress_UnitTooLarge_ThrowsArgumentOutOfRangeException(byte unit)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => OpenNettyAddress.FromNitooAddress(1234u, unit));

        Assert.Equal("unit", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0041), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromNitooAddress_DeviceIdentifier_ReturnsNitooAddress()
    {
        var identifier = OpenNettyDeviceIdentifier.FromNitooSerialNumber(1234);

        var address = OpenNettyAddress.FromNitooAddress(identifier, 5);

        Assert.Equal(OpenNettyAddress.FromNitooAddress(1234u, 5), address);
    }

    [Fact]
    public void FromNitooAddress_NonNitooDeviceIdentifier_ThrowsArgumentException()
    {
        var identifier = OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(1234);

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyAddress.FromNitooAddress(identifier, 5));

        Assert.StartsWith(SR.GetResourceString(SR.ID0111), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0u, (byte) 0)]
    [InlineData(1u, (byte) 15)]
    [InlineData(1234u, (byte) 5)]
    [InlineData(1048576u, (byte) 0)]
    public void NitooAddress_RoundTrips(uint identifier, byte unit)
    {
        var address = OpenNettyAddress.FromNitooAddress(identifier, unit);

        Assert.Equal((identifier, unit), OpenNettyAddress.ToNitooAddress(address));
    }

    [Theory]
    [InlineData((byte) 0, "30")]
    [InlineData((byte) 1, "31")]
    [InlineData((byte) 42, "342")]
    [InlineData((byte) 201, "3201")]
    public void FromScsDryContactAddress_ValidIdentifier_ReturnsDryContactAddress(byte identifier, string expected)
    {
        var address = OpenNettyAddress.FromScsDryContactAddress(identifier);

        Assert.Equal(OpenNettyAddressType.ScsDryContact, address.Type);
        Assert.Equal(expected, address.Value);
        Assert.Empty(address.Parameters);
    }

    [Theory]
    [InlineData((byte) 202)]
    [InlineData((byte) 255)]
    public void FromScsDryContactAddress_IdentifierTooLarge_ThrowsArgumentOutOfRangeException(byte identifier)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => OpenNettyAddress.FromScsDryContactAddress(identifier));

        Assert.Equal("identifier", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0132), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((byte) 0)]
    [InlineData((byte) 100)]
    [InlineData((byte) 201)]
    public void ScsDryContactAddress_RoundTrips(byte identifier)
    {
        var address = OpenNettyAddress.FromScsDryContactAddress(identifier);

        Assert.Equal(identifier, OpenNettyAddress.ToScsDryContactAddress(address));
    }

    [Theory]
    [InlineData(0, true, null, null, null, "0")]
    [InlineData(5, true, null, null, null, "0#4#05")]
    [InlineData(15, true, null, null, null, "0#4#15")]
    [InlineData(0, false, 5, null, null, "#5")]
    [InlineData(0, false, 255, null, null, "#255")]
    [InlineData(3, false, 255, null, null, "#255#4#03")]
    [InlineData(15, false, 1, null, null, "#1#4#15")]
    [InlineData(0, false, null, 0, null, "00")]
    [InlineData(0, false, null, 5, null, "5")]
    [InlineData(0, false, null, 9, null, "9")]
    [InlineData(0, false, null, 10, null, "100")]
    [InlineData(5, false, null, 7, null, "7#4#05")]
    [InlineData(15, false, null, 10, null, "100#4#15")]
    [InlineData(0, false, null, 5, 3, "53")]
    [InlineData(0, false, null, 5, 12, "0512")]
    [InlineData(0, false, null, 0, 5, "05")]
    [InlineData(0, false, null, 0, 12, "0012")]
    [InlineData(0, false, null, 10, 3, "1003")]
    [InlineData(0, false, null, 10, 12, "1012")]
    [InlineData(7, false, null, 5, 3, "53#4#07")]
    public void FromScsLightPointAddress_ValidArguments_ReturnsLightPointAddress(
        byte extension, bool general, int? group, int? area, int? point, string expected)
    {
        var address = OpenNettyAddress.FromScsLightPointAddress(extension, general, (byte?) group, (byte?) area, (byte?) point);

        Assert.Equal(OpenNettyAddressType.ScsLightPoint, address.Type);
        Assert.Equal(expected, address.ToString());
    }

    [Theory]
    [InlineData(11)]
    [InlineData(255)]
    public void FromScsLightPointAddress_AreaTooLarge_ThrowsArgumentOutOfRangeException(byte area)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => OpenNettyAddress.FromScsLightPointAddress(extension: 0, general: false, group: null, area, point: 1));

        Assert.Equal("area", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0042), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromScsLightPointAddress_GroupEqualToZero_ThrowsArgumentOutOfRangeException()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => OpenNettyAddress.FromScsLightPointAddress(extension: 0, general: false, group: 0, area: null, point: null));

        Assert.Equal("group", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0044), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(255)]
    public void FromScsLightPointAddress_ExtensionTooLarge_ThrowsArgumentOutOfRangeException(byte extension)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => OpenNettyAddress.FromScsLightPointAddress(extension, general: true, group: null, area: null, point: null));

        Assert.Equal("extension", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0043), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, 5, null, null, "group", nameof(SR.ID0052))]
    [InlineData(true, null, 5, null, "area", nameof(SR.ID0053))]
    [InlineData(true, null, null, 5, "point", nameof(SR.ID0054))]
    [InlineData(true, 5, 5, 5, "group", nameof(SR.ID0052))]
    [InlineData(false, 5, 5, null, "area", nameof(SR.ID0053))]
    [InlineData(false, 5, null, 5, "point", nameof(SR.ID0054))]
    [InlineData(false, 5, 5, 5, "area", nameof(SR.ID0053))]
    public void FromScsLightPointAddress_IncompatibleArguments_ThrowsArgumentException(
        bool general, int? group, int? area, int? point, string parameterName, string resourceName)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => OpenNettyAddress.FromScsLightPointAddress(extension: 0, general, (byte?) group, (byte?) area, (byte?) point));

        Assert.Equal(parameterName, exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(resourceName), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, 3)]
    public void FromScsLightPointAddress_NoAddressKindCanBeInferred_ThrowsInvalidOperationException(int? area, int? point)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenNettyAddress.FromScsLightPointAddress(extension: 0, general: false, group: null, (byte?) area, (byte?) point));

        Assert.Equal(SR.GetResourceString(SR.ID0051), exception.Message);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(15, 255)]
    public void ScsLightPointGroupAddress_RoundTrips(byte extension, byte group)
    {
        var address = OpenNettyAddress.FromScsLightPointAddress(extension, general: false, group, area: null, point: null);

        Assert.Equal((extension, false, (byte?) group, (byte?) null, (byte?) null), OpenNettyAddress.ToScsLightPointAddress(address));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void ScsLightPointGeneralAddress_RoundTrips(byte extension)
    {
        var address = OpenNettyAddress.FromScsLightPointAddress(extension, general: true, group: null, area: null, point: null);

        Assert.Equal((extension, true, (byte?) null, (byte?) null, (byte?) null), OpenNettyAddress.ToScsLightPointAddress(address));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 5)]
    [InlineData(0, 10)]
    [InlineData(15, 0)]
    [InlineData(15, 9)]
    [InlineData(15, 10)]
    public void ScsLightPointAreaAddress_RoundTrips(byte extension, byte area)
    {
        var address = OpenNettyAddress.FromScsLightPointAddress(extension, general: false, group: null, area, point: null);

        Assert.Equal((extension, false, (byte?) null, (byte?) area, (byte?) null), OpenNettyAddress.ToScsLightPointAddress(address));
    }

    public static TheoryData<byte, byte, byte> PointToPointCombinations()
    {
        var data = new TheoryData<byte, byte, byte>();

        foreach (byte extension in (ReadOnlySpan<byte>) [0, 7, 15])
        {
            for (byte area = 0; area <= 10; area++)
            {
                for (byte point = 1; point <= 15; point++)
                {
                    data.Add(extension, area, point);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PointToPointCombinations))]
    public void ScsLightPointPointToPointAddress_RoundTrips(byte extension, byte area, byte point)
    {
        var address = OpenNettyAddress.FromScsLightPointAddress(extension, general: false, group: null, area, point);

        Assert.Equal((extension, false, (byte?) null, (byte?) area, (byte?) point), OpenNettyAddress.ToScsLightPointAddress(address));
    }

    [Theory]
    [InlineData((ushort) 0, "20")]
    [InlineData((ushort) 1, "21")]
    [InlineData((ushort) 1234, "21234")]
    [InlineData((ushort) 2047, "22047")]
    public void FromScsScenarioPlusAddress_ValidIdentifier_ReturnsScenarioPlusAddress(ushort identifier, string expected)
    {
        var address = OpenNettyAddress.FromScsScenarioPlusAddress(identifier);

        Assert.Equal(OpenNettyAddressType.ScsScenarioPlus, address.Type);
        Assert.Equal(expected, address.Value);
        Assert.Empty(address.Parameters);
    }

    [Theory]
    [InlineData((ushort) 2048)]
    [InlineData(ushort.MaxValue)]
    public void FromScsScenarioPlusAddress_IdentifierTooLarge_ThrowsArgumentOutOfRangeException(ushort identifier)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => OpenNettyAddress.FromScsScenarioPlusAddress(identifier));

        Assert.Equal("identifier", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0116), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((ushort) 0)]
    [InlineData((ushort) 1000)]
    [InlineData((ushort) 2047)]
    public void ScsScenarioPlusAddress_RoundTrips(ushort identifier)
    {
        var address = OpenNettyAddress.FromScsScenarioPlusAddress(identifier);

        Assert.Equal(identifier, OpenNettyAddress.ToScsScenarioPlusAddress(address));
    }

    [Fact]
    public void FromZigbeeAddress_NullIdentifier_ReturnsGeneralAddress()
    {
        var address = OpenNettyAddress.FromZigbeeAddress(null, 5);

        Assert.Equal(OpenNettyAddress.FromDecimalZigbeeAddress(0, 5), address);
    }

    [Fact]
    public void FromZigbeeAddress_ZigbeeSerialNumber_ReturnsZigbeeAddress()
    {
        var identifier = OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(255);

        var address = OpenNettyAddress.FromZigbeeAddress(identifier, 5);

        Assert.Equal(OpenNettyAddressType.Zigbee, address.Type);
        Assert.Equal("25505", address.Value);
    }

    [Fact]
    public void FromZigbeeAddress_MacAddress_ReturnsZigbeeAddress()
    {
        var identifier = OpenNettyDeviceIdentifier.FromMacAddress("00:04:74:00:00:00:00:FF");

        var address = OpenNettyAddress.FromZigbeeAddress(identifier, 5);

        Assert.Equal("25505", address.Value);
    }

    [Fact]
    public void FromZigbeeAddress_UnsupportedIdentifierType_ThrowsArgumentException()
    {
        var identifier = OpenNettyDeviceIdentifier.FromNitooSerialNumber(255);

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyAddress.FromZigbeeAddress(identifier, 5));

        Assert.StartsWith(SR.GetResourceString(SR.ID0113), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0#4#05", "general")]
    [InlineData("0", "general")]
    [InlineData("#5", "group")]
    [InlineData("#255#4#03", "group")]
    [InlineData("00", "area")]
    [InlineData("7", "area")]
    [InlineData("100#4#05", "area")]
    [InlineData("53", "point-to-point")]
    [InlineData("0012#4#05", "point-to-point")]
    public void IsScsLightPointAreaAddress_LightPointAddress_ReturnsTrueOnlyForAreaAddresses(string notation, string kind)
    {
        Assert.Equal(kind is "area", OpenNettyAddress.IsScsLightPointAreaAddress(LightPoint(notation)));
    }

    [Theory]
    [InlineData("0#4#05", "general")]
    [InlineData("0", "general")]
    [InlineData("#5", "group")]
    [InlineData("#255#4#03", "group")]
    [InlineData("00", "area")]
    [InlineData("7", "area")]
    [InlineData("100#4#05", "area")]
    [InlineData("53", "point-to-point")]
    [InlineData("0012#4#05", "point-to-point")]
    public void IsScsLightPointGeneralAddress_LightPointAddress_ReturnsTrueOnlyForGeneralAddresses(string notation, string kind)
    {
        Assert.Equal(kind is "general", OpenNettyAddress.IsScsLightPointGeneralAddress(LightPoint(notation)));
    }

    [Theory]
    [InlineData("0#4#05", "general")]
    [InlineData("0", "general")]
    [InlineData("#5", "group")]
    [InlineData("#255#4#03", "group")]
    [InlineData("00", "area")]
    [InlineData("7", "area")]
    [InlineData("100#4#05", "area")]
    [InlineData("53", "point-to-point")]
    [InlineData("0012#4#05", "point-to-point")]
    public void IsScsLightPointGroupAddress_LightPointAddress_ReturnsTrueOnlyForGroupAddresses(string notation, string kind)
    {
        Assert.Equal(kind is "group", OpenNettyAddress.IsScsLightPointGroupAddress(LightPoint(notation)));
    }

    [Theory]
    [InlineData("0#4#05", "general")]
    [InlineData("0", "general")]
    [InlineData("#5", "group")]
    [InlineData("#255#4#03", "group")]
    [InlineData("00", "area")]
    [InlineData("7", "area")]
    [InlineData("100#4#05", "area")]
    [InlineData("53", "point-to-point")]
    [InlineData("0012#4#05", "point-to-point")]
    public void IsScsLightPointPointToPointAddress_LightPointAddress_ReturnsTrueOnlyForPointToPointAddresses(string notation, string kind)
    {
        Assert.Equal(kind is "point-to-point", OpenNettyAddress.IsScsLightPointPointToPointAddress(LightPoint(notation)));
    }

    [Theory]
    [InlineData(OpenNettyAddressType.Unknown)]
    [InlineData(OpenNettyAddressType.Nitoo)]
    [InlineData(OpenNettyAddressType.Zigbee)]
    [InlineData(OpenNettyAddressType.ScsScenarioPlus)]
    [InlineData(OpenNettyAddressType.ScsDryContact)]
    public void IsScsLightPointPredicates_NonLightPointAddress_ReturnFalse(OpenNettyAddressType type)
    {
        var address = new OpenNettyAddress(type, "0");

        Assert.False(OpenNettyAddress.IsScsLightPointAreaAddress(address));
        Assert.False(OpenNettyAddress.IsScsLightPointGeneralAddress(address));
        Assert.False(OpenNettyAddress.IsScsLightPointGroupAddress(address));
        Assert.False(OpenNettyAddress.IsScsLightPointPointToPointAddress(address));
    }

    [Fact]
    public void IsScsLightPointPredicates_InvalidLightPointAddress_ThrowArgumentException()
    {
        var address = LightPoint("10");

        Assert.Throws<ArgumentException>(() => OpenNettyAddress.IsScsLightPointAreaAddress(address));
        Assert.Throws<ArgumentException>(() => OpenNettyAddress.IsScsLightPointGeneralAddress(address));
        Assert.Throws<ArgumentException>(() => OpenNettyAddress.IsScsLightPointGroupAddress(address));
        Assert.Throws<ArgumentException>(() => OpenNettyAddress.IsScsLightPointPointToPointAddress(address));
    }

    [Theory]
    [InlineData("0", 0u, (byte) 0)]
    [InlineData("1", 0u, (byte) 1)]
    [InlineData("16", 1u, (byte) 0)]
    [InlineData("19749", 1234u, (byte) 5)]
    [InlineData("16777216", 1048576u, (byte) 0)]
    public void ToNitooAddress_ValidAddress_ReturnsIdentifierAndUnit(string value, uint identifier, byte unit)
    {
        var address = new OpenNettyAddress(OpenNettyAddressType.Nitoo, value);

        var result = OpenNettyAddress.ToNitooAddress(address);

        Assert.Equal((identifier, unit), result);
    }

    [Theory]
    [InlineData(OpenNettyAddressType.Unknown)]
    [InlineData(OpenNettyAddressType.Zigbee)]
    [InlineData(OpenNettyAddressType.ScsLightPoint)]
    [InlineData(OpenNettyAddressType.ScsScenarioPlus)]
    [InlineData(OpenNettyAddressType.ScsDryContact)]
    public void ToNitooAddress_NonNitooAddress_ThrowsArgumentException(OpenNettyAddressType type)
    {
        var address = new OpenNettyAddress(type, "16");

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyAddress.ToNitooAddress(address));

        Assert.Equal("address", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0048), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("16777217")]
    [InlineData("4294967296")]
    public void ToNitooAddress_InvalidValue_ThrowsArgumentException(string value)
    {
        var address = new OpenNettyAddress(OpenNettyAddressType.Nitoo, value);

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyAddress.ToNitooAddress(address));

        Assert.Equal("address", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0049), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("30", (ushort) 0)]
    [InlineData("31", (ushort) 1)]
    [InlineData("342", (ushort) 42)]
    [InlineData("3201", (ushort) 201)]
    public void ToScsDryContactAddress_ValidAddress_ReturnsIdentifier(string value, ushort expected)
    {
        var address = new OpenNettyAddress(OpenNettyAddressType.ScsDryContact, value);

        Assert.Equal(expected, OpenNettyAddress.ToScsDryContactAddress(address));
    }

    [Theory]
    [InlineData(OpenNettyAddressType.Unknown)]
    [InlineData(OpenNettyAddressType.Nitoo)]
    [InlineData(OpenNettyAddressType.Zigbee)]
    [InlineData(OpenNettyAddressType.ScsLightPoint)]
    [InlineData(OpenNettyAddressType.ScsScenarioPlus)]
    public void ToScsDryContactAddress_NonDryContactAddress_ThrowsArgumentException(OpenNettyAddressType type)
    {
        var address = new OpenNettyAddress(type, "31");

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyAddress.ToScsDryContactAddress(address));

        Assert.Equal("address", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0133), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("3")]
    [InlineData("40")]
    [InlineData("3202")]
    [InlineData("399999")]
    public void ToScsDryContactAddress_InvalidValue_ThrowsArgumentException(string value)
    {
        var address = new OpenNettyAddress(OpenNettyAddressType.ScsDryContact, value);

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyAddress.ToScsDryContactAddress(address));

        Assert.Equal("address", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0134), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#5", 0, false, 5, null, null)]
    [InlineData("#255#4#03", 3, false, 255, null, null)]
    [InlineData("#1#4#15", 15, false, 1, null, null)]
    [InlineData("0", 0, true, null, null, null)]
    [InlineData("0#4#05", 5, true, null, null, null)]
    [InlineData("0#4#00", 0, true, null, null, null)]
    [InlineData("00", 0, false, null, 0, null)]
    [InlineData("1", 0, false, null, 1, null)]
    [InlineData("9", 0, false, null, 9, null)]
    [InlineData("100", 0, false, null, 10, null)]
    [InlineData("7#4#05", 5, false, null, 7, null)]
    [InlineData("00#4#02", 2, false, null, 0, null)]
    [InlineData("100#4#15", 15, false, null, 10, null)]
    [InlineData("53", 0, false, null, 5, 3)]
    [InlineData("99", 0, false, null, 9, 9)]
    [InlineData("01", 0, false, null, 0, 1)]
    [InlineData("05", 0, false, null, 0, 5)]
    [InlineData("0001", 0, false, null, 0, 1)]
    [InlineData("0010", 0, false, null, 0, 10)]
    [InlineData("0012", 0, false, null, 0, 12)]
    [InlineData("0015", 0, false, null, 0, 15)]
    [InlineData("0112", 0, false, null, 1, 12)]
    [InlineData("0915", 0, false, null, 9, 15)]
    [InlineData("1001", 0, false, null, 10, 1)]
    [InlineData("1012", 0, false, null, 10, 12)]
    [InlineData("1015", 0, false, null, 10, 15)]
    [InlineData("53#4#07", 7, false, null, 5, 3)]
    [InlineData("0012#4#15", 15, false, null, 0, 12)]
    public void ToScsLightPointAddress_ValidAddress_ReturnsAddressComponents(
        string notation, byte extension, bool general, int? group, int? area, int? point)
    {
        var address = LightPoint(notation);

        var result = OpenNettyAddress.ToScsLightPointAddress(address);

        Assert.Equal((extension, general, (byte?) group, (byte?) area, (byte?) point), result);
    }

    [Theory]
    [InlineData(OpenNettyAddressType.Unknown)]
    [InlineData(OpenNettyAddressType.Nitoo)]
    [InlineData(OpenNettyAddressType.Zigbee)]
    [InlineData(OpenNettyAddressType.ScsScenarioPlus)]
    [InlineData(OpenNettyAddressType.ScsDryContact)]
    public void ToScsLightPointAddress_NonLightPointAddress_ThrowsArgumentException(OpenNettyAddressType type)
    {
        var address = new OpenNettyAddress(type, "53");

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyAddress.ToScsLightPointAddress(address));

        Assert.Equal("address", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0050), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#0")]
    [InlineData("#256")]
    [InlineData("#5#3#03")]
    [InlineData("#5#4#16")]
    [InlineData("#5#4")]
    [InlineData("#5#4#03#1")]
    [InlineData("0#4")]
    [InlineData("0#4#16")]
    [InlineData("0#3#05")]
    [InlineData("0#5")]
    [InlineData("10")]
    [InlineData("5#4#16")]
    [InlineData("5#3#05")]
    [InlineData("5#4")]
    [InlineData("100#4#16")]
    [InlineData("100#3#02")]
    [InlineData("50")]
    [InlineData("123")]
    [InlineData("000")]
    [InlineData("0000")]
    [InlineData("0016")]
    [InlineData("0109")]
    [InlineData("0916")]
    [InlineData("1000")]
    [InlineData("1016")]
    [InlineData("1112")]
    [InlineData("53#4#16")]
    [InlineData("53#3#05")]
    [InlineData("53#4")]
    [InlineData("53#5")]
    public void ToScsLightPointAddress_InvalidAddress_ThrowsArgumentException(string notation)
    {
        var address = LightPoint(notation);

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyAddress.ToScsLightPointAddress(address));

        Assert.Equal("address", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0051), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("20", (ushort) 0)]
    [InlineData("21", (ushort) 1)]
    [InlineData("21234", (ushort) 1234)]
    [InlineData("22047", (ushort) 2047)]
    public void ToScsScenarioPlusAddress_ValidAddress_ReturnsIdentifier(string value, ushort expected)
    {
        var address = new OpenNettyAddress(OpenNettyAddressType.ScsScenarioPlus, value);

        Assert.Equal(expected, OpenNettyAddress.ToScsScenarioPlusAddress(address));
    }

    [Theory]
    [InlineData(OpenNettyAddressType.Unknown)]
    [InlineData(OpenNettyAddressType.Nitoo)]
    [InlineData(OpenNettyAddressType.Zigbee)]
    [InlineData(OpenNettyAddressType.ScsLightPoint)]
    [InlineData(OpenNettyAddressType.ScsDryContact)]
    public void ToScsScenarioPlusAddress_NonScenarioPlusAddress_ThrowsArgumentException(OpenNettyAddressType type)
    {
        var address = new OpenNettyAddress(type, "21");

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyAddress.ToScsScenarioPlusAddress(address));

        Assert.Equal("address", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0114), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2")]
    [InlineData("32")]
    [InlineData("22048")]
    [InlineData("299999")]
    public void ToScsScenarioPlusAddress_InvalidValue_ThrowsArgumentException(string value)
    {
        var address = new OpenNettyAddress(OpenNettyAddressType.ScsScenarioPlus, value);

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyAddress.ToScsScenarioPlusAddress(address));

        Assert.Equal("address", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0115), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("00", 0u, (byte) 0)]
    [InlineData("05", 0u, (byte) 5)]
    [InlineData("99", 0u, (byte) 99)]
    [InlineData("25500", 255u, (byte) 0)]
    [InlineData("25505", 255u, (byte) 5)]
    [InlineData("123499", 1234u, (byte) 99)]
    [InlineData("429496729599", uint.MaxValue, (byte) 99)]
    public void ToZigbeeAddress_ValidAddress_ReturnsIdentifierAndUnit(string value, uint identifier, byte unit)
    {
        var address = new OpenNettyAddress(OpenNettyAddressType.Zigbee, value);

        var result = OpenNettyAddress.ToZigbeeAddress(address);

        Assert.Equal((identifier, unit), result);
    }

    [Theory]
    [InlineData(OpenNettyAddressType.Unknown)]
    [InlineData(OpenNettyAddressType.Nitoo)]
    [InlineData(OpenNettyAddressType.ScsLightPoint)]
    [InlineData(OpenNettyAddressType.ScsScenarioPlus)]
    [InlineData(OpenNettyAddressType.ScsDryContact)]
    public void ToZigbeeAddress_NonZigbeeAddress_ThrowsArgumentException(OpenNettyAddressType type)
    {
        var address = new OpenNettyAddress(type, "25505");

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyAddress.ToZigbeeAddress(address));

        Assert.Equal("address", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0055), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("5")]
    [InlineData("429496729600")]
    public void ToZigbeeAddress_InvalidValue_ThrowsArgumentException(string value)
    {
        var address = new OpenNettyAddress(OpenNettyAddressType.Zigbee, value);

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyAddress.ToZigbeeAddress(address));

        Assert.Equal("address", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0056), exception.Message, StringComparison.Ordinal);
    }

    private static OpenNettyAddress LightPoint(string notation)
    {
        var parts = notation.Split('#');

        return new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, parts[0], [.. parts[1..]]);
    }

    private static OpenNettyAddress Address(string value, params string[] parameters)
        => new(OpenNettyAddressType.ScsLightPoint, value, [.. parameters]);
}
