/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Exposes common capabilities supported by OpenNetty endpoints or devices.
/// </summary>
public static class OpenNettyCapabilities
{
    /// <summary>
    /// Action scenario control.
    /// </summary>
    public static readonly OpenNettyCapability ActionScenarioControl = new("Action scenario control");

    /// <summary>
    /// Action scenario state.
    /// </summary>
    public static readonly OpenNettyCapability ActionScenarioState = new("Action scenario state");

    /// <summary>
    /// Advanced dimming control.
    /// </summary>
    public static readonly OpenNettyCapability AdvancedDimmingControl = new("Advanced dimming control");

    /// <summary>
    /// Advanced dimming state.
    /// </summary>
    public static readonly OpenNettyCapability AdvancedDimmingState = new("Advanced dimming state");

    /// <summary>
    /// Advanced shutter control.
    /// </summary>
    public static readonly OpenNettyCapability AdvancedShutterControl = new("Advanced shutter control");

    /// <summary>
    /// Advanced shutter state.
    /// </summary>
    public static readonly OpenNettyCapability AdvancedShutterState = new("Advanced shutter state");

    /// <summary>
    /// Basic dimming control.
    /// </summary>
    public static readonly OpenNettyCapability BasicDimmingControl = new("Basic dimming control");

    /// <summary>
    /// Basic dimming state.
    /// </summary>
    public static readonly OpenNettyCapability BasicDimmingState = new("Basic dimming state");

    /// <summary>
    /// Basic shutter control.
    /// </summary>
    public static readonly OpenNettyCapability BasicShutterControl = new("Basic shutter control");

    /// <summary>
    /// Basic shutter state.
    /// </summary>
    public static readonly OpenNettyCapability BasicShutterState = new("Basic shutter state");

    /// <summary>
    /// Battery alert.
    /// </summary>
    public static readonly OpenNettyCapability BatteryAlert = new("Battery alert");

    /// <summary>
    /// Battery level.
    /// </summary>
    public static readonly OpenNettyCapability BatteryLevel = new("Battery level");

    /// <summary>
    /// Date/time.
    /// </summary>
    public static readonly OpenNettyCapability DateTime = new("Date/time");

    /// <summary>
    /// Device description.
    /// </summary>
    public static readonly OpenNettyCapability DeviceDescription = new("Device description");

    /// <summary>
    /// Dimming scenario control.
    /// </summary>
    public static readonly OpenNettyCapability DimmingScenarioControl = new("Dimming scenario control");

    /// <summary>
    /// Dimming scenario state.
    /// </summary>
    public static readonly OpenNettyCapability DimmingScenarioState = new("Dimming scenario state");

    /// <summary>
    /// Firmware version.
    /// </summary>
    public static readonly OpenNettyCapability FirmwareVersion = new("Firmware version");

    /// <summary>
    /// Firmware version.
    /// </summary>
    public static readonly OpenNettyCapability HardwareVersion = new("Hardware version");

    /// <summary>
    /// Memory reading.
    /// </summary>
    public static readonly OpenNettyCapability MemoryReading = new("Memory reading");

    /// <summary>
    /// Memory writing.
    /// </summary>
    public static readonly OpenNettyCapability MemoryWriting = new("Memory writing");

    /// <summary>
    /// On/off scenario control.
    /// </summary>
    public static readonly OpenNettyCapability OnOffScenarioControl = new("On/off scenario control");

    /// <summary>
    /// On/off scenario state.
    /// </summary>
    public static readonly OpenNettyCapability OnOffScenarioState = new("On/off scenario state");

    /// <summary>
    /// On/off switch control.
    /// </summary>
    public static readonly OpenNettyCapability OnOffSwitchControl = new("On/off switch control");

    /// <summary>
    /// On/off switch state.
    /// </summary>
    public static readonly OpenNettyCapability OnOffSwitchState = new("On/off switch state");

    /// <summary>
    /// OpenWebNet gateway.
    /// </summary>
    public static readonly OpenNettyCapability OpenWebNetGateway = new("OpenWebNet gateway");

    /// <summary>
    /// OpenWebNet command session.
    /// </summary>
    public static readonly OpenNettyCapability OpenWebNetCommandSession = new("OpenWebNet command session");

    /// <summary>
    /// OpenWebNet event session.
    /// </summary>
    public static readonly OpenNettyCapability OpenWebNetEventSession = new("OpenWebNet event session");

    /// <summary>
    /// OpenWebNet generic session.
    /// </summary>
    public static readonly OpenNettyCapability OpenWebNetGenericSession = new("OpenWebNet generic session");

    /// <summary>
    /// Pilot wire heating.
    /// </summary>
    public static readonly OpenNettyCapability PilotWireHeating = new("Pilot wire heating");

    /// <summary>
    /// Progressive scenario control.
    /// </summary>
    public static readonly OpenNettyCapability ProgressiveScenarioControl = new("Progressive scenario control");

    /// <summary>
    /// Progressive scenario state.
    /// </summary>
    public static readonly OpenNettyCapability ProgressiveScenarioState = new("Progressive scenario state");

    /// <summary>
    /// Short pressure scenario state.
    /// </summary>
    public static readonly OpenNettyCapability ShortPressureScenarioState = new("Short pressure scenario state");

    /// <summary>
    /// Smart meter indexes.
    /// </summary>
    public static readonly OpenNettyCapability SmartMeterIndexes = new("Smart meter indexes");

    /// <summary>
    /// Smart meter information.
    /// </summary>
    public static readonly OpenNettyCapability SmartMeterInformation = new("Smart meter information");

    /// <summary>
    /// Action scenario control.
    /// </summary>
    public static readonly OpenNettyCapability StopActionScenarioControl = new("Stop action scenario control");

    /// <summary>
    /// Action scenario state.
    /// </summary>
    public static readonly OpenNettyCapability StopActionScenarioState = new("Stop action scenario state");

    /// <summary>
    /// Stop/up/down scenario control.
    /// </summary>
    public static readonly OpenNettyCapability StopUpDownScenarioControl = new("Stop/up/down scenario control");

    /// <summary>
    /// Stop/up/down scenario state.
    /// </summary>
    public static readonly OpenNettyCapability StopUpDownScenarioState = new("Stop/up/down scenario state");

    /// <summary>
    /// Timed scenario control.
    /// </summary>
    public static readonly OpenNettyCapability TimedScenarioControl = new("Timed scenario control");

    /// <summary>
    /// Timed scenario state.
    /// </summary>
    public static readonly OpenNettyCapability TimedScenarioState = new("Timed scenario state");

    /// <summary>
    /// Toggle scenario state.
    /// </summary>
    public static readonly OpenNettyCapability ToggleScenarioState = new("Toggle scenario state");

    /// <summary>
    /// Unit description.
    /// </summary>
    public static readonly OpenNettyCapability UnitDescription = new("Unit description");

    /// <summary>
    /// Uptime.
    /// </summary>
    public static readonly OpenNettyCapability Uptime = new("Uptime");

    /// <summary>
    /// Water heating.
    /// </summary>
    public static readonly OpenNettyCapability WaterHeating = new("Water heating");

    /// <summary>
    /// Wireless burglar alarm state.
    /// </summary>
    public static readonly OpenNettyCapability WirelessBurglarAlarmState = new("Wireless burglar alarm state");

    /// <summary>
    /// Zigbee binding.
    /// </summary>
    public static readonly OpenNettyCapability ZigbeeBinding = new("Zigbee binding");

    /// <summary>
    /// Zigbee network management.
    /// </summary>
    public static readonly OpenNettyCapability ZigbeeNetworkManagement = new("Zigbee network management");

    /// <summary>
    /// Zigbee supervision.
    /// </summary>
    public static readonly OpenNettyCapability ZigbeeSupervision = new("Zigbee supervision");
}
