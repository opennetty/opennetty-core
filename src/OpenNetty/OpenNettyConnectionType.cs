namespace OpenNetty;

/// <summary>
/// Represents the type of an OpenNetty connection.
/// </summary>
public enum OpenNettyConnectionType
{
    /// <summary>
    /// The connection uses a serial port.
    /// </summary>
    Serial = 0,

    /// <summary>
    /// The connection uses a TCP socket.
    /// </summary>
    Tcp = 1
}
