/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Exposes common settings supported by OpenNetty.
/// </summary>
public static class OpenNettySettings
{
    /// <summary>
    /// Action validation (Nitoo only).
    /// </summary>
    public static readonly OpenNettySetting ActionValidation = new("Action validation");

    /// <summary>
    /// Clock synchronization.
    /// </summary>
    public static readonly OpenNettySetting ClockSynchronization = new("Clock synchronization");

    /// <summary>
    /// Function type (SCS only).
    /// </summary>
    public static readonly OpenNettySetting FunctionType = new("Function type");

    /// <summary>
    /// Home Assistant button icon.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantButtonIcon = new("Home Assistant button icon");

    /// <summary>
    /// Home Assistant button name.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantButtonName = new("Home Assistant button name");

    /// <summary>
    /// Home Assistant cover device class.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantCoverDeviceClass = new("Home Assistant cover device class");

    /// <summary>
    /// Home Assistant cover icon.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantCoverIcon = new("Home Assistant cover icon");

    /// <summary>
    /// Home Assistant cover name.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantCoverName = new("Home Assistant cover name");

    /// <summary>
    /// Home Assistant discovery.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantDiscovery = new("Home Assistant discovery");

    /// <summary>
    /// Home Assistant discovery UI culture.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantDiscoveryUICulture = new("Home Assistant discovery UI culture");

    /// <summary>
    /// Home Assistant entity type.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantEntityType = new("Home Assistant entity type");

    /// <summary>
    /// Home Assistant light icon.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantLightIcon = new("Home Assistant light icon");

    /// <summary>
    /// Home Assistant light on command type.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantLightOnCommandType = new("Home Assistant light on command type");

    /// <summary>
    /// Home Assistant light name.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantLightName = new("Home Assistant light name");

    /// <summary>
    /// Home Assistant scenario device class.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantScenarioDeviceClass = new("Home Assistant scenario device class");

    /// <summary>
    /// Home Assistant scenario icon.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantScenarioIcon = new("Home Assistant scenario icon");

    /// <summary>
    /// Home Assistant suggested area.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantSuggestedArea = new("Home Assistant suggested area");

    /// <summary>
    /// Home Assistant switch device class.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantSwitchDeviceClass = new("Home Assistant switch device class");

    /// <summary>
    /// Home Assistant switch icon.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantSwitchIcon = new("Home Assistant switch icon");

    /// <summary>
    /// Home Assistant switch name.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantSwitchName = new("Home Assistant switch name");

    /// <summary>
    /// MQTT topic.
    /// </summary>
    public static readonly OpenNettySetting MqttTopic = new("MQTT topic");

    /// <summary>
    /// Push button numbers.
    /// </summary>
    public static readonly OpenNettySetting PushButtonNumbers = new("Push button numbers");

    /// <summary>
    /// Serial port baud rate.
    /// </summary>
    public static readonly OpenNettySetting SerialPortBaudRate = new("Serial port baud rate");

    /// <summary>
    /// Serial port data bits.
    /// </summary>
    public static readonly OpenNettySetting SerialPortDataBits = new("Serial port data bits");

    /// <summary>
    /// Serial port parity.
    /// </summary>
    public static readonly OpenNettySetting SerialPortParity = new("Serial port parity");

    /// <summary>
    /// Serial port stop bits.
    /// </summary>
    public static readonly OpenNettySetting SerialPortStopBits = new("Serial port stop bits");

    /// <summary>
    /// Smart meter base index offset.
    /// </summary>
    public static readonly OpenNettySetting SmartMeterBaseIndexOffset = new("Smart meter base index offset");

    /// <summary>
    /// Smart meter blue index offset [base].
    /// </summary>
    public static readonly OpenNettySetting SmartMeterBlueIndexOffsetBase = new("Smart meter blue index offset [base]");

    /// <summary>
    /// Smart meter blue index offset [off-peak].
    /// </summary>
    public static readonly OpenNettySetting SmartMeterBlueIndexOffsetOffPeak = new("Smart meter blue index offset [off-peak]");

    /// <summary>
    /// Smart meter peak/off-peak index offset [base].
    /// </summary>
    public static readonly OpenNettySetting SmartMeterPeakOffPeakIndexOffsetBase = new("Smart meter peak/off-peak index offset [base]");

    /// <summary>
    /// Smart meter peak/off-peak index offset [off-peak].
    /// </summary>
    public static readonly OpenNettySetting SmartMeterPeakOffPeakIndexOffsetOffPeak = new("Smart meter peak/off-peak index offset [off-peak]");

    /// <summary>
    /// Smart meter red index offset [base].
    /// </summary>
    public static readonly OpenNettySetting SmartMeterRedIndexOffsetBase = new("Smart meter red index offset [base]");

    /// <summary>
    /// Smart meter red index offset [off-peak].
    /// </summary>
    public static readonly OpenNettySetting SmartMeterRedIndexOffsetOffPeak = new("Smart meter red index offset [off-peak]");

    /// <summary>
    /// Smart meter white index offset [base].
    /// </summary>
    public static readonly OpenNettySetting SmartMeterWhiteIndexOffsetBase = new("Smart meter white index offset [base]");

    /// <summary>
    /// Smart meter white index offset [off-peak].
    /// </summary>
    public static readonly OpenNettySetting SmartMeterWhiteIndexOffsetOffPeak = new("Smart meter white index offset [off-peak]");

    /// <summary>
    /// Switch mode.
    /// </summary>
    public static readonly OpenNettySetting SwitchMode = new("Switch mode");

    /// <summary>
    /// Exposes common function types.
    /// </summary>
    public static class FunctionTypes
    {
        /// <summary>
        /// Automation actuator.
        /// </summary>
        public const string AutomationActuator = "Automation actuator";

        /// <summary>
        /// Light actuator.
        /// </summary>
        public const string LightActuator = "Light actuator";

        /// <summary>
        /// Scheduled scenario.
        /// </summary>
        public const string ScheduledScenario = "Scheduled scenario";

        /// <summary>
        /// Scheduled scenario plus.
        /// </summary>
        public const string ScheduledScenarioPlus = "Scheduled scenario plus";
    }

    /// <summary>
    /// Exposes common Home Assistant device classes.
    /// </summary>
    public static class HomeAssistantDeviceClasses
    {
        /// <summary>
        /// Shutter.
        /// </summary>
        public const string Shutter = "shutter";

        /// <summary>
        /// Switch.
        /// </summary>
        public const string Switch = "switch";
    }

    /// <summary>
    /// Exposes common Home Assistant entity types.
    /// </summary>
    public static class HomeAssistantEntityTypes
    {
        /// <summary>
        /// Light.
        /// </summary>
        public const string Light = "light";

        /// <summary>
        /// Switch.
        /// </summary>
        public const string Switch = "switch";
    }

    /// <summary>
    /// Exposes common switch modes.
    /// </summary>
    public static class SwitchModes
    {
        /// <summary>
        /// Default.
        /// </summary>
        public const string Default = "Default";

        /// <summary>
        /// Push button.
        /// </summary>
        public const string PushButton = "Push button";
    }
}
