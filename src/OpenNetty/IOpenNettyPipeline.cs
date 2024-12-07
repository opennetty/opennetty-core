/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;

namespace OpenNetty;

/// <summary>
/// Represents a thread-safe notification pipeline that can be observed and whose delivery order is guaranteed.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface IOpenNettyPipeline : IAsyncObservable<OpenNettyNotification>
{
    /// <summary>
    /// Connects the <see cref="IAsyncObservable{T}"/> so that notifications can start being processed.
    /// </summary>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous
    /// operation and whose result is used as a signal by the OpenNetty hosted service
    /// to inform the pipeline that no additional notification will be processed.
    /// </returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    ValueTask<IAsyncDisposable> ConnectAsync();

    /// <summary>
    /// Publishes a new notification.
    /// </summary>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    ValueTask PublishAsync(OpenNettyNotification notification, CancellationToken cancellationToken = default);
}