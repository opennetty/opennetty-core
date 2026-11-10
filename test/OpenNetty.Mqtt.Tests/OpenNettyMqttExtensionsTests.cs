/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using NSubstitute;
using Xunit;

namespace OpenNetty.Mqtt.Tests;

public sealed class OpenNettyMqttExtensionsTests
{
    [Fact]
    public void AddMqttIntegration_NullBuilder_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("builder", () => OpenNettyMqttExtensions.AddMqttIntegration(null!));
    }

    [Fact]
    public void AddMqttIntegration_RegistersOptionsWithStartupValidation()
    {
        var services = new ServiceCollection();

        CreateBuilder(services).AddMqttIntegration();

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IOptions<OpenNettyMqttOptions>>());
        Assert.NotNull(provider.GetService<IStartupValidator>());
    }

    [Fact]
    public void AddMqttIntegration_PostConfiguresTheOptionsWithDefaultValues()
    {
        var services = new ServiceCollection();
        CreateBuilder(services).AddMqttIntegration();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<OpenNettyMqttOptions>>().Value;

        Assert.Equal("opennetty", options.RootTopic);
        Assert.Equal("homeassistant", options.HomeAssistantDiscoveryRootTopic);
        Assert.Same(CultureInfo.CurrentUICulture, options.HomeAssistantDiscoveryUICulture);
    }

    [Fact]
    public void AddMqttIntegration_InvalidOptions_FailTheStartupValidation()
    {
        var services = new ServiceCollection();
        CreateBuilder(services).AddMqttIntegration().SetRootTopic("invalid/");
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Equal(SR.GetResourceString(SR.ID2007), Assert.Single(exception.Failures));
    }

    [Fact]
    public void AddMqttIntegration_ValidOptions_PassTheStartupValidation()
    {
        var services = new ServiceCollection();
        CreateBuilder(services).AddMqttIntegration();
        using var provider = services.BuildServiceProvider();

        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Null(exception);
    }

    [Fact]
    public void AddMqttIntegration_InvalidEndpoints_FailTheOpenNettyOptionsValidation()
    {
        var services = new ServiceCollection();
        var builder = CreateBuilder(services);
        builder.AddMqttIntegration();
        builder.Configure(static options => options.Endpoints.Add(new OpenNettyEndpoint
        {
            Name = "Endpoint",
            Protocol = OpenNettyProtocol.Scs,
            Settings = ImmutableDictionary<OpenNettySetting, string>.Empty.Add(OpenNettySettings.MqttTopic, "invalid/+")
        }));
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<OpenNettyOptions>>().Value);

        Assert.Equal(SR.FormatID2005("invalid/+"), Assert.Single(exception.Failures));
    }

    [Theory]
    [InlineData(typeof(MqttFactory))]
    [InlineData(typeof(IManagedMqttClient))]
    public void AddMqttIntegration_RegistersSingletonFactories(Type serviceType)
    {
        var services = new ServiceCollection();

        CreateBuilder(services).AddMqttIntegration();

        var descriptor = Assert.Single(services, descriptor => descriptor.ServiceType == serviceType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    [Fact]
    public void AddMqttIntegration_ResolvesTheMqttFactoryAsSingleton()
    {
        var services = new ServiceCollection();
        CreateBuilder(services).AddMqttIntegration();
        using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<MqttFactory>(), provider.GetRequiredService<MqttFactory>());
    }

    [Fact]
    public void AddMqttIntegration_ResolvesTheManagedClientFromTheFactory()
    {
        var services = new ServiceCollection();
        CreateBuilder(services).AddMqttIntegration();
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IManagedMqttClient>();

        Assert.NotNull(client);
        Assert.Same(client, provider.GetRequiredService<IManagedMqttClient>());
    }

    [Theory]
    [InlineData(typeof(IOpenNettyHandler), typeof(OpenNettyMqttHostedService))]
    [InlineData(typeof(IHostedService), typeof(OpenNettyMqttHostedService))]
    [InlineData(typeof(IPostConfigureOptions<OpenNettyMqttOptions>), typeof(OpenNettyMqttConfiguration))]
    [InlineData(typeof(IValidateOptions<OpenNettyOptions>), typeof(OpenNettyMqttConfiguration))]
    [InlineData(typeof(IValidateOptions<OpenNettyMqttOptions>), typeof(OpenNettyMqttConfiguration))]
    public void AddMqttIntegration_RegistersEnumerableServices(Type serviceType, Type implementationType)
    {
        var services = new ServiceCollection();

        CreateBuilder(services).AddMqttIntegration();

        var descriptor = Assert.Single(services, descriptor =>
            descriptor.ServiceType == serviceType && descriptor.ImplementationType == implementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddMqttIntegration_RegistersTheMqttWorkerAsSingleton()
    {
        var services = new ServiceCollection();

        CreateBuilder(services).AddMqttIntegration();

        var descriptor = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IOpenNettyMqttWorker));
        Assert.Equal(typeof(OpenNettyMqttWorker), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddMqttIntegration_ReturnsMqttBuilderBoundToTheServiceCollection()
    {
        var services = new ServiceCollection();

        var builder = CreateBuilder(services).AddMqttIntegration();

        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void AddMqttIntegration_ExistingRegistrations_AreNotReplaced()
    {
        var factory = new MqttFactory();
        var worker = Substitute.For<IOpenNettyMqttWorker>();
        var services = new ServiceCollection();
        services.AddSingleton(factory);
        services.AddSingleton(worker);

        CreateBuilder(services).AddMqttIntegration();

        Assert.Same(factory, Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(MqttFactory)).ImplementationInstance);
        Assert.Same(worker, Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IOpenNettyMqttWorker)).ImplementationInstance);
    }

    [Fact]
    public void AddMqttIntegration_CalledMultipleTimes_DoesNotDuplicateTheServices()
    {
        var once = new ServiceCollection();
        var twice = new ServiceCollection();

        CreateBuilder(once).AddMqttIntegration();
        var builder = CreateBuilder(twice);
        builder.AddMqttIntegration();
        builder.AddMqttIntegration();

        Assert.Equal(CountMqttRegistrations(once), CountMqttRegistrations(twice));
    }

    [Fact]
    public void AddMqttIntegration_CalledMultipleTimes_ReturnsNewBuildersSharingTheServiceCollection()
    {
        var services = new ServiceCollection();
        var builder = CreateBuilder(services);

        var first = builder.AddMqttIntegration();
        var second = builder.AddMqttIntegration();

        Assert.NotSame(first, second);
        Assert.Same(first.Services, second.Services);
    }

    [Fact]
    public void AddMqttIntegrationWithConfiguration_NullBuilder_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>("builder", () => OpenNettyMqttExtensions.AddMqttIntegration(null!, static _ => { }));
    }

    [Fact]
    public void AddMqttIntegrationWithConfiguration_NullConfiguration_ThrowsArgumentNullException()
    {
        var builder = CreateBuilder(new ServiceCollection());

        Assert.Throws<ArgumentNullException>("configuration", () => builder.AddMqttIntegration(null!));
    }

    [Fact]
    public void AddMqttIntegrationWithConfiguration_ReturnsTheSameBuilder()
    {
        var builder = CreateBuilder(new ServiceCollection());

        Assert.Same(builder, builder.AddMqttIntegration(static _ => { }));
    }

    [Fact]
    public void AddMqttIntegrationWithConfiguration_InvokesConfigurationWithMqttBuilderBoundToTheServiceCollection()
    {
        var services = new ServiceCollection();
        var builders = new List<OpenNettyMqttBuilder>();

        CreateBuilder(services).AddMqttIntegration(builders.Add);

        var builder = Assert.Single(builders);
        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void AddMqttIntegrationWithConfiguration_RegistersTheMqttServices()
    {
        var services = new ServiceCollection();

        CreateBuilder(services).AddMqttIntegration(static _ => { });

        Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IOpenNettyMqttWorker));
        Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(MqttFactory));
    }

    [Fact]
    public void AddMqttIntegrationWithConfiguration_ConfigurationIsAppliedToTheOptions()
    {
        var services = new ServiceCollection();
        var clientOptions = new MqttClientOptions();

        CreateBuilder(services).AddMqttIntegration(builder => builder
            .SetRootTopic("custom")
            .Configure(options =>
            {
                options.ClientOptions = clientOptions;
                options.DisableHomeAssistantDiscovery = true;
            }));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OpenNettyMqttOptions>>().Value;
        Assert.Equal("custom", options.RootTopic);
        Assert.Same(clientOptions, options.ClientOptions);
        Assert.True(options.DisableHomeAssistantDiscovery);
    }

    [Fact]
    public void AddMqttIntegrationWithConfiguration_ConfigurationExceptionIsPropagated()
    {
        var builder = CreateBuilder(new ServiceCollection());

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddMqttIntegration(static _ => throw new InvalidOperationException("failure")));

        Assert.Equal("failure", exception.Message);
    }

    private static OpenNettyBuilder CreateBuilder(IServiceCollection services) => new(services);

    private static int CountMqttRegistrations(IServiceCollection services)
    {
        var assembly = typeof(OpenNettyMqttOptions).Assembly;

        return services.Count(descriptor => descriptor.ServiceType.Assembly == assembly || descriptor.ImplementationType?.Assembly == assembly);
    }
}
