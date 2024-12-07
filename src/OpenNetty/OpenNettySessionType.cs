/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Represents the type of an OpenNetty session.
/// </summary>
public enum OpenNettySessionType
{
    /// <summary>
    /// The session doesn't have a specific type.
    /// </summary>
    Generic = 0,

    /// <summary>
    /// The session is a command session.
    /// </summary>
    Command = 1,

    /// <summary>
    /// The session is an event session.
    /// </summary>
    Event = 2
}
