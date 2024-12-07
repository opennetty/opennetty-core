/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Provides various settings needed to configure the OpenNetty services.
/// </summary>
public sealed class OpenNettyOptions
{
    /// <summary>
    /// Gets the list of registered gateways.
    /// </summary>
    public List<OpenNettyGateway> Gateways { get; } = [];

    /// <summary>
    /// Gets the list of registered endpoints.
    /// </summary>
    public List<OpenNettyEndpoint> Endpoints { get; } = [];
}
