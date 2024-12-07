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
