/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Provides various settings used by workers to manage sessions and dispatch messages.
/// </summary>
public sealed record class OpenNettyWorkerOptions
{
    /// <summary>
    /// Gets or sets the maximum lifetime of command sessions.
    /// </summary>
    public required TimeSpan CommandSessionMaximumLifetime { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of concurrent command sessions allowed.
    /// </summary>
    public required byte MaximumConcurrentCommandSessions { get; init; }

    /// <summary>
    /// Creates a default instance of the <see cref="OpenNettyWorkerOptions"/>
    /// class with default options appropriate for the specified device.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <returns>A default instance of the <see cref="OpenNettyWorkerOptions"/> class.</returns>
    public static OpenNettyWorkerOptions CreateDefaults(OpenNettyDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new()
        {
            CommandSessionMaximumLifetime    = device.Definition.Protocol is OpenNettyProtocol.Scs ? TimeSpan.FromSeconds(20) : TimeSpan.Zero,
            MaximumConcurrentCommandSessions = device.Definition.Protocol is OpenNettyProtocol.Scs ? (byte) 3 : (byte) 0,
        };
    }
}
