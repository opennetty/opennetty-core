/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Reflection;
using Xunit;

namespace OpenNetty.Mqtt.Tests;

public sealed class OpenNettyMqttAttributesTests
{
    [Theory]
    [InlineData(OpenNettyMqttAttributes.ActuatorLock, "actuator_lock")]
    [InlineData(OpenNettyMqttAttributes.Availability, "availability")]
    [InlineData(OpenNettyMqttAttributes.BatteryAlert, "battery_alert")]
    [InlineData(OpenNettyMqttAttributes.BatteryLevel, "battery_level")]
    [InlineData(OpenNettyMqttAttributes.Brightness, "brightness")]
    [InlineData(OpenNettyMqttAttributes.DryContactState, "dry_contact_state")]
    [InlineData(OpenNettyMqttAttributes.FirmwareVersion, "firmware_version")]
    [InlineData(OpenNettyMqttAttributes.HardwareVersion, "hardware_version")]
    [InlineData(OpenNettyMqttAttributes.IncomingMessage, "incoming_message")]
    [InlineData(OpenNettyMqttAttributes.LastCommunicationDate, "last_communication_date")]
    [InlineData(OpenNettyMqttAttributes.MacAddress, "mac_address")]
    [InlineData(OpenNettyMqttAttributes.MemoryData, "memory_data")]
    [InlineData(OpenNettyMqttAttributes.OutgoingMessage, "outgoing_message")]
    [InlineData(OpenNettyMqttAttributes.PilotWireDerogationMode, "pilot_wire_derogation_mode")]
    [InlineData(OpenNettyMqttAttributes.PilotWireSetpointMode, "pilot_wire_setpoint_mode")]
    [InlineData(OpenNettyMqttAttributes.PilotWireShutdownMode, "pilot_wire_shutdown_mode")]
    [InlineData(OpenNettyMqttAttributes.Scenario, "scenario")]
    [InlineData(OpenNettyMqttAttributes.ShutterPosition, "shutter_position")]
    [InlineData(OpenNettyMqttAttributes.ShutterState, "shutter_state")]
    [InlineData(OpenNettyMqttAttributes.SmartMeterBaseIndex, "smart_meter_base_index")]
    [InlineData(OpenNettyMqttAttributes.SmartMeterBlueIndex, "smart_meter_blue_index")]
    [InlineData(OpenNettyMqttAttributes.SmartMeterPeakOffPeakIndex, "smart_meter_peak_off_peak_index")]
    [InlineData(OpenNettyMqttAttributes.SmartMeterRedIndex, "smart_meter_red_index")]
    [InlineData(OpenNettyMqttAttributes.SmartMeterWhiteIndex, "smart_meter_white_index")]
    [InlineData(OpenNettyMqttAttributes.SmartMeterPowerCutMode, "smart_meter_power_cut_mode")]
    [InlineData(OpenNettyMqttAttributes.SmartMeterRateType, "smart_meter_rate_type")]
    [InlineData(OpenNettyMqttAttributes.SmartMeterSubscriptionType, "smart_meter_subscription_type")]
    [InlineData(OpenNettyMqttAttributes.StartupDate, "startup_date")]
    [InlineData(OpenNettyMqttAttributes.SwitchState, "switch_state")]
    [InlineData(OpenNettyMqttAttributes.WaterHeaterState, "water_heater_state")]
    [InlineData(OpenNettyMqttAttributes.WaterHeaterSetpointMode, "water_heater_setpoint_mode")]
    [InlineData(OpenNettyMqttAttributes.WirelessBurglarAlarmState, "wireless_burglar_alarm_state")]
    [InlineData(OpenNettyMqttAttributes.ZigbeeBinding, "zigbee_binding")]
    [InlineData(OpenNettyMqttAttributes.ZigbeeChannel, "zigbee_channel")]
    [InlineData(OpenNettyMqttAttributes.ZigbeeDevicesCount, "zigbee_devices_count")]
    [InlineData(OpenNettyMqttAttributes.ZigbeeNetwork, "zigbee_network")]
    [InlineData(OpenNettyMqttAttributes.ZigbeeSupervision, "zigbee_supervision")]
    public void Constant_HasTheExpectedValue(string actual, string expected)
    {
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Constants_AreAllCoveredByTheExpectedValues()
    {
        Assert.Equal(37, GetValues().Count);
    }

    [Fact]
    public void Constants_HaveUniqueValues()
    {
        var values = GetValues();

        Assert.Equal(values.Count, values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Constants_AreLowerCaseSnakeCaseIdentifiers()
    {
        Assert.All(GetValues(), static value => Assert.Matches("^[a-z]+(_[a-z]+)*$", value));
    }

    private static List<string> GetValues() => [.. typeof(OpenNettyMqttAttributes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(static field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(static field => (string) field.GetRawConstantValue()!)];
}
