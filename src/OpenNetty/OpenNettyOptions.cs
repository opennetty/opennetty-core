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
