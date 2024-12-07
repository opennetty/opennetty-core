/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Threading.Channels;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient;

namespace OpenNetty.Mqtt;

/// <summary>
/// Represents a worker responsible for processing incoming MQTT application messages.
/// </summary>
public interface IOpenNettyMqttWorker
{
    /// <summary>
    /// Processes incoming MQTT application messages.
    /// </summary>
    /// <param name="client">The MQTT client.</param>
    /// <param name="reader">The channel reader used to iterate incoming MQTT application messages.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="Task"/> that can be used to monitor the asynchronous operation.</returns>
    Task ProcessMessagesAsync(
        IManagedMqttClient client,
        ChannelReader<MqttApplicationMessage> reader,
        CancellationToken cancellationToken);
}