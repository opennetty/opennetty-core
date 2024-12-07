/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Globalization;

namespace OpenNetty;

/// <summary>
/// Exposes common OpenNetty models, as defined by the Nitoo and MyHome specifications.
/// </summary>
public static class OpenNettyModels
{
    /// <summary>
    /// Lighting models (WHO = 1).
    /// </summary>
    public static class Lighting
    {
        /// <summary>
        /// Switch state.
        /// </summary>
        public enum SwitchState
        {
            /// <summary>
            /// Off.
            /// </summary>
            Off = 0,

            /// <summary>
            /// On.
            /// </summary>
            On = 1
        }
    }

    /// <summary>
    /// Temperature control models (WHO = 4).
    /// </summary>
    public static class TemperatureControl
    {
        /// <summary>
        /// Pilot wire mode.
        /// </summary>
        public enum PilotWireMode
        {
            /// <summary>
            /// Comfort.
            /// </summary>
            Comfort = 0,

            /// <summary>
            /// Comfort - 1°C.
            /// </summary>
            ComfortMinusOne = 1,

            /// <summary>
            /// Comfort - 2°C.
            /// </summary>
            ComfortMinusTwo = 2,

            /// <summary>
            /// Eco (comfort - 4°C).
            /// </summary>
            Eco = 3,

            /// <summary>
            /// Frost protection (~7°C).
            /// </summary>
            FrostProtection = 4
        }

        /// <summary>
        /// Pilot wire configuration.
        /// </summary>
        public sealed record class PilotWireConfiguration
        {
            /// <summary>
            /// Gets or sets the derogation duration.
            /// </summary>
            public required PilotWireDerogationDuration? DerogationDuration { get; init; }

            /// <summary>
            /// Gets or sets a boolean indicating whether a derogation is active.
            /// </summary>
            public required bool IsDerogationActive { get; init; }

            /// <summary>
            /// Gets or sets the pilot wire mode.
            /// </summary>
            public required PilotWireMode Mode { get; init; }

            /// <summary>
            /// Creates a new instance of the <see cref="PilotWireConfiguration"/> class using the specified unit description.
            /// </summary>
            /// <param name="values">The unit description values.</param>
            /// <returns>A new instance of the <see cref="PilotWireConfiguration"/> class.</returns>
            public static PilotWireConfiguration CreateFromUnitDescription(ImmutableArray<string> values) => new()
            {
                DerogationDuration = ushort.Parse(values[0], CultureInfo.InvariantCulture) switch
                {
                    >=  8 and <  72 => PilotWireDerogationDuration.None,
                    >= 72 and < 136 => PilotWireDerogationDuration.FourHours,
                    >= 136          => PilotWireDerogationDuration.EightHours,

                    _ => null
                },
                IsDerogationActive = ushort.Parse(values[0], CultureInfo.InvariantCulture) is >= 8,
                Mode               = values[0] switch
                {
                    "0" or "8"  or "72" or "136" => PilotWireMode.Comfort,
                    "1" or "9"  or "73" or "137" => PilotWireMode.ComfortMinusOne,
                    "2" or "10" or "74" or "138" => PilotWireMode.ComfortMinusTwo,
                    "3" or "11" or "75" or "139" => PilotWireMode.Eco,
                    "4" or "12" or "76" or "140" => PilotWireMode.FrostProtection,

                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                }
            };
        }

        /// <summary>
        /// Pilot wire derogation duration.
        /// </summary>
        public enum PilotWireDerogationDuration
        {
            /// <summary>
            /// None.
            /// </summary>
            None = 0,

            /// <summary>
            /// 4 hours.
            /// </summary>
            FourHours = 1,

            /// <summary>
            /// 8 hours.
            /// </summary>
            EightHours = 2
        }

        /// <summary>
        /// Smart meter indexes.
        /// </summary>
        public sealed record class SmartMeterIndexes
        {
            /// <summary>
            /// Gets or sets the base index.
            /// </summary>
            public required ulong BaseIndex { get; init; }

            /// <summary>
            /// Gets or sets the blue index, if available.
            /// </summary>
            public required ulong? BlueIndex { get; init; }

            /// <summary>
            /// Gets or sets the off-peak index, if available.
            /// </summary>
            public required ulong? OffPeakIndex { get; init; }

            /// <summary>
            /// Gets or sets the red index, if available.
            /// </summary>
            public required ulong? RedIndex { get; init; }

            /// <summary>
            /// Gets or sets the subscription type.
            /// </summary>
            public required SmartMeterSubscriptionType SubscriptionType { get; init; }

            /// <summary>
            /// Gets or sets the blue index, if available.
            /// </summary>
            public required ulong? WhiteIndex { get; init; }

            /// <summary>
            /// Creates a new instance of the <see cref="SmartMeterIndexes"/> class using the specified unit description.
            /// </summary>
            /// <param name="values">The unit description values.</param>
            /// <returns>A new instance of the <see cref="SmartMeterIndexes"/> class.</returns>
            public static SmartMeterIndexes CreateFromDimensionValues(ImmutableArray<string> values) => new()
            {
                BaseIndex        = ulong.Parse(values[1], CultureInfo.InvariantCulture),
                BlueIndex        = values[0] is "3" ? ulong.Parse(values[2], CultureInfo.InvariantCulture) : null,
                OffPeakIndex     = values[0] is "2" ? ulong.Parse(values[2], CultureInfo.InvariantCulture) : null,
                RedIndex         = values[0] is "5" ? ulong.Parse(values[2], CultureInfo.InvariantCulture) : null,
                SubscriptionType = values[0] switch
                {
                    "1"               => SmartMeterSubscriptionType.Base,
                    "2"               => SmartMeterSubscriptionType.OffPeak,
                    "3" or "4" or "5" => SmartMeterSubscriptionType.Tempo,

                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                },
                WhiteIndex       = values[0] is "4" ? ulong.Parse(values[2], CultureInfo.InvariantCulture) : null,
            };
        }

        /// <summary>
        /// Smart meter information.
        /// </summary>
        public sealed record class SmartMeterInformation
        {
            /// <summary>
            /// Gets or sets a boolean indicating whether a power cut is active.
            /// </summary>
            public required bool IsPowerCutActive { get; init; }

            /// <summary>
            /// Gets or sets the rate type.
            /// </summary>
            public required SmartMeterRateType RateType { get; init; }

            /// <summary>
            /// Creates a new instance of the <see cref="SmartMeterInformation"/> class using the specified unit description.
            /// </summary>
            /// <param name="values">The unit description values.</param>
            /// <returns>A new instance of the <see cref="SmartMeterInformation"/> class.</returns>
            public static SmartMeterInformation CreateFromUnitDescription(ImmutableArray<string> values) => new()
            {
                IsPowerCutActive = values[0] is "33" or "49",
                RateType         = values[0] switch
                {
                    "32" or "33" => SmartMeterRateType.OffPeak,
                    "48" or "49" => SmartMeterRateType.Peak,

                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                }
            };
        }

        /// <summary>
        /// Smart meter subscription type.
        /// </summary>
        public enum SmartMeterSubscriptionType
        {
            /// <summary>
            /// Base.
            /// </summary>
            Base = 0,

            /// <summary>
            /// Off-peak.
            /// </summary>
            OffPeak = 1,

            /// <summary>
            /// Tempo.
            /// </summary>
            Tempo = 2
        }

        /// <summary>
        /// Smart meter rate type.
        /// </summary>
        public enum SmartMeterRateType
        {
            /// <summary>
            /// Peak.
            /// </summary>
            Peak = 0,

            /// <summary>
            /// Off-peak.
            /// </summary>
            OffPeak = 1
        }

        /// <summary>
        /// Water heater mode.
        /// </summary>
        public enum WaterHeaterMode
        {
            /// <summary>
            /// Automatic.
            /// </summary>
            Automatic = 0,

            /// <summary>
            /// Forced off (no hot water will be produced).
            /// </summary>
            ForcedOff = 1,

            /// <summary>
            /// Forced on (hot water will be produced until the next off-peak signal, if available).
            /// </summary>
            ForcedOn = 2
        }

        /// <summary>
        /// Water heater state.
        /// </summary>
        public enum WaterHeaterState
        {
            /// <summary>
            /// Idle.
            /// </summary>
            Idle = 0,

            /// <summary>
            /// Heating.
            /// </summary>
            Heating = 1
        }
    }

    /// <summary>
    /// Alarm models (WHO = 5).
    /// </summary>
    public static class Alarm
    {
        /// <summary>
        /// Wireless burglar alarm state.
        /// </summary>
        public enum WirelessBurglarAlarmState
        {
            /// <summary>
            /// Disarmed.
            /// </summary>
            Disarmed = 0,

            /// <summary>
            /// Armed.
            /// </summary>
            Armed = 1,

            /// <summary>
            /// Partially armed.
            /// </summary>
            PartiallyArmed = 2,

            /// <summary>
            /// Exit delay elapsed.
            /// </summary>
            ExitDelayElapsed = 3,

            /// <summary>
            /// Alarm triggered.
            /// </summary>
            Triggered = 4,

            /// <summary>
            /// Event detected.
            /// </summary>
            EventDetected = 5
        }
    }

    /// <summary>
    /// Diagnostics models (WHO = 1000).
    /// </summary>
    public static class Diagnostics
    {
        /// <summary>
        /// Device description.
        /// </summary>
        public sealed record class DeviceDescription
        {
            /// <summary>
            /// Gets or sets the function code.
            /// </summary>
            public required ushort FunctionCode { get; init; }

            /// <summary>
            /// Gets or sets the device model.
            /// </summary>
            public required string Model { get; init; }

            /// <summary>
            /// Gets or sets the number of units available.
            /// </summary>
            public required ushort Units { get; init; }

            /// <summary>
            /// Gets or sets the device version.
            /// </summary>
            public required Version Version { get; init; }

            /// <summary>
            /// Creates a new instance of the <see cref="DeviceDescription"/> class using the specified unit description.
            /// </summary>
            /// <param name="values">The unit description values.</param>
            /// <returns>A new instance of the <see cref="DeviceDescription"/> class.</returns>
            public static DeviceDescription CreateFromDeviceDescription(ImmutableArray<string> values) => new()
            {
                FunctionCode = ushort.Parse(values[2], CultureInfo.InvariantCulture),
                Model        = uint.Parse(values[0], CultureInfo.InvariantCulture).ToString("X"),
                Units        = ushort.Parse(values[3], CultureInfo.InvariantCulture),
                Version      = new Version(int.Parse(uint.Parse(values[1], CultureInfo.InvariantCulture).ToString("X")), 0)
            };
        }

        /// <summary>
        /// Memory data.
        /// </summary>
        public sealed record class MemoryData
        {
            /// <summary>
            /// Gets or sets the address.
            /// </summary>
            public required OpenNettyAddress Address { get; init; }

            /// <summary>
            /// Gets or sets the function code.
            /// </summary>
            public required ushort FunctionCode { get; init; }

            /// <summary>
            /// Gets or sets the media.
            /// </summary>
            public required OpenNettyMedia Media { get; init; }

            /// <summary>
            /// Creates a new instance of the <see cref="MemoryData"/> class using the specified unit description.
            /// </summary>
            /// <param name="values">The unit description values.</param>
            /// <returns>A new instance of the <see cref="MemoryData"/> class.</returns>
            public static MemoryData CreateFromUnitDescription(ImmutableArray<string> values) => new()
            {
                Address      = new OpenNettyAddress(OpenNettyAddressType.NitooDevice, values[1]),
                FunctionCode = ushort.Parse(values[2], CultureInfo.InvariantCulture),
                Media        = values[0] switch
                {
                    "64"  => OpenNettyMedia.Radio,
                    "96"  => OpenNettyMedia.Powerline,
                    "128" => OpenNettyMedia.Infrared,

                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                }
            };
        }

        /// <summary>
        /// Unit description.
        /// </summary>
        public sealed record class UnitDescription
        {
            /// <summary>
            /// Gets or sets the function code.
            /// </summary>
            public required ushort FunctionCode { get; init; }

            /// <summary>
            /// Gets or sets the values.
            /// </summary>
            public ImmutableArray<string> Values { get; init; }

            /// <summary>
            /// Creates a new instance of the <see cref="UnitDescription"/> class using the specified unit description.
            /// </summary>
            /// <param name="values">The unit description values.</param>
            /// <returns>A new instance of the <see cref="UnitDescription"/> class.</returns>
            public static UnitDescription CreateFromUnitDescription(ImmutableArray<string> values) => new()
            {
                FunctionCode = ushort.Parse(values[0], CultureInfo.InvariantCulture),
                Values       = values[1..]
            };
        }
    }
}
