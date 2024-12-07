/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Represents an abstract OpenNetty notification.
/// </summary>
public abstract class OpenNettyNotification
{
    /// <summary>
    /// Gets or sets the OpenNetty gateway associated with the notification.
    /// </summary>
    public required OpenNettyGateway Gateway { get; init; }
}
