/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using Microsoft.Extensions.Options;

namespace OpenNetty.Mqtt;

/// <summary>
/// Exposes extensions allowing to register the OpenNetty MQTT services.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class OpenNettyMqttConfiguration : IPostConfigureOptions<OpenNettyMqttOptions>,
    IValidateOptions<OpenNettyMqttOptions>
{
    /// <inheritdoc/>
    public void PostConfigure(string? name, OpenNettyMqttOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.EndpointNameProvider ??= static endpoint => endpoint.Name?.ToLowerInvariant();

        if (string.IsNullOrEmpty(options.RootTopic))
        {
            options.RootTopic = "opennetty";
        }
    }

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, OpenNettyMqttOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.RootTopic.Contains('+', StringComparison.OrdinalIgnoreCase) ||
            options.RootTopic.Contains('*', StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(SR.GetResourceString(SR.ID2001));
        }

        return ValidateOptionsResult.Success;
    }
}
