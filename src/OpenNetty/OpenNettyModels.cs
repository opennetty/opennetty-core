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
        /// ON/OFF scenario type.
        /// </summary>
        public enum OnOffScenarioType
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
    /// Automation models (WHO = 2).
    /// </summary>
    public static class Automation
    {
        /// <summary>
        /// Shutter state.
        /// </summary>
        public enum ShutterState
        {
            /// <summary>
            /// Stopped.
            /// </summary>
            Stopped = 0,

            /// <summary>
            /// Opening.
            /// </summary>
            Opening = 1,

            /// <summary>
            /// Closing.
            /// </summary>
            Closing = 2,

            /// <summary>
            /// Open.
            /// </summary>
            /// <remarks>
            /// Note: this value is only supported by advanced shutter actuators.
            /// </remarks>
            Open = 3,

            /// <summary>
            /// Closed.
            /// </summary>
            /// <remarks>
            /// Note: this value is only supported by advanced shutter actuators.
            /// </remarks>
            Closed = 4
        }

        /// <summary>
        /// STOP/UP/DOWN scenario type.
        /// </summary>
        public enum StopUpDownScenarioType
        {
            /// <summary>
            /// Stop.
            /// </summary>
            Stop = 0,

            /// <summary>
            /// Up.
            /// </summary>
            Up = 1,

            /// <summary>
            /// Down.
            /// </summary>
            Down = 2
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
            /// Gets or sets a boolean indicating whether a shutdown is active.
            /// </summary>
            public required bool IsShutdownActive { get; init; }

            /// <summary>
            /// Gets or sets the pilot wire mode.
            /// </summary>
            public required PilotWireMode Mode { get; init; }

            /// <summary>
            /// Creates a new instance of the <see cref="PilotWireConfiguration"/> class using the specified unit description.
            /// </summary>
            /// <param name="values">The unit description values.</param>
            /// <returns>A new instance of the <see cref="PilotWireConfiguration"/> class.</returns>
            public static PilotWireConfiguration CreateFromUnitDescription(ReadOnlySpan<string> values)
            {
                if (values is not [{ Length: > 0 }])
                {
                    throw new InvalidOperationException(SR.GetResourceString(SR.ID0068));
                }

                var value = byte.Parse(values[0], CultureInfo.InvariantCulture);

                return new()
                {
                    DerogationDuration = (value & 0b_1100_0000) switch
                    {
                        0b_0000_0000 => PilotWireDerogationDuration.None,
                        0b_0100_0000 => PilotWireDerogationDuration.FourHours,
                        0b_1000_0000 => PilotWireDerogationDuration.EightHours,

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                    },
                    IsDerogationActive = (value & 0b_0000_1000) is not 0,
                    IsShutdownActive   = (value & 0b_0001_0000) is not 0,
                    Mode               = (value & 0b_0000_0111) switch
                    {
                        0 => PilotWireMode.Comfort,
                        1 => PilotWireMode.ComfortMinusOne,
                        2 => PilotWireMode.ComfortMinusTwo,
                        3 => PilotWireMode.Eco,
                        4 => PilotWireMode.FrostProtection,

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                    }
                };
            }
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
        /// Pilot wire transmission options.
        /// </summary>
        public sealed record class PilotWireTransmissionOptions
        {
            /// <summary>
            /// Gets or sets a boolean indicating whether devices previously associated via a Push&amp;Learn binding
            /// should be prevented from applying the request sent to the targeted endpoint (for instance, to prevent
            /// pilot wire cable outlets from transitioning to the same state as a pilot wire derogation command device).
            /// </summary>
            public bool ExcludeAssociatedDevices { get; init; }
        }

        /// <summary>
        /// Smart meter index.
        /// </summary>
        public sealed record class SmartMeterIndex
        {
            /// <summary>
            /// Gets or sets the base index.
            /// </summary>
            public required ulong BaseIndex { get; init; }

            /// <summary>
            /// Gets or sets the off-peak index.
            /// </summary>
            public required ulong OffPeakIndex { get; init; }
        }

        /// <summary>
        /// Smart meter indexes.
        /// </summary>
        public sealed record class SmartMeterIndexes
        {
            /// <summary>
            /// Gets or sets the base index.
            /// </summary>
            public required SmartMeterIndex? BaseIndex { get; init; }

            /// <summary>
            /// Gets or sets the blue index, if available.
            /// </summary>
            public required SmartMeterIndex? BlueIndex { get; init; }

            /// <summary>
            /// Gets or sets the peak/off-peak index, if available.
            /// </summary>
            public required SmartMeterIndex? PeakOffPeakIndex { get; init; }

            /// <summary>
            /// Gets or sets the red index, if available.
            /// </summary>
            public required SmartMeterIndex? RedIndex { get; init; }

            /// <summary>
            /// Gets or sets the subscription type.
            /// </summary>
            public required SmartMeterSubscriptionType SubscriptionType { get; init; }

            /// <summary>
            /// Gets or sets the blue index, if available.
            /// </summary>
            public required SmartMeterIndex? WhiteIndex { get; init; }

            /// <summary>
            /// Creates a new instance of the <see cref="SmartMeterIndexes"/> class using the specified unit description.
            /// </summary>
            /// <param name="values">The unit description values.</param>
            /// <returns>A new instance of the <see cref="SmartMeterIndexes"/> class.</returns>
            public static SmartMeterIndexes CreateFromDimensionValues(ReadOnlySpan<string> values)
            {
                if (values is not [{ Length: > 0 }, { Length: > 0 }, ..])
                {
                    throw new InvalidOperationException(SR.GetResourceString(SR.ID0068));
                }

                return new()
                {
                    BaseIndex        = values[0] is "1" ? GetSimpleIndex(values)  : null,
                    BlueIndex        = values[0] is "3" ? GetComplexIndex(values) : null,
                    PeakOffPeakIndex = values[0] is "2" ? GetComplexIndex(values) : null,
                    RedIndex         = values[0] is "5" ? GetComplexIndex(values) : null,
                    SubscriptionType = values[0] switch
                    {
                        "1"               => SmartMeterSubscriptionType.Base,
                        "2"               => SmartMeterSubscriptionType.PeakOffPeak,
                        "3" or "4" or "5" => SmartMeterSubscriptionType.Tempo,

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                    },
                    WhiteIndex       = values[0] is "4" ? GetComplexIndex(values) : null
                };

                static SmartMeterIndex GetComplexIndex(ReadOnlySpan<string> values) => new()
                {
                    BaseIndex    = ulong.Parse(values[1], CultureInfo.InvariantCulture),
                    OffPeakIndex = ulong.Parse(values[2], CultureInfo.InvariantCulture)
                };

                static SmartMeterIndex GetSimpleIndex(ReadOnlySpan<string> values) => new()
                {
                    BaseIndex    = ulong.Parse(values[1], CultureInfo.InvariantCulture),
                    OffPeakIndex = default
                };
            }
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
            public static SmartMeterInformation CreateFromUnitDescription(ReadOnlySpan<string> values)
            {
                if (values is not [{ Length: > 0 }])
                {
                    throw new InvalidOperationException(SR.GetResourceString(SR.ID0068));
                }

                return new()
                {
                    IsPowerCutActive = values[0] is "33" or "49",
                    RateType         = values[0] switch
                    {
                        "32" or "33" => SmartMeterRateType.OffPeak,
                        "48" or "49" => SmartMeterRateType.Peak,

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                    }
                };
            }
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
            /// Peak/off-peak.
            /// </summary>
            PeakOffPeak = 1,

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
    /// Management models (WHO = 13).
    /// </summary>
    public static class Management
    {
        /// <summary>
        /// Zigbee network event type.
        /// </summary>
        public enum ZigbeeNetworkEventType
        {
            /// <summary>
            /// Closed.
            /// </summary>
            Closed = 0,

            /// <summary>
            /// Opened.
            /// </summary>
            Opened = 1,

            /// <summary>
            /// Created.
            /// </summary>
            Created = 2,

            /// <summary>
            /// Joined.
            /// </summary>
            Joined = 3,

            /// <summary>
            /// Left.
            /// </summary>
            Left = 4
        }
    }

    /// <summary>
    /// Scenarios models (WHO = 15).
    /// </summary>
    public static class Scenarios
    {
        /// <summary>
        /// Pressure scenario type.
        /// </summary>
        public enum PressureScenarioType
        {
            /// <summary>
            /// Pressure.
            /// </summary>
            Pressure = 0,

            /// <summary>
            /// Release after short pressure.
            /// </summary>
            ReleaseAfterShortPressure = 1,

            /// <summary>
            /// Release after extended pressure.
            /// </summary>
            ReleaseAfterExtendedPressure = 2,

            /// <summary>
            /// Extended pressure.
            /// </summary>
            ExtendedPressure = 3
        }
    }

    /// <summary>
    /// Scenarios plus models (WHO = 25).
    /// </summary>
    public static class ScenariosPlus
    {
        /// <summary>
        /// Action scenario type.
        /// </summary>
        public enum ActionScenarioType
        {
            /// <summary>
            /// Action.
            /// </summary>
            Action = 0,

            /// <summary>
            /// Stop action.
            /// </summary>
            StopAction = 1
        }

        /// <summary>
        /// Pressure scenario type.
        /// </summary>
        public enum PressureScenarioType
        {
            /// <summary>
            /// Short pressure.
            /// </summary>
            ShortPressure = 0,

            /// <summary>
            /// Start of extended pressure.
            /// </summary>
            StartOfExtendedPressure = 1,

            /// <summary>
            /// Extended pressure.
            /// </summary>
            ExtendedPressure = 2,

            /// <summary>
            /// End of extended pressure.
            /// </summary>
            EndOfExtendedPressure = 3
        }

        /// <summary>
        /// Zigbee binding event type.
        /// </summary>
        public enum ZigbeeBindingEventType
        {
            /// <summary>
            /// Closed.
            /// </summary>
            Closed = 0,

            /// <summary>
            /// Opened.
            /// </summary>
            Opened = 1,

            /// <summary>
            /// Canceled.
            /// </summary>
            Canceled = 2
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
            public required byte FunctionCode { get; init; }

            /// <summary>
            /// Gets or sets the device model.
            /// </summary>
            public required string Model { get; init; }

            /// <summary>
            /// Gets or sets the number of units available.
            /// </summary>
            public required byte Units { get; init; }

            /// <summary>
            /// Gets or sets the device version.
            /// </summary>
            public required Version Version { get; init; }

            /// <summary>
            /// Creates a new instance of the <see cref="DeviceDescription"/> class using the specified unit description.
            /// </summary>
            /// <param name="values">The unit description values.</param>
            /// <returns>A new instance of the <see cref="DeviceDescription"/> class.</returns>
            public static DeviceDescription CreateFromDeviceDescription(ReadOnlySpan<string> values)
            {
                if (values is not [{ Length: > 0 }, { Length: > 0 }, { Length: > 0 }, { Length: > 0 }])
                {
                    throw new InvalidOperationException(SR.GetResourceString(SR.ID0068));
                }

                return new()
                {
                    FunctionCode = byte.Parse(values[2], CultureInfo.InvariantCulture),
                    Model        = uint.Parse(values[0], CultureInfo.InvariantCulture).ToString("X"),
                    Units        = byte.Parse(values[3], CultureInfo.InvariantCulture),
                    Version      = new Version(int.Parse(uint.Parse(values[1], CultureInfo.InvariantCulture).ToString("X")), 0)
                };
            }
        }

        /// <summary>
        /// Availability.
        /// </summary>
        public enum Availability
        {
            /// <summary>
            /// Offline.
            /// </summary>
            Offline = 0,

            /// <summary>
            /// Online.
            /// </summary>
            Online = 1
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
            public required byte FunctionCode { get; init; }

            /// <summary>
            /// Gets or sets the medium.
            /// </summary>
            public required OpenNettyMedium Medium { get; init; }

            /// <summary>
            /// Creates a new instance of the <see cref="MemoryData"/> class using the specified unit description.
            /// </summary>
            /// <param name="values">The unit description values.</param>
            /// <returns>A new instance of the <see cref="MemoryData"/> class.</returns>
            public static MemoryData CreateFromUnitDescription(ReadOnlySpan<string> values)
            {
                // Note: while the frame number is part of the returned model, it MUST be present in the dimension values.
                if (values is not [{ Length: > 0 }, { Length: > 0 }, { Length: > 0 }, { Length: > 0 }])
                {
                    throw new InvalidOperationException(SR.GetResourceString(SR.ID0068));
                }

                return new()
                {
                    Address      = OpenNettyAddress.FromNitooAddress(
                        identifier: uint.Parse(values[1], CultureInfo.InvariantCulture) / 16,
                        unit      : (byte) (uint.Parse(values[1], CultureInfo.InvariantCulture) % 16)),
                    FunctionCode = byte.Parse(values[2], CultureInfo.InvariantCulture),
                    Medium       = values[0] switch
                    {
                        "64"  => OpenNettyMedium.Radio,
                        "96"  => OpenNettyMedium.Powerline,
                        "128" => OpenNettyMedium.Infrared,

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                    }
                };
            }
        }

        /// <summary>
        /// Unit description.
        /// </summary>
        public sealed record class UnitDescription
        {
            /// <summary>
            /// Gets or sets the function code.
            /// </summary>
            public required byte FunctionCode { get; init; }

            /// <summary>
            /// Gets or sets the values.
            /// </summary>
            public required ImmutableArray<string> Values { get; init; } = [];

            /// <summary>
            /// Creates a new instance of the <see cref="UnitDescription"/> class using the specified unit description.
            /// </summary>
            /// <param name="values">The unit description values.</param>
            /// <returns>A new instance of the <see cref="UnitDescription"/> class.</returns>
            public static UnitDescription CreateFromUnitDescription(ReadOnlySpan<string> values)
            {
                if (values is not [{ Length: > 0 }, { Length: > 0 }, ..])
                {
                    throw new InvalidOperationException(SR.GetResourceString(SR.ID0068));
                }

                return new()
                {
                    FunctionCode = byte.Parse(values[0], CultureInfo.InvariantCulture),
                    Values       = [.. values[1..]]
                };
            }
        }
    }
}
