/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty.Mqtt;

/// <summary>
/// Represents an OpenNetty MQTT operation.
/// </summary>
public enum OpenNettyMqttOperation
{
    /// <summary>
    /// Get (/get).
    /// </summary>
    Get = 0,

    /// <summary>
    /// Set (/set).
    /// </summary>
    Set = 1
}
