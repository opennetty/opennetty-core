/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Exposes common notifications supported by the OpenNetty stack.
/// </summary>
public static class OpenNettyNotifications
{
    /// <summary>
    /// Represents a notification dispatched when an outgoing message is ready to be sent.
    /// </summary>
    public sealed class MessageReady : OpenNettyNotification
    {
        /// <summary>
        /// Gets or sets the message to send.
        /// </summary>
        public required OpenNettyMessage Message { get; init; }

        /// <summary>
        /// Gets or sets the transmission options to use.
        /// </summary>
        public required OpenNettyTransmissionOptions Options { get; init; }

        /// <summary>
        /// Gets or sets the transaction associated with the notification.
        /// </summary>
        public required OpenNettyTransaction Transaction { get; init; }
    }

    /// <summary>
    /// Represents a notification dispatched when an outgoing message was successfully sent.
    /// </summary>
    public sealed class MessageSent : OpenNettyNotification
    {
        /// <summary>
        /// Gets or sets the message that was successfully sent.
        /// </summary>
        public required OpenNettyMessage Message { get; init; }

        /// <summary>
        /// Gets or sets the session that was used to send the message.
        /// </summary>
        public required OpenNettySession Session { get; init; }

        /// <summary>
        /// Gets or sets the transaction associated with the notification.
        /// </summary>
        public required OpenNettyTransaction Transaction { get; init; }
    }

    /// <summary>
    /// Represents a notification dispatched when an incoming message was received.
    /// </summary>
    public sealed class MessageReceived : OpenNettyNotification
    {
        /// <summary>
        /// Gets or sets the received message.
        /// </summary>
        public required OpenNettyMessage Message { get; init; }

        /// <summary>
        /// Gets or sets the session that received the message.
        /// </summary>
        public required OpenNettySession Session { get; init; }
    }

    /// <summary>
    /// Represents a notification dispatched when an outgoing message was rejected by a Nitoo device.
    /// </summary>
    public sealed class InvalidAction : OpenNettyNotification
    {
        /// <summary>
        /// Gets or sets the message that was rejected by the device.
        /// </summary>
        public required OpenNettyMessage Message { get; init; }

        /// <summary>
        /// Gets or sets the session that was used to send the message.
        /// </summary>
        public required OpenNettySession Session { get; init; }

        /// <summary>
        /// Gets or sets the transaction associated with the notification.
        /// </summary>
        public required OpenNettyTransaction Transaction { get; init; }
    }

    /// <summary>
    /// Represents a notification dispatched when an outgoing message was rejected by the gateway.
    /// </summary>
    public sealed class InvalidFrame : OpenNettyNotification
    {
        /// <summary>
        /// Gets or sets the message that was rejected by the gateway.
        /// </summary>
        public required OpenNettyMessage Message { get; init; }

        /// <summary>
        /// Gets or sets the session that was used to send the message.
        /// </summary>
        public required OpenNettySession Session { get; init; }

        /// <summary>
        /// Gets or sets the transaction associated with the notification.
        /// </summary>
        public required OpenNettyTransaction Transaction { get; init; }
    }

    /// <summary>
    /// Represents a notification dispatched when an outgoing message was not validated by a Nitoo device.
    /// </summary>
    public sealed class NoActionReceived : OpenNettyNotification
    {
        /// <summary>
        /// Gets or sets the message that wasn't validated by the device.
        /// </summary>
        public required OpenNettyMessage Message { get; init; }

        /// <summary>
        /// Gets or sets the session that was used to send the message.
        /// </summary>
        public required OpenNettySession Session { get; init; }

        /// <summary>
        /// Gets or sets the transaction associated with the notification.
        /// </summary>
        public required OpenNettyTransaction Transaction { get; init; }
    }

    /// <summary>
    /// Represents a notification dispatched when an outgoing message was not validated by the gateway.
    /// </summary>
    public sealed class NoAcknowledgmentReceived : OpenNettyNotification
    {
        /// <summary>
        /// Gets or sets the message that wasn't validated by the device.
        /// </summary>
        public required OpenNettyMessage Message { get; init; }

        /// <summary>
        /// Gets or sets the session that was used to send the message.
        /// </summary>
        public required OpenNettySession Session { get; init; }

        /// <summary>
        /// Gets or sets the transaction associated with the notification.
        /// </summary>
        public required OpenNettyTransaction Transaction { get; init; }
    }

    /// <summary>
    /// Represents a notification dispatched when an outgoing message was rejected by a busy gateway.
    /// </summary>
    public sealed class GatewayBusy : OpenNettyNotification
    {
        /// <summary>
        /// Gets or sets the message that was rejected by the gateway.
        /// </summary>
        public required OpenNettyMessage Message { get; init; }

        /// <summary>
        /// Gets or sets the session that was used to send the message.
        /// </summary>
        public required OpenNettySession Session { get; init; }

        /// <summary>
        /// Gets or sets the transaction associated with the notification.
        /// </summary>
        public required OpenNettyTransaction Transaction { get; init; }
    }
}
