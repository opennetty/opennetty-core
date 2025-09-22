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
    /// Action scenario activation.
    /// </summary>
    public static readonly OpenNettyCapability ActionScenarioActivation = new("Action scenario activation");

    /// <summary>
    /// Action scenario event.
    /// </summary>
    public static readonly OpenNettyCapability ActionScenarioEvent = new("Action scenario event");

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
    /// Configurable push button numbers.
    /// </summary>
    public static readonly OpenNettyCapability ConfigurablePushButtonNumbers = new("Configurable push button numbers");

    /// <summary>
    /// Date/time.
    /// </summary>
    public static readonly OpenNettyCapability DateTime = new("Date/time");

    /// <summary>
    /// Device description.
    /// </summary>
    public static readonly OpenNettyCapability DeviceDescription = new("Device description");

    /// <summary>
    /// Dimming scenario activation.
    /// </summary>
    public static readonly OpenNettyCapability DimmingScenarioActivation = new("Dimming scenario activation");

    /// <summary>
    /// Dimming scenario event.
    /// </summary>
    public static readonly OpenNettyCapability DimmingScenarioEvent = new("Dimming scenario event");

    /// <summary>
    /// Pressure scenario activation.
    /// </summary>
    public static readonly OpenNettyCapability PressureScenarioActivation = new("Pressure scenario activation");

    /// <summary>
    /// Pressure scenario event.
    /// </summary>
    public static readonly OpenNettyCapability PressureScenarioEvent = new("Pressure scenario event");

    /// <summary>
    /// Pressure scenario plus activation.
    /// </summary>
    public static readonly OpenNettyCapability PressureScenarioPlusActivation = new("Pressure scenario plus activation");

    /// <summary>
    /// Pressure scenario plus event.
    /// </summary>
    public static readonly OpenNettyCapability PressureScenarioPlusEvent = new("Pressure scenario plus event");

    /// <summary>
    /// Firmware version.
    /// </summary>
    public static readonly OpenNettyCapability FirmwareVersion = new("Firmware version");

    /// <summary>
    /// Firmware version.
    /// </summary>
    public static readonly OpenNettyCapability HardwareVersion = new("Hardware version");

    /// <summary>
    /// MAC address.
    /// </summary>
    public static readonly OpenNettyCapability MacAddress = new("MAC address");

    /// <summary>
    /// Memory reading.
    /// </summary>
    public static readonly OpenNettyCapability MemoryReading = new("Memory reading");

    /// <summary>
    /// Memory writing.
    /// </summary>
    public static readonly OpenNettyCapability MemoryWriting = new("Memory writing");

    /// <summary>
    /// On/off scenario activation.
    /// </summary>
    public static readonly OpenNettyCapability OnOffScenarioActivation = new("On/off scenario activation");

    /// <summary>
    /// On/off scenario event.
    /// </summary>
    public static readonly OpenNettyCapability OnOffScenarioEvent = new("On/off scenario event");

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
    /// Progressive scenario activation.
    /// </summary>
    public static readonly OpenNettyCapability ProgressiveScenarioActivation = new("Progressive scenario activation");

    /// <summary>
    /// Progressive scenario event.
    /// </summary>
    public static readonly OpenNettyCapability ProgressiveScenarioEvent = new("Progressive scenario event");

    /// <summary>
    /// Smart meter indexes.
    /// </summary>
    public static readonly OpenNettyCapability SmartMeterIndexes = new("Smart meter indexes");

    /// <summary>
    /// Smart meter information.
    /// </summary>
    public static readonly OpenNettyCapability SmartMeterInformation = new("Smart meter information");

    /// <summary>
    /// Action scenario activation.
    /// </summary>
    public static readonly OpenNettyCapability StopActionScenarioActivation = new("Stop action scenario activation");

    /// <summary>
    /// Action scenario event.
    /// </summary>
    public static readonly OpenNettyCapability StopActionScenarioEvent = new("Stop action scenario event");

    /// <summary>
    /// Stop/up/down scenario activation.
    /// </summary>
    public static readonly OpenNettyCapability StopUpDownScenarioActivation = new("Stop/up/down scenario activation");

    /// <summary>
    /// Stop/up/down scenario event.
    /// </summary>
    public static readonly OpenNettyCapability StopUpDownScenarioEvent = new("Stop/up/down scenario event");

    /// <summary>
    /// Timed scenario activation.
    /// </summary>
    public static readonly OpenNettyCapability TimedScenarioActivation = new("Timed scenario activation");

    /// <summary>
    /// Timed scenario event.
    /// </summary>
    public static readonly OpenNettyCapability TimedScenarioEvent = new("Timed scenario event");

    /// <summary>
    /// Toggle scenario event.
    /// </summary>
    public static readonly OpenNettyCapability ToggleScenarioEvent = new("Toggle scenario event");

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
    /// Zigbee end device (ZED).
    /// </summary>
    public static readonly OpenNettyCapability ZigbeeEndDevice = new("Zigbee end device");

    /// <summary>
    /// Zigbee network management.
    /// </summary>
    public static readonly OpenNettyCapability ZigbeeNetworkManagement = new("Zigbee network management");

    /// <summary>
    /// Zigbee router (ZR).
    /// </summary>
    public static readonly OpenNettyCapability ZigbeeRouter = new("Zigbee router");

    /// <summary>
    /// Zigbee supervision.
    /// </summary>
    public static readonly OpenNettyCapability ZigbeeSupervision = new("Zigbee supervision");
}
