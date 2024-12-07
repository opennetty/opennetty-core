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
