/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyExtensionsTests
{
    [Fact]
    public void AddOpenNetty_NullServices_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("services", () => OpenNettyExtensions.AddOpenNetty(null!));
    }

    [Fact]
    public void AddOpenNetty_ReturnsBuilderBoundToTheServiceCollection()
    {
        var services = new ServiceCollection();

        var builder = services.AddOpenNetty();

        Assert.Same(services, builder.Services);
    }

    [Theory]
    [InlineData(typeof(OpenNettyController), typeof(OpenNettyController))]
    [InlineData(typeof(OpenNettyEvents), typeof(OpenNettyEvents))]
    [InlineData(typeof(OpenNettyManager), typeof(OpenNettyManager))]
    [InlineData(typeof(IOpenNettyPipeline), typeof(OpenNettyPipeline))]
    [InlineData(typeof(IOpenNettyService), typeof(OpenNettyService))]
    [InlineData(typeof(IOpenNettyWorker), typeof(OpenNettyWorker))]
    public void AddOpenNetty_RegistersSingletonServices(Type serviceType, Type implementationType)
    {
        var services = new ServiceCollection();

        services.AddOpenNetty();

        var descriptor = Assert.Single(services, descriptor => descriptor.ServiceType == serviceType);
        Assert.Equal(implementationType, descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Theory]
    [InlineData(typeof(IOpenNettyHandler), typeof(OpenNettyCoordinator))]
    [InlineData(typeof(IPostConfigureOptions<OpenNettyOptions>), typeof(OpenNettyConfiguration))]
    [InlineData(typeof(IValidateOptions<OpenNettyOptions>), typeof(OpenNettyConfiguration))]
    [InlineData(typeof(IHostedService), typeof(OpenNettyHostedService))]
    public void AddOpenNetty_RegistersEnumerableServices(Type serviceType, Type implementationType)
    {
        var services = new ServiceCollection();

        services.AddOpenNetty();

        var descriptor = Assert.Single(services, descriptor =>
            descriptor.ServiceType == serviceType && descriptor.ImplementationType == implementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddOpenNetty_RegistersLoggingAndOptions()
    {
        var services = new ServiceCollection();

        services.AddOpenNetty();

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<ILoggerFactory>());
        Assert.NotNull(provider.GetService<IOptions<OpenNettyOptions>>());
    }

    [Fact]
    public void AddOpenNetty_CalledMultipleTimes_DoesNotDuplicateOpenNettyRegistrations()
    {
        var once = new ServiceCollection();
        var twice = new ServiceCollection();

        once.AddOpenNetty();
        twice.AddOpenNetty();
        twice.AddOpenNetty();

        Assert.Equal(CountOpenNettyRegistrations(once), CountOpenNettyRegistrations(twice));
    }

    [Fact]
    public void AddOpenNetty_ExistingRegistration_IsNotReplaced()
    {
        var service = Substitute.For<IOpenNettyService>();
        var services = new ServiceCollection();
        services.AddSingleton(service);

        services.AddOpenNetty();

        var descriptor = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IOpenNettyService));
        Assert.Same(service, descriptor.ImplementationInstance);
    }

    [Fact]
    public void AddOpenNettyWithConfiguration_NullServices_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("services", () => OpenNettyExtensions.AddOpenNetty(null!, _ => { }));
    }

    [Fact]
    public void AddOpenNettyWithConfiguration_NullConfiguration_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>("configuration", () => services.AddOpenNetty(null!));
    }

    [Fact]
    public void AddOpenNettyWithConfiguration_InvokesConfigurationWithBuilderBoundToTheServiceCollection()
    {
        var services = new ServiceCollection();
        var builders = new List<OpenNettyBuilder>();

        services.AddOpenNetty(builders.Add);

        var builder = Assert.Single(builders);
        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void AddOpenNettyWithConfiguration_RegistersServicesAndReturnsTheServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddOpenNetty(_ => { });

        Assert.Same(services, result);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IOpenNettyService));
    }

    private static int CountOpenNettyRegistrations(IServiceCollection services)
    {
        var assembly = typeof(OpenNettyOptions).Assembly;

        return services.Count(descriptor => descriptor.ServiceType.Assembly == assembly || descriptor.ImplementationType?.Assembly == assembly);
    }
}
