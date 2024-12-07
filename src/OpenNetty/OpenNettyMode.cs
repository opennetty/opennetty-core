/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Exposes common OpenNetty transmission modes, as defined by the Nitoo and MyHome specifications.
/// </summary>
public enum OpenNettyMode
{
    /// <summary>
    /// Broadcast (one-to-all communication).
    /// </summary>
    Broadcast = 0,

    /// <summary>
    /// Multicast (one-to-many communication).
    /// </summary>
    Multicast = 1,

    /// <summary>
    /// Unicast (one-to-one communication).
    /// </summary>
    Unicast = 2
}
