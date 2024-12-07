/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using System.Threading.Channels;

namespace OpenNetty;

/// <summary>
/// Represents a worker responsible for processing incoming and outgoing notifications.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface IOpenNettyWorker
{
    /// <summary>
    /// Processes incoming and outgoing notifications for the specified gateway.
    /// </summary>
    /// <param name="gateway">The gateway.</param>
    /// <param name="reader">The channel reader used to iterate incoming notifications.</param>
    /// <param name="writer">The channel writer used to dispatch outgoing notifications.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="Task"/> that can be used to monitor the asynchronous operation.</returns>
    Task ProcessNotificationsAsync(
        OpenNettyGateway gateway,
        ChannelReader<OpenNettyNotification> reader,
        ChannelWriter<OpenNettyNotification> writer,
        CancellationToken cancellationToken);
}