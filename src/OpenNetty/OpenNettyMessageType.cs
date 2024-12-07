/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Exposes common OpenNetty message types, as defined by the Nitoo and MyHome specifications.
/// </summary>
public enum OpenNettyMessageType
{
    /// <summary>
    /// Unknown frame.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// ACK frame.
    /// </summary>
    Acknowledgement = 1,

    /// <summary>
    /// Bus command.
    /// </summary>
    BusCommand = 2,

    /// <summary>
    /// BUSY NACK frame (Zigbee-specific).
    /// </summary>
    BusyNegativeAcknowledgement = 3,

    /// <summary>
    /// Dimension read.
    /// </summary>
    DimensionRead = 4,

    /// <summary>
    /// Dimension request.
    /// </summary>
    DimensionRequest = 5,

    /// <summary>
    /// Dimension set.
    /// </summary>
    DimensionSet = 6,

    /// <summary>
    /// NACK frame.
    /// </summary>
    NegativeAcknowledgement = 7,

    /// <summary>
    /// Status request.
    /// </summary>
    StatusRequest = 8
}
