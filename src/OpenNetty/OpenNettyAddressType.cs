namespace OpenNetty;

/// <summary>
/// Exposes common OpenNetty address types, as defined by the Nitoo and MyHome specifications.
/// </summary>
public enum OpenNettyAddressType
{
    /// <summary>
    /// Unknown address.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Nitoo device address.
    /// </summary>
    NitooDevice = 1,

    /// <summary>
    /// Nitoo unit address.
    /// </summary>
    NitooUnit = 2,

    /// <summary>
    /// Zigbee "all devices, all units" address.
    /// </summary>
    ZigbeeAllDevicesAllUnits = 3,

    /// <summary>
    /// Zigbee "all devices, specific unit" address.
    /// </summary>
    ZigbeeAllDevicesSpecificUnit = 4,

    /// <summary>
    /// Zigbee "specific device, all units" address.
    /// </summary>
    ZigbeeSpecificDeviceAllUnits = 5,

    /// <summary>
    /// Zigbee "specific device, specific unit" address.
    /// </summary>
    ZigbeeSpecificDeviceSpecificUnit = 6,

    /// <summary>
    /// SCS light point point-to-point address.
    /// </summary>
    ScsLightPointPointToPoint = 7,

    /// <summary>
    /// SCS light point group address.
    /// </summary>
    ScsLightPointGroup = 8,

    /// <summary>
    /// SCS light point area address.
    /// </summary>
    ScsLightPointArea = 9,

    /// <summary>
    /// SCS light point general address.
    /// </summary>
    ScsLightPointGeneral = 10
}
