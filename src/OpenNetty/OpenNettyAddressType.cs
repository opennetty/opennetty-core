/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

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
    /// Nitoo address.
    /// </summary>
    Nitoo = 1,

    /// <summary>
    /// Zigbee address.
    /// </summary>
    Zigbee = 2,

    /// <summary>
    /// SCS light point address.
    /// </summary>
    ScsLightPoint = 3,

    /// <summary>
    /// SCS scenario plus address.
    /// </summary>
    ScsScenarioPlus = 4,

    /// <summary>
    /// SCS dry contact address.
    /// </summary>
    ScsDryContact = 5
}
