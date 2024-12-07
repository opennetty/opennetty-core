/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Exposes extensions allowing to register the OpenNetty MQTT services.
/// </summary>
public static class OpenNettyMqttExtensions
{
    /// <summary>
    /// Registers the OpenNetty MQTT services in the DI container.
    /// </summary>
    /// <param name="builder">The services builder used by OpenNetty to register new services.</param>
    /// <remarks>This extension can be safely called multiple times.</remarks>
    /// <returns>The <see cref="OpenNettyMqttBuilder"/> instance.</returns>
    public static OpenNettyMqttBuilder AddMqttIntegration(this OpenNettyBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptionsWithValidateOnStart<OpenNettyMqttOptions>();

        builder.Services.TryAddSingleton(static provider => new MqttFactory());
        builder.Services.TryAddSingleton(static provider =>
        {
            var factory = provider.GetRequiredService<MqttFactory>();
            return factory.CreateManagedMqttClient();
        });

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IOpenNettyHandler, OpenNettyMqttHostedService>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IPostConfigureOptions<OpenNettyMqttOptions>, OpenNettyMqttConfiguration>());

        builder.Services.TryAddSingleton<IOpenNettyMqttWorker, OpenNettyMqttWorker>();

        builder.Services.AddHostedService<OpenNettyMqttHostedService>();

        return new OpenNettyMqttBuilder(builder.Services);
    }

    /// <summary>
    /// Registers the OpenNetty MQTT services in the DI container.
    /// </summary>
    /// <param name="builder">The services builder used by OpenNetty to register new services.</param>
    /// <param name="configuration">The configuration delegate used to configure the core services.</param>
    /// <remarks>This extension can be safely called multiple times.</remarks>
    /// <returns>The <see cref="OpenNettyBuilder"/> instance.</returns>
    public static OpenNettyBuilder AddMqttIntegration(this OpenNettyBuilder builder, Action<OpenNettyMqttBuilder> configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        configuration(builder.AddMqttIntegration());

        return builder;
    }
}
