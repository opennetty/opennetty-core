/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace OpenNetty.Mqtt;

/// <summary>
/// Exposes extensions allowing to register the OpenNetty MQTT services.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class OpenNettyMqttConfiguration : IPostConfigureOptions<OpenNettyMqttOptions>,
                                                 IValidateOptions<OpenNettyOptions>,
                                                 IValidateOptions<OpenNettyMqttOptions>
{
    /// <inheritdoc/>
    public void PostConfigure(string? name, OpenNettyMqttOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.HomeAssistantDiscoveryUICulture ??= CultureInfo.CurrentUICulture;

        if (string.IsNullOrEmpty(options.HomeAssistantDiscoveryRootTopic))
        {
            options.HomeAssistantDiscoveryRootTopic = "homeassistant";
        }

        if (string.IsNullOrEmpty(options.RootTopic))
        {
            options.RootTopic = "opennetty";
        }
    }

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, OpenNettyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = new ValidateOptionsResultBuilder();

        foreach (var endpoint in options.Endpoints)
        {
            if (endpoint.GetStringSetting(OpenNettySettings.MqttTopic) is string topic &&
                (topic.Contains('+', StringComparison.OrdinalIgnoreCase) ||
                 topic.Contains('*', StringComparison.OrdinalIgnoreCase)))
            {
                builder.AddError(SR.FormatID2005(topic));
            }
        }

        return builder.Build();
    }

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, OpenNettyMqttOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = new ValidateOptionsResultBuilder();

        if (options.RootTopic.Contains('+', StringComparison.OrdinalIgnoreCase) ||
            options.RootTopic.Contains('*', StringComparison.OrdinalIgnoreCase))
        {
            builder.AddError(SR.GetResourceString(SR.ID2001));
        }

        if (options.RootTopic.EndsWith("/", StringComparison.OrdinalIgnoreCase))
        {
            builder.AddError(SR.GetResourceString(SR.ID2007));
        }

        if (options.HomeAssistantDiscoveryRootTopic.EndsWith("/", StringComparison.OrdinalIgnoreCase))
        {
            builder.AddError(SR.GetResourceString(SR.ID2008));
        }

        if (string.Equals(options.HomeAssistantDiscoveryRootTopic, options.RootTopic, StringComparison.OrdinalIgnoreCase))
        {
            builder.AddError(SR.GetResourceString(SR.ID2009));
        }

        return builder.Build();
    }
}
