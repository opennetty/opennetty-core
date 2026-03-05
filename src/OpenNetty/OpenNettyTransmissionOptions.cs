/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace OpenNetty;

/// <summary>
/// Provides various settings used to control how messages are sent by a session.
/// </summary>
public sealed record OpenNettyTransmissionOptions
{
    /// <summary>
    /// Gets or sets the action validation timeout (Nitoo only).
    /// </summary>
    public required TimeSpan ActionValidationTimeout { get; init; }

    /// <summary>
    /// Gets or sets a boolean indicating whether the message
    /// can be replayed if an error occurs while sending it.
    /// </summary>
    public required bool DisallowUnsafeRetransmissions { get; init; }

    /// <summary>
    /// Gets or sets the frame acknowledgement timeout.
    /// </summary>
    public required TimeSpan FrameAcknowledgementTimeout { get; init; }

    /// <summary>
    /// Gets or sets a boolean indicating whether OpenNetty should
    /// wait for the gateway to return an ACK, BUSY NACK or NACK frame.
    /// </summary>
    public required bool IgnoreAcknowledgementValidation { get; init; }

    /// <summary>
    /// Gets or sets a boolean indicating whether OpenNetty should wait for the
    /// end device to reply with a VALID ACTION or INVALID ACTION frame (Nitoo only).
    /// </summary>
    public required bool IgnoreActionValidation { get; init; }

    /// <summary>
    /// Gets or sets the reply timeout used when multiple dimensions should be returned.
    /// </summary>
    public required TimeSpan MultipleDimensionReplyTimeout { get; init; }

    /// <summary>
    /// Gets or sets the reply timeout used when multiple status replies should be returned.
    /// </summary>
    public required TimeSpan MultipleStatusReplyTimeout { get; init; }

    /// <summary>
    /// Gets or sets the outgoing message processing timeout.
    /// </summary>
    public required TimeSpan OutgoingMessageProcessingTimeout { get; init; }

    /// <summary>
    /// Gets or sets the <see cref="ResiliencePipeline"/> used to send an outgoing message.
    /// </summary>
    public required ResiliencePipeline OutgoingMessageResiliencePipeline { get; init; }

    /// <summary>
    /// Gets or sets the post-sending delay, if applicable.
    /// </summary>
    public required TimeSpan PostSendingDelay { get; init; }

    /// <summary>
    /// Gets or sets the reply timeout used when a single dimension should be returned.
    /// </summary>
    public required TimeSpan UniqueDimensionReplyTimeout { get; init; }

    /// <summary>
    /// Gets or sets the reply timeout used when a unique status reply should be returned.
    /// </summary>
    public required TimeSpan UniqueStatusReplyTimeout { get; init; }

    /// <summary>
    /// Creates a default instance of the <see cref="OpenNettyTransmissionOptions"/>
    /// class with default options appropriate for the specified device.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <returns>A default instance of the <see cref="OpenNettyTransmissionOptions"/> class.</returns>
    public static OpenNettyTransmissionOptions CreateDefaults(OpenNettyDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new()
        {
            ActionValidationTimeout          = device.Definition.Protocol is OpenNettyProtocol.Nitoo ? TimeSpan.FromSeconds(2) : TimeSpan.Zero,
            DisallowUnsafeRetransmissions    = false,
            FrameAcknowledgementTimeout      = TimeSpan.FromSeconds(5),
            IgnoreAcknowledgementValidation  = false,
            IgnoreActionValidation           = false,
            MultipleDimensionReplyTimeout    = device.Definition.Protocol is OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee ? TimeSpan.FromSeconds(10) : TimeSpan.Zero,
            MultipleStatusReplyTimeout       = device.Definition.Protocol is OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee ? TimeSpan.FromSeconds(10) : TimeSpan.Zero,
            OutgoingMessageProcessingTimeout = TimeSpan.FromSeconds(10),
            PostSendingDelay                 = device.Definition.Protocol is OpenNettyProtocol.Nitoo ? TimeSpan.FromMilliseconds(150) : TimeSpan.Zero,
            UniqueDimensionReplyTimeout      = TimeSpan.FromSeconds(2),
            UniqueStatusReplyTimeout         = TimeSpan.FromSeconds(2),

            OutgoingMessageResiliencePipeline = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions
            {
                DelayGenerator = static arguments => new(arguments.AttemptNumber switch
                {
                    0 => TimeSpan.FromMilliseconds(250),
                    1 => TimeSpan.FromMilliseconds(500),
                    _ => TimeSpan.FromMilliseconds(1_000)
                }),
                // Note: this setting is deliberately set to the maximum value allowed
                // to be able to define it dynamically in the ShouldHandle delegate.
                MaxRetryAttempts = int.MaxValue,
                ShouldHandle = static arguments =>
                {
                    if (!arguments.Context.Properties.TryGetValue(
                        key  : new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)),
                        value: out OpenNettyGateway? gateway))
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0067));
                    }

                    if (!arguments.Context.Properties.TryGetValue(
                        key  : new ResiliencePropertyKey<ILogger<OpenNettyService>>(nameof(ILogger<>)),
                        value: out ILogger<OpenNettyService>? logger))
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0067));
                    }

                    if (!arguments.Context.Properties.TryGetValue(
                        key  : new ResiliencePropertyKey<OpenNettyMessage>(nameof(OpenNettyMessage)),
                        value: out OpenNettyMessage? message))
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0067));
                    }

                    if (!arguments.Context.Properties.TryGetValue(
                        key  : new ResiliencePropertyKey<OpenNettyTransmissionOptions>(nameof(OpenNettyTransmissionOptions)),
                        value: out OpenNettyTransmissionOptions? options))
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0067));
                    }

                    // Never retransmit a message if no exception was thrown.
                    if (arguments.Outcome.Exception is null)
                    {
                        return ValueTask.FromResult(false);
                    }

                    logger.LogInformation(6016, arguments.Outcome.Exception, SR.GetResourceString(SR.ID6016), gateway, message);

                    return ValueTask.FromResult(arguments.Outcome.Exception switch
                    {
                        // Nitoo gateways are known for returning NACK frames when sending multiple messages
                        // in a row. In this case, always retry sending the message 3 times before giving up.
                        OpenNettyException { ErrorCode: OpenNettyErrorCode.InvalidFrame }
                            when message.Protocol is OpenNettyProtocol.Nitoo => arguments.AttemptNumber is < 3,

                        // In large Zigbee networks, Zigbee gateways can sometimes return BUSY NACK frames
                        // when the network is overloaded (e.g when sending multiple broadcast frames).
                        // In this case, always retry sending the message twice before giving up.
                        OpenNettyException { ErrorCode: OpenNettyErrorCode.InvalidFrame or OpenNettyErrorCode.GatewayBusy }
                            when message.Protocol is OpenNettyProtocol.Zigbee => arguments.AttemptNumber is < 2,

                        // SCS gateways are less easily overloaded. As such, retry sending the message only once before giving up.
                        OpenNettyException { ErrorCode: OpenNettyErrorCode.InvalidFrame }
                            when message.Protocol is OpenNettyProtocol.Scs => arguments.AttemptNumber is < 1,

                        // For messages sent via powerline or radio (that are prone to interference), always retry
                        // twice if the error was caused by a missing reply from the end device, unless the sender
                        // explicitly specified that unsafe retransmissions are not allowed for this message.
                        OpenNettyException { ErrorCode: OpenNettyErrorCode.NoActionReceived    or
                                                        OpenNettyErrorCode.NoDimensionReceived or
                                                        OpenNettyErrorCode.NoStatusReceived }
                            when message.Medium is OpenNettyMedium.Powerline or OpenNettyMedium.Radio
                            => !options.DisallowUnsafeRetransmissions && arguments.AttemptNumber is < 2,

                        // For messages sent via a dedicated bus, retry only once if the error was caused
                        // by a missing reply from the end device, unless the sender explicitly specified
                        // that unsafe retransmissions are not allowed for this message.
                        OpenNettyException { ErrorCode: OpenNettyErrorCode.InvalidFrame or OpenNettyErrorCode.GatewayBusy }
                            when message.Medium is OpenNettyMedium.Bus
                            => !options.DisallowUnsafeRetransmissions && arguments.AttemptNumber is < 1,

                        _ => false
                    });
                },
                OnRetry = static arguments =>
                {
                    if (!arguments.Context.Properties.TryGetValue(
                        key  : new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)),
                        value: out OpenNettyGateway? gateway))
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0067));
                    }

                    if (!arguments.Context.Properties.TryGetValue(
                        key  : new ResiliencePropertyKey<ILogger<OpenNettyService>>(nameof(ILogger<>)),
                        value: out ILogger<OpenNettyService>? logger))
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0067));
                    }

                    if (!arguments.Context.Properties.TryGetValue(
                        key  : new ResiliencePropertyKey<OpenNettyMessage>(nameof(OpenNettyMessage)),
                        value: out OpenNettyMessage? message))
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0067));
                    }

                    logger.LogInformation(6017, SR.GetResourceString(SR.ID6017),
                        gateway, message, (uint) arguments.AttemptNumber + 1);

                    return ValueTask.CompletedTask;
                }
            }).Build()
        };
    }
}
