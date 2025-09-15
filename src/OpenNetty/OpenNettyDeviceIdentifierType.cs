/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Exposes common OpenNetty identifier types, as defined by the Nitoo and MyHome specifications.
/// </summary>
public enum OpenNettyDeviceIdentifierType
{
    /// <summary>
    /// Unknown identifier.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// MAC address.
    /// </summary>
    MacAddress = 1,

    /// <summary>
    /// Nitoo serial number.
    /// </summary>
    NitooSerialNumber = 2,

    /// <summary>
    /// SCS serial number.
    /// </summary>
    ScsSerialNumber = 3,

    /// <summary>
    /// Zigbee serial number.
    /// </summary>
    ZigbeeSerialNumber = 4
}
