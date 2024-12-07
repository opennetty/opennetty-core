/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Exposes common OpenNetty transmission options that can
/// be used to control how messages are sent by a session.
/// </summary>
[Flags]
public enum OpenNettyTransmissionOptions
{
    /// <summary>
    /// Default options.
    /// </summary>
    None = 0,

    /// <summary>
    /// Do no wait for the gateway to return an ACK, BUSYACK or NACK frame.
    /// </summary>
    IgnoreAcknowledgementValidation = 0x01,

    /// <summary>
    /// Wait for the end device to reply with a VALID ACTION or INVALID ACTION frame (Nitoo only).
    /// </summary>
    RequireActionValidation = 0x02,

    /// <summary>
    /// Do no add an additional delay after sending the message.
    /// </summary>
    DisablePostSendingDelay = 0x03,

    /// <summary>
    /// Prevent the message from being replayed if an error occurs while sending it.
    /// </summary>
    DisallowRetransmissions = 0x04
}
