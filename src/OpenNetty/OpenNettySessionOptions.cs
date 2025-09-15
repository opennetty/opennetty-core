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
/// Provides various settings used to manage an OpenNetty session.
/// </summary>
public sealed record class OpenNettySessionOptions
{
    /// <summary>
    /// Gets or sets the connection negotiation timeout.
    /// </summary>
    public required TimeSpan ConnectionNegotiationTimeout { get; init; }

    /// <summary>
    /// Gets or sets the <see cref="ResiliencePipeline"/> used to manage sessions.
    /// </summary>
    public required ResiliencePipeline SessionResiliencePipeline { get; init; }

    /// <summary>
    /// Creates a default instance of the <see cref="OpenNettySessionOptions"/>
    /// class with default options appropriate for the specified device.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <returns>A default instance of the <see cref="OpenNettySessionOptions"/> class.</returns>
    public static OpenNettySessionOptions CreateDefaults(OpenNettyDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new()
        {
            ConnectionNegotiationTimeout = TimeSpan.FromSeconds(10),

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
                    ShouldHandle = static arguments =>
                    {
                        if (!arguments.Context.Properties.TryGetValue(
                            key  : new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)),
                            value: out OpenNettyGateway? gateway))
                        {
                            throw new InvalidOperationException(SR.GetResourceString(SR.ID0067));
                        }

                        if (!arguments.Context.Properties.TryGetValue(
                            key  : new ResiliencePropertyKey<ILogger<OpenNettyWorker>>(nameof(ILogger<>)),
                            value: out ILogger<OpenNettyWorker>? logger))
                        {
                            throw new InvalidOperationException(SR.GetResourceString(SR.ID0067));
                        }

                        if (!arguments.Context.Properties.TryGetValue(
                            key  : new ResiliencePropertyKey<OpenNettySessionType>(nameof(OpenNettySessionType)),
                            value: out OpenNettySessionType type))
                        {
                            throw new InvalidOperationException(SR.GetResourceString(SR.ID0067));
                        }

                        if (arguments.Outcome.Exception is not Exception exception)
                        {
                            return ValueTask.FromResult(false);
                        }

                        logger.LogInformation(6020, exception, SR.GetResourceString(SR.ID6020), type, gateway);

                        // Always recreate new sessions on failed attempts, unless the operation was canceled by the worker.
                        return ValueTask.FromResult(!arguments.Context.CancellationToken.IsCancellationRequested);
                    }
                })
                .Build()
        };
    }
}
