namespace OpenNetty;

/// <summary>
/// Exposes common OpenNetty dimensions, as defined by the Nitoo and MyHome specifications.
/// </summary>
public static class OpenNettyDimensions
{
    /// <summary>
    /// Lighting dimensions (WHO = 1).
    /// </summary>
    public static class Lighting
    {
        /// <summary>
        /// Dimmer level/speed (DIMENSION = 1).
        /// </summary>
        public static readonly OpenNettyDimension DimmerLevelSpeed = new(OpenNettyCategories.Lighting, "1");

        /// <summary>
        /// Dimmer status (DIMENSION = 4).
        /// </summary>
        public static readonly OpenNettyDimension DimmerStatus = new(OpenNettyCategories.Lighting, "4");

        /// <summary>
        /// Dimmer step (DIMENSION = 10).
        /// </summary>
        public static readonly OpenNettyDimension DimmerStep = new(OpenNettyCategories.Lighting, "10");
    }

    /// <summary>
    /// Temperature control dimensions (WHO = 4).
    /// </summary>
    public static class TemperatureControl
    {
        /// <summary>
        /// Water heating mode (DIMENSION = 40).
        /// </summary>
        public static readonly OpenNettyDimension WaterHeatingMode = new(OpenNettyCategories.TemperatureControl, "40");

        /// <summary>
        /// Smart meter rate type (DIMENSION = 42).
        /// </summary>
        public static readonly OpenNettyDimension SmartMeterRateType = new(OpenNettyCategories.TemperatureControl, "42");

        /// <summary>
        /// Smart meter indexes (DIMENSION = 43).
        /// </summary>
        public static readonly OpenNettyDimension SmartMeterIndexes = new(OpenNettyCategories.TemperatureControl, "43");
    }

    /// <summary>
    /// Management dimensions (WHO = 13).
    /// </summary>
    public static class Management
    {
        /// <summary>
        /// Time (DIMENSION = 0).
        /// </summary>
        public static readonly OpenNettyDimension Time = new(OpenNettyCategories.Management, "0");

        /// <summary>
        /// Date (DIMENSION = 1).
        /// </summary>
        public static readonly OpenNettyDimension Date = new(OpenNettyCategories.Management, "1");

        /// <summary>
        /// IP address (DIMENSION = 10).
        /// </summary>
        public static readonly OpenNettyDimension IpAddress = new(OpenNettyCategories.Management, "10");

        /// <summary>
        /// Netmask (DIMENSION = 11).
        /// </summary>
        public static readonly OpenNettyDimension Netmask = new(OpenNettyCategories.Management, "11");

        /// <summary>
        /// Firmware version (DIMENSION = 16).
        /// </summary>
        public static readonly OpenNettyDimension FirmwareVersion = new(OpenNettyCategories.Management, "16");

        /// <summary>
        /// Hardware version (DIMENSION = 17).
        /// </summary>
        public static readonly OpenNettyDimension HardwareVersion = new(OpenNettyCategories.Management, "17");

        /// <summary>
        /// Uptime (DIMENSION = 19).
        /// </summary>
        public static readonly OpenNettyDimension Uptime = new(OpenNettyCategories.Management, "19");

        /// <summary>
        /// Date/time (DIMENSION = 22).
        /// </summary>
        public static readonly OpenNettyDimension DateTime = new(OpenNettyCategories.Management, "22");

        /// <summary>
        /// Device identifier (DIMENSION = 27).
        /// </summary>
        public static readonly OpenNettyDimension DeviceIdentifier = new(OpenNettyCategories.Management, "27");

        /// <summary>
        /// Battery information (DIMENSION = 72).
        /// </summary>
        public static readonly OpenNettyDimension BatteryInformation = new(OpenNettyCategories.Management, "72");
    }

    /// <summary>
    /// Diagnostics dimensions (WHO = 1000).
    /// </summary>
    public static class Diagnostics
    {
        /// <summary>
        /// Device description (DIMENSION = 51).
        /// </summary>
        public static readonly OpenNettyDimension DeviceDescription = new(OpenNettyCategories.Diagnostics, "51");

        /// <summary>
        /// Memory data (DIMENSION = 52).
        /// </summary>
        public static readonly OpenNettyDimension MemoryData = new(OpenNettyCategories.Diagnostics, "52");

        /// <summary>
        /// Extended memory data (DIMENSION = 53).
        /// </summary>
        public static readonly OpenNettyDimension ExtendedMemoryData = new(OpenNettyCategories.Diagnostics, "53");

        /// <summary>
        /// Memory write (DIMENSION = 54).
        /// </summary>
        public static readonly OpenNettyDimension MemoryWrite = new(OpenNettyCategories.Diagnostics, "54");

        /// <summary>
        /// Unit description (DIMENSION = 55).
        /// </summary>
        public static readonly OpenNettyDimension UnitDescription = new(OpenNettyCategories.Diagnostics, "55");

        /// <summary>
        /// Memory depth (DIMENSION = 56).
        /// </summary>
        public static readonly OpenNettyDimension MemoryDepth = new(OpenNettyCategories.Diagnostics, "56");
    }
}
