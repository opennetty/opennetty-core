/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using Polly;
using Polly.Retry;

namespace OpenNetty;

/// <summary>
/// Provides various settings used to communicate with an OpenNetty gateway.
/// </summary>
public sealed record OpenNettyGatewayOptions
{
    /// <summary>
    /// Gets or sets the action validation timeout (Nitoo only).
    /// </summary>
    public required TimeSpan ActionValidationTimeout { get; init; }

    /// <summary>
    /// Gets or sets the maximum lifetime of command sessions.
    /// </summary>
    public required TimeSpan CommandSessionMaximumLifetime { get; init; }

    /// <summary>
    /// Gets or sets the connection negotiation timeout.
    /// </summary>
    public required TimeSpan ConnectionNegotiationTimeout { get; init; }

    /// <summary>
    /// Gets or sets a boolean indicating whether the supervision mode should be enabled (Zigbee only).
    /// </summary>
    public required bool EnableSupervisionMode { get; init; }

    /// <summary>
    /// Gets or sets the frame acknowledgement timeout.
    /// </summary>
    public required TimeSpan FrameAcknowledgementTimeout { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of concurrent command sessions allowed.
    /// </summary>
    public required ushort MaximumConcurrentCommandSessions { get; init; }

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
    /// Gets or sets the <see cref="ResiliencePipeline"/> used to manage sessions.
    /// </summary>
    public required ResiliencePipeline SessionResiliencePipeline { get; init; }

    /// <summary>
    /// Gets or sets the <see cref="ResiliencePipeline"/> used to send an outgoing message.
    /// </summary>
    public required ResiliencePipeline OutgoingMessageResiliencePipeline { get; init; }

    /// <summary>
    /// Creates a default instance of the <see cref="OpenNettyGatewayOptions"/>
    /// class with default options appropriate for the specified device.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <returns>A default instance of the <see cref="OpenNettyGatewayOptions"/> class.</returns>
    public static OpenNettyGatewayOptions CreateDefaults(OpenNettyDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new()
        {
            ActionValidationTimeout          = device.Definition.Protocol is OpenNettyProtocol.Nitoo ? TimeSpan.FromSeconds(2) : TimeSpan.Zero,
            CommandSessionMaximumLifetime    = device.Definition.Protocol is OpenNettyProtocol.Scs ? TimeSpan.FromSeconds(20) : TimeSpan.Zero,
            ConnectionNegotiationTimeout     = TimeSpan.FromSeconds(10),
            EnableSupervisionMode            = device.Definition.HasCapability(OpenNettyCapabilities.ZigbeeSupervision),
            FrameAcknowledgementTimeout      = TimeSpan.FromSeconds(5),
            MaximumConcurrentCommandSessions = device.Definition.Protocol is OpenNettyProtocol.Scs ? (ushort) 3 : (ushort) 0,
            MultipleDimensionReplyTimeout    = device.Definition.Protocol is OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee ? TimeSpan.FromSeconds(10) : TimeSpan.Zero,
            MultipleStatusReplyTimeout       = device.Definition.Protocol is OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee ? TimeSpan.FromSeconds(10) : TimeSpan.Zero,
            OutgoingMessageProcessingTimeout = TimeSpan.FromSeconds(10),
            PostSendingDelay                 = device.Definition.Protocol is OpenNettyProtocol.Nitoo ? TimeSpan.FromMilliseconds(150) : TimeSpan.Zero,
            UniqueDimensionReplyTimeout      = TimeSpan.FromSeconds(2),
            UniqueStatusReplyTimeout         = TimeSpan.FromSeconds(2),

            OutgoingMessageResiliencePipeline = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions
            {
                DelayGenerator = static arguments =>
                {
                    if (!arguments.Context.Properties.TryGetValue(
                        key  : new ResiliencePropertyKey<OpenNettyTransmissionOptions>(nameof(OpenNettyTransmissionOptions)),
                        value: out OpenNettyTransmissionOptions options))
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0074));
                    }

                    // If the post-sending delay was disabled for this transmission,
                    // use longer pause times when retrying to send a message.
                    if (options.HasFlag(OpenNettyTransmissionOptions.DisablePostSendingDelay))
                    {
                        return new(arguments.AttemptNumber switch
                        {
                            0 => TimeSpan.FromMilliseconds(200),
                            1 => TimeSpan.FromMilliseconds(500),
                            _ => TimeSpan.FromMilliseconds(1_000)
                        });
                    }

                    return new(arguments.AttemptNumber switch
                    {
                        0 => TimeSpan.FromMilliseconds(100),
                        1 => TimeSpan.FromMilliseconds(300),
                        _ => TimeSpan.FromMilliseconds(800)
                    });
                },
                // Note: this setting is deliberately set to the maximum value allowed
                // to be able to define it dynamically in the ShouldHandle delegate.
                MaxRetryAttempts = int.MaxValue,
                ShouldHandle = static arguments =>
                {
                    if (!arguments.Context.Properties.TryGetValue(
                        key: new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)),
                        value: out OpenNettyGateway? gateway))
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0074));
                    }

                    if (!arguments.Context.Properties.TryGetValue(
                        key: new ResiliencePropertyKey<OpenNettyLogger<OpenNettyService>>(nameof(OpenNettyLogger<OpenNettyService>)),
                        value: out OpenNettyLogger<OpenNettyService>? logger))
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0074));
                    }

                    if (!arguments.Context.Properties.TryGetValue(
                        key  : new ResiliencePropertyKey<OpenNettyMessage>(nameof(OpenNettyMessage)),
                        value: out OpenNettyMessage? message))
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0074));
                    }

                    if (!arguments.Context.Properties.TryGetValue(
                        key  : new ResiliencePropertyKey<OpenNettyTransmissionOptions>(nameof(OpenNettyTransmissionOptions)),
                        value: out OpenNettyTransmissionOptions options))
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0074));
                    }

                    // Never retransmit a message if no exception was thrown.
                    if (arguments.Outcome.Exception is null)
                    {
                        return ValueTask.FromResult(false);
                    }

                    logger.MessageErrored(arguments.Outcome.Exception, message, gateway);

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
                            => arguments.AttemptNumber is < 2 && !options.HasFlag(OpenNettyTransmissionOptions.DisallowRetransmissions),

                        // For messages sent via a dedicated bus, retry only once if the error was caused
                        // by a missing reply from the end device, unless the sender explicitly specified
                        // that unsafe retransmissions are not allowed for this message.
                        OpenNettyException { ErrorCode: OpenNettyErrorCode.InvalidFrame or OpenNettyErrorCode.GatewayBusy }
                            when message.Medium is OpenNettyMedium.Bus
                            => arguments.AttemptNumber is < 1 && !options.HasFlag(OpenNettyTransmissionOptions.DisallowRetransmissions),

                        _ => false
                    });
                },
                OnRetry = static arguments =>
                {
                    if (!arguments.Context.Properties.TryGetValue(
                        key  : new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)),
                        value: out OpenNettyGateway? gateway))
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0074));
                    }

                    if (!arguments.Context.Properties.TryGetValue(
                        key  : new ResiliencePropertyKey<OpenNettyLogger<OpenNettyService>>(nameof(OpenNettyLogger<OpenNettyService>)),
                        value: out OpenNettyLogger<OpenNettyService>? logger))
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0074));
                    }

                    if (!arguments.Context.Properties.TryGetValue(
                        key  : new ResiliencePropertyKey<OpenNettyMessage>(nameof(OpenNettyMessage)),
                        value: out OpenNettyMessage? message))
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0074));
                    }

                    logger.MessageRetransmitted(message, gateway, (uint) arguments.AttemptNumber + 1);

                    return ValueTask.CompletedTask;
                }
            }).Build(),

            SessionResiliencePipeline = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = int.MaxValue,
                    ShouldHandle = static arguments => ValueTask.FromResult(
                        !arguments.Context.CancellationToken.IsCancellationRequested)
                })
                .AddRetry(new RetryStrategyOptions
                {
                    DelayGenerator = static arguments => new(arguments.AttemptNumber switch
                    {
                        0      or      1 => TimeSpan.FromSeconds(1),
                        2      or      3 => TimeSpan.FromSeconds(5),
                        4      or      5 => TimeSpan.FromSeconds(10),
                        6 or 7 or 8 or 9 => TimeSpan.FromSeconds(30),
                               _         => TimeSpan.FromSeconds(60)
                    }),
                    MaxRetryAttempts = int.MaxValue,
                    ShouldHandle = static arguments => ValueTask.FromResult(
                        !arguments.Context.CancellationToken.IsCancellationRequested &&
                        arguments.Outcome.Exception is not null)
                })
                .Build()
        };
    }
}
