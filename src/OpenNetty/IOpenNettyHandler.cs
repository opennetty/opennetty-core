namespace OpenNetty;

/// <summary>
/// Represents an OpenNetty handler whose lifetime is controlled by the OpenNetty hosted service.
/// </summary>
/// <remarks>
/// This interface is typically used to subscribe to events before the
/// OpenNetty stack starts establishing sessions and processing events.
/// </remarks>
public interface IOpenNettyHandler
{
    /// <summary>
    /// Subscribes to OpenNetty events.
    /// </summary>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous
    /// operation and whose result is used by the OpenNetty hosted service to inform
    /// the handler that the subscription should be aborted and discarded.
    /// </returns>
    ValueTask<IAsyncDisposable> SubscribeAsync();
}
