/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Exposes common OpenNetty protocols, as defined by the Nitoo and MyHome specifications.
/// </summary>
public enum OpenNettyProtocol
{
    /// <summary>
    /// SCS (used in the Legrand/BTicino MyHome and MyHome Up products).
    /// </summary>
    Scs = 0,

    /// <summary>
    /// Nitoo (used in the In One by Legrand products).
    /// </summary>
    Nitoo = 1,

    /// <summary>
    /// Zigbee (used in the Legrand/BTicino MyHome Play products).
    /// </summary>
    Zigbee = 2
}
