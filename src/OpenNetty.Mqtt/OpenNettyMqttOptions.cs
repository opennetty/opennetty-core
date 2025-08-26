/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using MQTTnet.Client;

namespace OpenNetty.Mqtt;

/// <summary>
/// Provides various settings needed to configure the OpenNetty MQTT services.
/// </summary>
public sealed class OpenNettyMqttOptions
{
    /// <summary>
    /// Gets or sets the MQTT client options.
    /// </summary>
    public MqttClientOptions? ClientOptions { get; set; }

    /// <summary>
    /// Gets or sets a boolean indicating whether Home Assistant discovery is enabled.
    /// </summary>
    public bool DisableDiscovery { get; set; }

    /// <summary>
    /// Gets or sets the MQTT discovery root topic (by default, "homeassistant").
    /// </summary>
    public string DiscoveryRootTopic { get; set; } = default!;

    /// <summary>
    /// Gets or sets the MQTT root topic (by default, "opennetty").
    /// </summary>
    public string RootTopic { get; set; } = default!;
}
