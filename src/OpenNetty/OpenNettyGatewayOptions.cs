/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Provides various settings used to communicate with an OpenNetty gateway.
/// </summary>
public sealed record class OpenNettyGatewayOptions
{
    /// <summary>
    /// Gets or sets the default session options to use when instantiating sessions.
    /// </summary>
    public required OpenNettySessionOptions DefaultSessionOptions { get; init; }

    /// <summary>
    /// Gets or sets the default transmission options to use when sending messages.
    /// </summary>
    public required OpenNettyTransmissionOptions DefaultTransmissionOptions { get; init; }

    /// <summary>
    /// Gets or sets the default worker options to use when managing workers.
    /// </summary>
    public required OpenNettyWorkerOptions DefaultWorkerOptions { get; init; }

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
            DefaultSessionOptions      = OpenNettySessionOptions.CreateDefaults(device),
            DefaultTransmissionOptions = OpenNettyTransmissionOptions.CreateDefaults(device),
            DefaultWorkerOptions       = OpenNettyWorkerOptions.CreateDefaults(device),
        };
    }
}
