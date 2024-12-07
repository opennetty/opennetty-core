using System.ComponentModel;
using Microsoft.Extensions.Options;

namespace OpenNetty.Mqtt;

/// <summary>
/// Exposes extensions allowing to register the OpenNetty MQTT services.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class OpenNettyMqttConfiguration : IPostConfigureOptions<OpenNettyMqttOptions>
{
    /// <inheritdoc/>
    public void PostConfigure(string? name, OpenNettyMqttOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.EndpointNameProvider ??= static endpoint => endpoint.Name?.ToLowerInvariant();
    }
}
