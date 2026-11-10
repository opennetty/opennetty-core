/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyUtilitiesTests
{
    [Theory]
    [InlineData(OpenNettyProtocol.Nitoo, OpenNettyDeviceIdentifierType.NitooSerialNumber, "000123", 2, "Nitoo/000123/2")]
    [InlineData(OpenNettyProtocol.Zigbee, OpenNettyDeviceIdentifierType.ZigbeeSerialNumber, "0ABCDEF", 1, "Zigbee/0ABCDEF/1")]
    [InlineData(OpenNettyProtocol.Scs, OpenNettyDeviceIdentifierType.ScsSerialNumber, "123456", 0, "SCS/123456/0")]
    [InlineData(OpenNettyProtocol.Zigbee, OpenNettyDeviceIdentifierType.MacAddress, "00:11:22:33:44:55", 15, "Zigbee/00:11:22:33:44:55/15")]
    public void ComputeDefaultEndpointName_UnitOfADeviceWithIdentifier_ReturnsProtocolIdentifierAndUnitIdentifier(
        OpenNettyProtocol protocol, OpenNettyDeviceIdentifierType type, string identifier, byte unitId, string expected)
    {
        var unit = CreateUnit(unitId, new OpenNettyDeviceIdentifier(type, identifier));

        var name = OpenNettyUtilities.ComputeDefaultEndpointName(protocol, address: null, unit);

        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Nitoo, "Kitchen", 3, "Nitoo/Kitchen/3")]
    [InlineData(OpenNettyProtocol.Scs, "Living room", 0, "SCS/Living room/0")]
    [InlineData(OpenNettyProtocol.Zigbee, "Garage", 7, "Zigbee/Garage/7")]
    public void ComputeDefaultEndpointName_UnitOfADeviceWithoutIdentifier_ReturnsProtocolDeviceNameAndUnitIdentifier(
        OpenNettyProtocol protocol, string deviceName, byte unitId, string expected)
    {
        var unit = CreateUnit(unitId, identifier: null, deviceName);

        var name = OpenNettyUtilities.ComputeDefaultEndpointName(protocol, address: null, unit);

        Assert.Equal(expected, name);
    }

    [Fact]
    public void ComputeDefaultEndpointName_DeviceWithIdentifierAndName_PrefersTheIdentifier()
    {
        var unit = CreateUnit(1, new OpenNettyDeviceIdentifier(OpenNettyDeviceIdentifierType.NitooSerialNumber, "000123"), "Kitchen");

        var name = OpenNettyUtilities.ComputeDefaultEndpointName(OpenNettyProtocol.Nitoo, address: null, unit);

        Assert.Equal("Nitoo/000123/1", name);
    }

    [Fact]
    public void ComputeDefaultEndpointName_NitooAddress_IsIgnoredInFavorOfTheUnit()
    {
        var address = OpenNettyAddress.FromNitooAddress(123, 2);
        var unit = CreateUnit(2, new OpenNettyDeviceIdentifier(OpenNettyDeviceIdentifierType.NitooSerialNumber, "000123"));

        var name = OpenNettyUtilities.ComputeDefaultEndpointName(OpenNettyProtocol.Nitoo, address, unit);

        Assert.Equal("Nitoo/000123/2", name);
    }

    [Fact]
    public void ComputeDefaultEndpointName_ScsDryContactAddress_UsesTheUnit()
    {
        var address = OpenNettyAddress.FromScsDryContactAddress(5);
        var unit = CreateUnit(4, new OpenNettyDeviceIdentifier(OpenNettyDeviceIdentifierType.ScsSerialNumber, "123456"));

        var name = OpenNettyUtilities.ComputeDefaultEndpointName(OpenNettyProtocol.Scs, address, unit);

        Assert.Equal("SCS/123456/4", name);
    }

    [Fact]
    public void ComputeDefaultEndpointName_ScsLightPointAddressWithAnotherProtocol_UsesTheUnit()
    {
        var address = OpenNettyAddress.FromScsLightPointAddress(0, general: true, group: null, area: null, point: null);
        var unit = CreateUnit(1, new OpenNettyDeviceIdentifier(OpenNettyDeviceIdentifierType.NitooSerialNumber, "000123"));

        var name = OpenNettyUtilities.ComputeDefaultEndpointName(OpenNettyProtocol.Nitoo, address, unit);

        Assert.Equal("Nitoo/000123/1", name);
    }

    [Theory]
    [InlineData(5, "SCS/PL scenario plus/5")]
    [InlineData(0, "SCS/PL scenario plus/0")]
    [InlineData(2047, "SCS/PL scenario plus/2047")]
    public void ComputeDefaultEndpointName_ScsScenarioPlusAddress_ReturnsTheScenarioIdentifier(ushort identifier, string expected)
    {
        var address = OpenNettyAddress.FromScsScenarioPlusAddress(identifier);

        var name = OpenNettyUtilities.ComputeDefaultEndpointName(OpenNettyProtocol.Scs, address, unit: null);

        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData(0, true, null, null, null, "SCS/PL general/0")]
    [InlineData(2, true, null, null, null, "SCS/PL general/2")]
    [InlineData(0, false, null, 3, null, "SCS/PL area/0/3")]
    [InlineData(0, false, null, 0, null, "SCS/PL area/0/0")]
    [InlineData(0, false, null, 10, null, "SCS/PL area/0/10")]
    [InlineData(4, false, null, 3, null, "SCS/PL area/4/3")]
    [InlineData(0, false, 5, null, null, "SCS/PL group/0/5")]
    [InlineData(3, false, 5, null, null, "SCS/PL group/3/5")]
    [InlineData(0, false, null, 1, 2, "SCS/PL point-to-point/0/1/2")]
    [InlineData(6, false, null, 1, 12, "SCS/PL point-to-point/6/1/12")]
    public void ComputeDefaultEndpointName_ScsLightPointAddress_StartsWithTheAddressKindAndComponents(
        byte extension, bool general, int? group, int? area, int? point, string expectedPrefix)
    {
        var address = CreateScsLightPointAddress(extension, general, group, area, point);

        var name = OpenNettyUtilities.ComputeDefaultEndpointName(OpenNettyProtocol.Scs, address, unit: null);

        Assert.StartsWith(expectedPrefix, name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, true, null, null, null, "SCS/PL general/0")]
    [InlineData(0, false, null, 3, null, "SCS/PL area/0/3")]
    [InlineData(0, false, 5, null, null, "SCS/PL group/0/5")]
    [InlineData(0, false, null, 1, 2, "SCS/PL point-to-point/0/1/2")]
    public void ComputeDefaultEndpointName_ScsLightPointAddress_ReturnsExactlyTheAddressKindAndComponents(
        byte extension, bool general, int? group, int? area, int? point, string expected)
    {
        var address = CreateScsLightPointAddress(extension, general, group, area, point);

        var name = OpenNettyUtilities.ComputeDefaultEndpointName(OpenNettyProtocol.Scs, address, unit: null);

        Assert.Equal(expected, name);
    }

    [Fact]
    public void ComputeDefaultEndpointName_DeviceWithoutIdentifierAndName_ThrowsInvalidOperationException()
    {
        var unit = CreateUnit(1);

        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenNettyUtilities.ComputeDefaultEndpointName(OpenNettyProtocol.Nitoo, address: null, unit));

        Assert.Equal(SR.GetResourceString(SR.ID0104), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public void ComputeDefaultEndpointName_NoUnitAndNoSpecificAddress_ThrowsInvalidOperationException(OpenNettyProtocol protocol)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenNettyUtilities.ComputeDefaultEndpointName(protocol, address: null, unit: null));

        Assert.Equal(SR.GetResourceString(SR.ID0104), exception.Message);
    }

    [Fact]
    public void ComputeDefaultEndpointName_UnitWithoutDevice_ThrowsInvalidOperationException()
    {
        var unit = new OpenNettyUnit { Definition = new OpenNettyUnitDefinition { Id = 1 } };

        Assert.Throws<InvalidOperationException>(
            () => OpenNettyUtilities.ComputeDefaultEndpointName(OpenNettyProtocol.Nitoo, address: null, unit));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    [InlineData(255)]
    public void ComputeDefaultEndpointName_UndefinedProtocol_ThrowsInvalidEnumArgumentException(int protocol)
    {
        Assert.Throws<InvalidEnumArgumentException>("protocol", () => OpenNettyUtilities.ComputeDefaultEndpointName(
            (OpenNettyProtocol) protocol, address: null, unit: null));
    }

    [Theory]
    [InlineData(OpenNettyBrand.BTicino, "BTicino")]
    [InlineData(OpenNettyBrand.Legrand, "Legrand")]
    public void GetBrandName_DefinedBrand_ReturnsTheBrandName(OpenNettyBrand brand, string expected)
    {
        Assert.Equal(expected, OpenNettyUtilities.GetBrandName(brand));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(100)]
    public void GetBrandName_UndefinedBrand_ThrowsInvalidEnumArgumentException(int brand)
    {
        Assert.Throws<InvalidEnumArgumentException>("brand", () => OpenNettyUtilities.GetBrandName((OpenNettyBrand) brand));
    }

    private static OpenNettyAddress CreateScsLightPointAddress(byte extension, bool general, int? group, int? area, int? point)
        => OpenNettyAddress.FromScsLightPointAddress(extension, general, (byte?) group, (byte?) area, (byte?) point);

    private static OpenNettyUnit CreateUnit(byte id, OpenNettyDeviceIdentifier? identifier = null, string? deviceName = null)
    {
        var device = new OpenNettyDevice { Identifier = identifier };

        if (deviceName is not null)
        {
            device.Name = deviceName;
        }

        return new OpenNettyUnit
        {
            Definition = new OpenNettyUnitDefinition { Id = id },
            Device = device
        };
    }
}
