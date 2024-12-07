/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace OpenNetty;

/// <summary>
/// Contains methods used to log strongly-typed messages.
/// </summary>
/// <typeparam name="TService">The generic typed used to infer a category name.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class OpenNettyLogger<TService>
{
    private readonly ILogger<TService> _logger;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyLogger{TCategoryName}"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public OpenNettyLogger(ILogger<TService> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Logs a message indicating that the hosted service is starting.
    /// </summary>
    [LoggerMessage(
        EventId = 6000,
        Level = LogLevel.Information,
        Message = "The OpenNetty hosted service is starting.")]
    public partial void HostedServiceStarting();

    /// <summary>
    /// Logs a message indicating that the hosted service has started.
    /// </summary>
    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Information,
        Message = "The OpenNetty hosted service has successfully started.")]
    public partial void HostedServiceStarted();

    /// <summary>
    /// Logs a message indicating that the hosted service has stopped.
    /// </summary>
    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Information,
        Message = "The OpenNetty hosted service has successfully stopped.")]
    public partial void HostedServiceStopped();

    /// <summary>
    /// Logs a message indicating that an exception error occurred while starting the hosted service.
    /// </summary>
    /// <param name="exception">The exception.</param>
    [LoggerMessage(
        EventId = 6003,
        Level = LogLevel.Critical,
        Message = "The OpenNetty hosted service has failed due to an unexpected exception.")]
    public partial void HostedServiceFailed(Exception exception);

    /// <summary>
    /// Logs a message indicating that a worker is starting.
    /// </summary>
    /// <param name="gateway">The gateway.</param>
    [LoggerMessage(
        EventId = 6004,
        Level = LogLevel.Information,
        Message = "The worker associated to the gateway {Gateway} is starting.")]
    public partial void WorkerStarting(OpenNettyGateway gateway);

    /// <summary>
    /// Logs a message indicating that a worker has started.
    /// </summary>
    /// <param name="gateway">The gateway.</param>
    [LoggerMessage(
        EventId = 6005,
        Level = LogLevel.Information,
        Message = "The worker associated to the gateway {Gateway} has successfully started.")]
    public partial void WorkerStarted(OpenNettyGateway gateway);

    /// <summary>
    /// Logs a message indicating that a long-lived task runner was scheduled.
    /// </summary>
    /// <param name="gateway">The gateway.</param>
    /// <param name="type">The session type.</param>
    [LoggerMessage(
        EventId = 6006,
        Level = LogLevel.Information,
        Message = "A task runner was successfully scheduled for gateway {Gateway} (session type: {Type}).")]
    public partial void TaskRunnerScheduled(OpenNettyGateway gateway, OpenNettySessionType type);

    /// <summary>
    /// Logs a message indicating that a new session was open.
    /// </summary>
    /// <param name="gateway">The gateway.</param>
    /// <param name="type">The session type.</param>
    /// <param name="session">The session.</param>
    [LoggerMessage(
        EventId = 6007,
        Level = LogLevel.Debug,
        Message = "A new session of type {Type} was open to gateway {Gateway}: {Session}.")]
    public partial void SessionOpen(OpenNettyGateway gateway, OpenNettySessionType type, OpenNettySession session);

    /// <summary>
    /// Logs a message indicating that a session was closed.
    /// </summary>
    /// <param name="session">The session.</param>
    [LoggerMessage(
        EventId = 6008,
        Level = LogLevel.Debug,
        Message = "The session {Session} was closed.")]
    public partial void SessionClosed(OpenNettySession session);

    /// <summary>
    /// Logs a message indicating that an incoming message was received.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="gateway">The gateway.</param>
    /// <param name="session">The session.</param>
    [LoggerMessage(
        EventId = 6009,
        Level = LogLevel.Debug,
        Message = "An incoming message was received by gateway {Gateway} using session {Session}: {Message}.")]
    public partial void MessageReceived(OpenNettyMessage message, OpenNettyGateway gateway, OpenNettySession session);

    /// <summary>
    /// Logs a message indicating that an outgoing message was sent.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="gateway">The gateway.</param>
    /// <param name="session">The session.</param>
    [LoggerMessage(
        EventId = 6010,
        Level = LogLevel.Debug,
        Message = "An outgoing message was successfully sent by gateway {Gateway} using session {Session}: {Message}.")]
    public partial void MessageSent(OpenNettyMessage message, OpenNettyGateway gateway, OpenNettySession session);

    /// <summary>
    /// Logs a message indicating that an outgoing message couldn't be sent because the gateway was too busy.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="gateway">The gateway.</param>
    /// <param name="session">The session.</param>
    [LoggerMessage(
        EventId = 6011,
        Level = LogLevel.Information,
        Message = "An outgoing message couldn't be sent by gateway {Gateway} using session {Session} because the gateway was too busy: {Message}.")]
    public partial void GatewayBusy(OpenNettyMessage message, OpenNettyGateway gateway, OpenNettySession session);

    /// <summary>
    /// Logs a message indicating that an outgoing message was rejected by the end device.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="gateway">The gateway.</param>
    /// <param name="session">The session.</param>
    [LoggerMessage(
        EventId = 6012,
        Level = LogLevel.Information,
        Message = "An outgoing message was sent by gateway {Gateway} using session {Session} but was rejected by the end device: {Message}.")]
    public partial void InvalidAction(OpenNettyMessage message, OpenNettyGateway gateway, OpenNettySession session);

    /// <summary>
    /// Logs a message indicating that an outgoing message couldn't be sent because no action validation frame was received.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="gateway">The gateway.</param>
    /// <param name="session">The session.</param>
    [LoggerMessage(
        EventId = 6013,
        Level = LogLevel.Information,
        Message = "An outgoing message was sent by gateway {Gateway} using session {Session} but was not acknowledged by the end device: {Message}.")]
    public partial void NoActionReceived(OpenNettyMessage message, OpenNettyGateway gateway, OpenNettySession session);

    /// <summary>
    /// Logs a message indicating that an outgoing message couldn't be sent because no acknowledgment frame was received.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="gateway">The gateway.</param>
    /// <param name="session">The session.</param>
    [LoggerMessage(
        EventId = 6014,
        Level = LogLevel.Information,
        Message = "An outgoing message couldn't be sent by gateway {Gateway} using session {Session} as no acknowledgement frame was received: {Message}.")]
    public partial void NoAcknowledgementReceived(OpenNettyMessage message, OpenNettyGateway gateway, OpenNettySession session);

    /// <summary>
    /// Logs a message indicating that an outgoing message was rejected by the gateway.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="gateway">The gateway.</param>
    /// <param name="session">The session.</param>
    [LoggerMessage(
        EventId = 6015,
        Level = LogLevel.Information,
        Message = "An outgoing message was rejected by gateway {Gateway} using session {Session}: {Message}.")]
    public partial void InvalidFrame(OpenNettyMessage message, OpenNettyGateway gateway, OpenNettySession session);

    /// <summary>
    /// Logs a message indicating that an outgoing message was not successfully sent.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <param name="message">The message.</param>
    /// <param name="gateway">The gateway.</param>
    [LoggerMessage(
        EventId = 6016,
        Level = LogLevel.Information,
        Message = "An error occurred while sending a message to gateway {Gateway}: {Message}.")]
    public partial void MessageErrored(Exception exception, OpenNettyMessage message, OpenNettyGateway gateway);

    /// <summary>
    /// Logs a message indicating that an outgoing message will be retransmitted.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="gateway">The gateway.</param>
    /// <param name="attempt">The attempt number.</param>
    [LoggerMessage(
        EventId = 6017,
        Level = LogLevel.Information,
        Message = "A message will be retransmitted by gateway {Gateway}: {Message} (attempt number n°{Attempt}).")]
    public partial void MessageRetransmitted(OpenNettyMessage message, OpenNettyGateway gateway, uint attempt);

    /// <summary>
    /// Logs a message indicating that an exception occurred in a Reactive Extensions event handler.
    /// </summary>
    /// <param name="exception">The exception.</param>
    [LoggerMessage(
        EventId = 6018,
        Level = LogLevel.Warning,
        Message = "An unhandled exception occurred in a Reactive Extensions event handler.")]
    public partial void UnhandledEventHandlerException(Exception exception);
}
