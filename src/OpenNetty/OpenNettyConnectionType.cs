/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Represents the type of an OpenNetty connection.
/// </summary>
public enum OpenNettyConnectionType
{
    /// <summary>
    /// The connection uses a serial port.
    /// </summary>
    Serial = 0,

    /// <summary>
    /// The connection uses a TCP socket.
    /// </summary>
    Tcp = 1
}
