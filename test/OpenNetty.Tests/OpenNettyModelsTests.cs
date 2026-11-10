/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyModelsTests
{
    public static TheoryData<string[]> SingleValueInvalidShapes => new()
    {
        { [] },
        { [""] },
        { ["1", "2"] }
    };

    public static TheoryData<string[]> SmartMeterIndexesInvalidShapes => new()
    {
        { [] },
        { ["1"] },
        { ["", "100"] },
        { ["1", ""] }
    };

    public static TheoryData<string[]> DeviceDescriptionInvalidShapes => new()
    {
        { [] },
        { ["1234", "16", "5"] },
        { ["1234", "16", "5", "3", "7"] },
        { ["1234", "", "5", "3"] }
    };

    public static TheoryData<string[]> UnitDescriptionInvalidShapes => new()
    {
        { [] },
        { ["5"] },
        { ["", "a"] },
        { ["5", ""] }
    };

    [Theory]
    [InlineData("0", OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, false, false, OpenNettyModels.TemperatureControl.PilotWireMode.Comfort)]
    [InlineData("1", OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, false, false, OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne)]
    [InlineData("2", OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, false, false, OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo)]
    [InlineData("3", OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, false, false, OpenNettyModels.TemperatureControl.PilotWireMode.Eco)]
    [InlineData("4", OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, false, false, OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection)]
    [InlineData("8", OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, true, false, OpenNettyModels.TemperatureControl.PilotWireMode.Comfort)]
    [InlineData("16", OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, false, true, OpenNettyModels.TemperatureControl.PilotWireMode.Comfort)]
    [InlineData("64", OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, false, false, OpenNettyModels.TemperatureControl.PilotWireMode.Comfort)]
    [InlineData("72", OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, true, false, OpenNettyModels.TemperatureControl.PilotWireMode.Comfort)]
    [InlineData("128", OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, false, false, OpenNettyModels.TemperatureControl.PilotWireMode.Comfort)]
    [InlineData("147", OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, false, true, OpenNettyModels.TemperatureControl.PilotWireMode.Eco)]
    [InlineData("156", OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, true, true, OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection)]
    public void PilotWireConfiguration_CreateFromUnitDescription_ValidValue_DecodesBitFields(
        string value, OpenNettyModels.TemperatureControl.PilotWireDerogationDuration duration, bool derogation, bool shutdown, OpenNettyModels.TemperatureControl.PilotWireMode mode)
    {
        var configuration = OpenNettyModels.TemperatureControl.PilotWireConfiguration.CreateFromUnitDescription([value]);

        Assert.Equal(duration, configuration.DerogationDuration);
        Assert.Equal(derogation, configuration.IsDerogationActive);
        Assert.Equal(shutdown, configuration.IsShutdownActive);
        Assert.Equal(mode, configuration.Mode);
    }

    [Theory]
    [MemberData(nameof(SingleValueInvalidShapes))]
    public void PilotWireConfiguration_CreateFromUnitDescription_UnexpectedValues_ThrowsInvalidOperationException(string[] values)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OpenNettyModels.TemperatureControl.PilotWireConfiguration.CreateFromUnitDescription(values));

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Theory]
    [InlineData("192")]
    [InlineData("5")]
    [InlineData("6")]
    [InlineData("7")]
    public void PilotWireConfiguration_CreateFromUnitDescription_UnsupportedBits_ThrowsInvalidDataException(string value)
    {
        var exception = Assert.Throws<InvalidDataException>(() => OpenNettyModels.TemperatureControl.PilotWireConfiguration.CreateFromUnitDescription([value]));

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public void PilotWireConfiguration_CreateFromUnitDescription_NonNumericValue_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => OpenNettyModels.TemperatureControl.PilotWireConfiguration.CreateFromUnitDescription(["abc"]));
    }

    [Fact]
    public void PilotWireTransmissionOptions_ExcludeAssociatedDevices_DefaultsToFalse()
    {
        Assert.False(new OpenNettyModels.TemperatureControl.PilotWireTransmissionOptions().ExcludeAssociatedDevices);
        Assert.True(new OpenNettyModels.TemperatureControl.PilotWireTransmissionOptions { ExcludeAssociatedDevices = true }.ExcludeAssociatedDevices);
    }

    [Fact]
    public void SmartMeterIndexes_CreateFromDimensionValues_BaseSubscription_ReturnsSimpleBaseIndex()
    {
        var indexes = OpenNettyModels.TemperatureControl.SmartMeterIndexes.CreateFromDimensionValues(["1", "1500"]);

        var expected = new OpenNettyModels.TemperatureControl.SmartMeterIndexes
        {
            BaseIndex = new OpenNettyModels.TemperatureControl.SmartMeterIndex { BaseIndex = 1500, OffPeakIndex = 0 },
            BlueIndex = null,
            PeakOffPeakIndex = null,
            RedIndex = null,
            SubscriptionType = OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.Base,
            WhiteIndex = null
        };

        Assert.Equal(expected, indexes);
    }

    [Fact]
    public void SmartMeterIndexes_CreateFromDimensionValues_BaseSubscription_IgnoresAdditionalValues()
    {
        var indexes = OpenNettyModels.TemperatureControl.SmartMeterIndexes.CreateFromDimensionValues(["1", "1500", "abc"]);

        Assert.Equal(new OpenNettyModels.TemperatureControl.SmartMeterIndex { BaseIndex = 1500, OffPeakIndex = 0 }, indexes.BaseIndex);
    }

    [Theory]
    [InlineData("2", OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.PeakOffPeak)]
    [InlineData("3", OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.Tempo)]
    [InlineData("4", OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.Tempo)]
    [InlineData("5", OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.Tempo)]
    public void SmartMeterIndexes_CreateFromDimensionValues_ComplexSubscription_ReturnsOnlyMatchingIndex(
        string subscription, OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType expectedType)
    {
        var index = new OpenNettyModels.TemperatureControl.SmartMeterIndex { BaseIndex = 1500, OffPeakIndex = 2500 };

        var indexes = OpenNettyModels.TemperatureControl.SmartMeterIndexes.CreateFromDimensionValues([subscription, "1500", "2500"]);

        Assert.Equal(expectedType, indexes.SubscriptionType);
        Assert.Equal(subscription is "2" ? index : null, indexes.PeakOffPeakIndex);
        Assert.Equal(subscription is "3" ? index : null, indexes.BlueIndex);
        Assert.Equal(subscription is "4" ? index : null, indexes.WhiteIndex);
        Assert.Equal(subscription is "5" ? index : null, indexes.RedIndex);
        Assert.Null(indexes.BaseIndex);
    }

    [Fact]
    public void SmartMeterIndexes_CreateFromDimensionValues_LargeIndexes_AreParsedAsUnsigned64BitIntegers()
    {
        var indexes = OpenNettyModels.TemperatureControl.SmartMeterIndexes.CreateFromDimensionValues(["2", "18446744073709551615", "4294967296"]);

        Assert.Equal(ulong.MaxValue, indexes.PeakOffPeakIndex!.BaseIndex);
        Assert.Equal(4294967296UL, indexes.PeakOffPeakIndex.OffPeakIndex);
    }

    [Theory]
    [MemberData(nameof(SmartMeterIndexesInvalidShapes))]
    public void SmartMeterIndexes_CreateFromDimensionValues_UnexpectedValues_ThrowsInvalidOperationException(string[] values)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OpenNettyModels.TemperatureControl.SmartMeterIndexes.CreateFromDimensionValues(values));

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("abc")]
    public void SmartMeterIndexes_CreateFromDimensionValues_UnknownSubscription_ThrowsInvalidDataException(string subscription)
    {
        var exception = Assert.Throws<InvalidDataException>(
            () => OpenNettyModels.TemperatureControl.SmartMeterIndexes.CreateFromDimensionValues([subscription, "1500"]));

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public void SmartMeterIndexes_CreateFromDimensionValues_NonNumericIndex_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => OpenNettyModels.TemperatureControl.SmartMeterIndexes.CreateFromDimensionValues(["1", "abc"]));
    }

    [Theory]
    [InlineData("32", OpenNettyModels.TemperatureControl.SmartMeterRateType.OffPeak, false)]
    [InlineData("33", OpenNettyModels.TemperatureControl.SmartMeterRateType.OffPeak, true)]
    [InlineData("48", OpenNettyModels.TemperatureControl.SmartMeterRateType.Peak, false)]
    [InlineData("49", OpenNettyModels.TemperatureControl.SmartMeterRateType.Peak, true)]
    public void SmartMeterInformation_CreateFromUnitDescription_ValidValue_ReturnsRateTypeAndPowerCutState(
        string value, OpenNettyModels.TemperatureControl.SmartMeterRateType rateType, bool powerCut)
    {
        var information = OpenNettyModels.TemperatureControl.SmartMeterInformation.CreateFromUnitDescription([value]);

        Assert.Equal(rateType, information.RateType);
        Assert.Equal(powerCut, information.IsPowerCutActive);
    }

    [Theory]
    [MemberData(nameof(SingleValueInvalidShapes))]
    public void SmartMeterInformation_CreateFromUnitDescription_UnexpectedValues_ThrowsInvalidOperationException(string[] values)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OpenNettyModels.TemperatureControl.SmartMeterInformation.CreateFromUnitDescription(values));

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("34")]
    [InlineData("abc")]
    public void SmartMeterInformation_CreateFromUnitDescription_UnknownValue_ThrowsInvalidDataException(string value)
    {
        var exception = Assert.Throws<InvalidDataException>(() => OpenNettyModels.TemperatureControl.SmartMeterInformation.CreateFromUnitDescription([value]));

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Theory]
    [InlineData("1234", "16", "5", "3", "4D2", 10, 5, 3)]
    [InlineData("255", "18", "0", "1", "FF", 12, 0, 1)]
    [InlineData("0", "0", "255", "255", "0", 0, 255, 255)]
    public void DeviceDescription_CreateFromDeviceDescription_ValidValues_ReturnsDecodedDescription(
        string model, string version, string functionCode, string units,
        string expectedModel, int expectedMajorVersion, byte expectedFunctionCode, byte expectedUnits)
    {
        var description = OpenNettyModels.Diagnostics.DeviceDescription.CreateFromDeviceDescription([model, version, functionCode, units]);

        Assert.Equal(expectedModel, description.Model);
        Assert.Equal(new Version(expectedMajorVersion, 0), description.Version);
        Assert.Equal(expectedFunctionCode, description.FunctionCode);
        Assert.Equal(expectedUnits, description.Units);
    }

    [Theory]
    [MemberData(nameof(DeviceDescriptionInvalidShapes))]
    public void DeviceDescription_CreateFromDeviceDescription_UnexpectedValues_ThrowsInvalidOperationException(string[] values)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OpenNettyModels.Diagnostics.DeviceDescription.CreateFromDeviceDescription(values));

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public void DeviceDescription_CreateFromDeviceDescription_FunctionCodeOutOfRange_ThrowsOverflowException()
    {
        Assert.Throws<OverflowException>(() => OpenNettyModels.Diagnostics.DeviceDescription.CreateFromDeviceDescription(["1234", "16", "256", "3"]));
    }

    [Theory]
    [InlineData("64", OpenNettyMedium.Radio)]
    [InlineData("96", OpenNettyMedium.Powerline)]
    [InlineData("128", OpenNettyMedium.Infrared)]
    public void MemoryData_CreateFromUnitDescription_ValidValues_ReturnsMediumAddressAndFunctionCode(
        string medium, OpenNettyMedium expectedMedium)
    {
        var data = OpenNettyModels.Diagnostics.MemoryData.CreateFromUnitDescription([medium, "305", "2", "1"]);

        Assert.Equal(expectedMedium, data.Medium);
        Assert.Equal(OpenNettyAddress.FromNitooAddress(identifier: 19, unit: 1), data.Address);
        Assert.Equal(2, data.FunctionCode);
    }

    [Fact]
    public void MemoryData_CreateFromUnitDescription_AddressWithZeroUnit_ReturnsDeviceAddress()
    {
        var data = OpenNettyModels.Diagnostics.MemoryData.CreateFromUnitDescription(["64", "304", "2", "1"]);

        Assert.Equal(OpenNettyAddress.FromNitooAddress(identifier: 19, unit: 0), data.Address);
    }

    [Theory]
    [MemberData(nameof(DeviceDescriptionInvalidShapes))]
    public void MemoryData_CreateFromUnitDescription_UnexpectedValues_ThrowsInvalidOperationException(string[] values)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OpenNettyModels.Diagnostics.MemoryData.CreateFromUnitDescription(values));

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65")]
    [InlineData("abc")]
    public void MemoryData_CreateFromUnitDescription_UnknownMedium_ThrowsInvalidDataException(string medium)
    {
        var exception = Assert.Throws<InvalidDataException>(
            () => OpenNettyModels.Diagnostics.MemoryData.CreateFromUnitDescription([medium, "305", "2", "1"]));

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Theory]
    [InlineData("5", new[] { "a" })]
    [InlineData("5", new[] { "a", "b", "c" })]
    public void UnitDescription_CreateFromUnitDescription_ValidValues_ReturnsFunctionCodeAndRemainingValues(
        string functionCode, string[] remainingValues)
    {
        var description = OpenNettyModels.Diagnostics.UnitDescription.CreateFromUnitDescription([functionCode, .. remainingValues]);

        Assert.Equal(5, description.FunctionCode);
        Assert.Equal(remainingValues, description.Values, StringComparer.Ordinal);
    }

    [Fact]
    public void UnitDescription_CreateFromUnitDescription_EmptyRemainingValue_IsPreserved()
    {
        var description = OpenNettyModels.Diagnostics.UnitDescription.CreateFromUnitDescription(["5", "a", ""]);

        Assert.Equal(["a", ""], description.Values, StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(UnitDescriptionInvalidShapes))]
    public void UnitDescription_CreateFromUnitDescription_UnexpectedValues_ThrowsInvalidOperationException(string[] values)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OpenNettyModels.Diagnostics.UnitDescription.CreateFromUnitDescription(values));

        Assert.Equal(SR.GetResourceString(SR.ID0068), exception.Message);
    }

    [Fact]
    public void UnitDescription_CreateFromUnitDescription_NonNumericFunctionCode_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => OpenNettyModels.Diagnostics.UnitDescription.CreateFromUnitDescription(["abc", "a"]));
    }
}
