﻿using MQTTnet.Client;

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
    /// Gets or sets the delegate responsible for resolving and,
    /// if applicable, normalizing the name associated with an endpoint.
    /// </summary>
    /// <remarks>
    /// By default, OpenNetty always lowercases the endpoint name.
    /// </remarks>
    public Func<OpenNettyEndpoint, string?> EndpointNameProvider { get; set; } = default!;

    /// <summary>
    /// Gets or sets the MQTT root topic (by default, "opennetty").
    /// </summary>
    public string RootTopic { get; set; } = "opennetty";
}