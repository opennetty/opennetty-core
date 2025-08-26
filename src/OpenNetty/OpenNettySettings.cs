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
    /// Actuator type (SCS only).
    /// </summary>
    public static readonly OpenNettySetting ActuatorType = new("Actuator type");

    /// <summary>
    /// Home Assistant device class.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantDeviceClass = new("Home Assistant device class");

    /// <summary>
    /// Home Assistant entity type.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantEntityType = new("Home Assistant entity type");

    /// <summary>
    /// Home Assistant suggested area.
    /// </summary>
    public static readonly OpenNettySetting HomeAssistantSuggestedArea = new("Home Assistant suggested area");

    /// <summary>
    /// MQTT discovery.
    /// </summary>
    public static readonly OpenNettySetting MqttDiscovery = new("MQTT discovery");

    /// <summary>
    /// MQTT endpoint name.
    /// </summary>
    public static readonly OpenNettySetting MqttEndpointName = new("MQTT endpoint name");

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
    /// Switch mode.
    /// </summary>
    public static readonly OpenNettySetting SwitchMode = new("Switch mode");

    /// <summary>
    /// Exposes common actuator types.
    /// </summary>
    public static class ActuatorTypes
    {
        /// <summary>
        /// Automation actuator.
        /// </summary>
        public const string Automation = "Automation";

        /// <summary>
        /// Lighting actuator.
        /// </summary>
        public const string Lighting = "Lighting";
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
