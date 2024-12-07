/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Exposes common OpenWebNet frames, as defined by the Nitoo and MyHome specifications.
/// </summary>
public static class OpenNettyFrames
{
    /// <summary>
    /// ACK frame.
    /// </summary>
    public static readonly OpenNettyFrame Acknowledgement = OpenNettyFrame.Parse("*#*1##");

    /// <summary>
    /// BUSY NACK frame (Zigbee-specific).
    /// </summary>
    public static readonly OpenNettyFrame BusyNegativeAcknowledgement = OpenNettyFrame.Parse("*#*6##");

    /// <summary>
    /// NACK frame.
    /// </summary>
    public static readonly OpenNettyFrame NegativeAcknowledgement = OpenNettyFrame.Parse("*#*0##");
}
