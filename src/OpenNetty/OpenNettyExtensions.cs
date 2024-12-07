using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Exposes extensions allowing to register the OpenNetty services.
/// </summary>
public static class OpenNettyExtensions
{
    /// <summary>
    /// Provides a common entry point for registering the OpenNetty services.
    /// </summary>
    /// <param name="services">The services collection.</param>
    /// <remarks>This extension can be safely called multiple times.</remarks>
    /// <returns>The <see cref="OpenNettyBuilder"/> instance.</returns>
    public static OpenNettyBuilder AddOpenNetty(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging();
        services.AddOptionsWithValidateOnStart<OpenNettyOptions>();

        services.TryAddSingleton<OpenNettyController>();
        services.TryAddSingleton<OpenNettyEvents>();
        services.TryAddSingleton(typeof(OpenNettyLogger<>));
        services.TryAddSingleton<OpenNettyManager>();
        services.TryAddSingleton<IOpenNettyPipeline, OpenNettyPipeline>();
        services.TryAddSingleton<IOpenNettyService, OpenNettyService>();
        services.TryAddSingleton<IOpenNettyWorker, OpenNettyWorker>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOpenNettyHandler, OpenNettyCoordinator>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<OpenNettyOptions>, OpenNettyConfiguration>());

        services.AddHostedService<OpenNettyHostedService>();

        return new OpenNettyBuilder(services);
    }

    /// <summary>
    /// Provides a common entry point for registering the OpenNetty services.
    /// </summary>
    /// <param name="services">The services collection.</param>
    /// <param name="configuration">The configuration delegate used to register new services.</param>
    /// <remarks>This extension can be safely called multiple times.</remarks>
    /// <returns>The <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddOpenNetty(this IServiceCollection services, Action<OpenNettyBuilder> configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        configuration(services.AddOpenNetty());

        return services;
    }
}
