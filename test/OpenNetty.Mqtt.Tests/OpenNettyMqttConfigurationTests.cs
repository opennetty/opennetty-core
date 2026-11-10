/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Globalization;
using Xunit;

namespace OpenNetty.Mqtt.Tests;

public sealed class OpenNettyMqttConfigurationTests
{
    [Fact]
    public void PostConfigure_NullOptions_ThrowsArgumentNullException()
    {
        var configuration = new OpenNettyMqttConfiguration();

        var exception = Assert.Throws<ArgumentNullException>(() => configuration.PostConfigure(name: null, options: null!));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void PostConfigure_UnsetUICulture_UsesTheCurrentUICulture()
    {
        var configuration = new OpenNettyMqttConfiguration();
        var options = new OpenNettyMqttOptions();

        configuration.PostConfigure(name: null, options);

        Assert.Same(CultureInfo.CurrentUICulture, options.HomeAssistantDiscoveryUICulture);
    }

    [Fact]
    public void PostConfigure_ExplicitUICulture_IsPreserved()
    {
        var configuration = new OpenNettyMqttConfiguration();
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var options = new OpenNettyMqttOptions { HomeAssistantDiscoveryUICulture = culture };

        configuration.PostConfigure(name: null, options);

        Assert.Same(culture, options.HomeAssistantDiscoveryUICulture);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void PostConfigure_MissingDiscoveryRootTopic_UsesTheDefaultTopic(string? topic)
    {
        var configuration = new OpenNettyMqttConfiguration();
        var options = new OpenNettyMqttOptions { HomeAssistantDiscoveryRootTopic = topic! };

        configuration.PostConfigure(name: null, options);

        Assert.Equal("homeassistant", options.HomeAssistantDiscoveryRootTopic);
    }

    [Theory]
    [InlineData("custom")]
    [InlineData("custom/discovery")]
    public void PostConfigure_ExplicitDiscoveryRootTopic_IsPreserved(string topic)
    {
        var configuration = new OpenNettyMqttConfiguration();
        var options = new OpenNettyMqttOptions { HomeAssistantDiscoveryRootTopic = topic };

        configuration.PostConfigure(name: null, options);

        Assert.Equal(topic, options.HomeAssistantDiscoveryRootTopic);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void PostConfigure_MissingRootTopic_UsesTheDefaultTopic(string? topic)
    {
        var configuration = new OpenNettyMqttConfiguration();
        var options = new OpenNettyMqttOptions { RootTopic = topic! };

        configuration.PostConfigure(name: null, options);

        Assert.Equal("opennetty", options.RootTopic);
    }

    [Theory]
    [InlineData("custom")]
    [InlineData("home/opennetty")]
    public void PostConfigure_ExplicitRootTopic_IsPreserved(string topic)
    {
        var configuration = new OpenNettyMqttConfiguration();
        var options = new OpenNettyMqttOptions { RootTopic = topic };

        configuration.PostConfigure(name: null, options);

        Assert.Equal(topic, options.RootTopic);
    }

    [Fact]
    public void PostConfigure_DoesNotAlterTheOtherOptions()
    {
        var configuration = new OpenNettyMqttConfiguration();
        var options = new OpenNettyMqttOptions { DisableHomeAssistantDiscovery = true };

        configuration.PostConfigure(name: null, options);

        Assert.True(options.DisableHomeAssistantDiscovery);
        Assert.Null(options.ClientOptions);
    }

    [Fact]
    public void PostConfigure_CalledTwice_IsIdempotent()
    {
        var configuration = new OpenNettyMqttConfiguration();
        var options = new OpenNettyMqttOptions();

        configuration.PostConfigure(name: null, options);
        configuration.PostConfigure(name: null, options);

        Assert.Equal("homeassistant", options.HomeAssistantDiscoveryRootTopic);
        Assert.Equal("opennetty", options.RootTopic);
    }

    [Fact]
    public void Validate_NullOpenNettyOptions_ThrowsArgumentNullException()
    {
        var configuration = new OpenNettyMqttConfiguration();

        var exception = Assert.Throws<ArgumentNullException>(() => configuration.Validate(name: null, (OpenNettyOptions) null!));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void Validate_OpenNettyOptionsWithoutEndpoint_Succeeds()
    {
        var configuration = new OpenNettyMqttConfiguration();

        var result = configuration.Validate(name: null, new OpenNettyOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_EndpointWithoutSettings_Succeeds()
    {
        var configuration = new OpenNettyMqttConfiguration();
        var options = new OpenNettyOptions { Endpoints = { CreateEndpoint() } };

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("living_room")]
    [InlineData("home/living room/light")]
    [InlineData("")]
    public void Validate_EndpointWithValidMqttTopic_Succeeds(string topic)
    {
        var configuration = new OpenNettyMqttConfiguration();
        var options = new OpenNettyOptions { Endpoints = { CreateEndpoint((OpenNettySettings.MqttTopic, topic)) } };

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("home/+/light")]
    [InlineData("home/*")]
    [InlineData("+")]
    [InlineData("*")]
    public void Validate_EndpointWithWildcardMqttTopic_Fails(string topic)
    {
        var configuration = new OpenNettyMqttConfiguration();
        var options = new OpenNettyOptions { Endpoints = { CreateEndpoint((OpenNettySettings.MqttTopic, topic)) } };

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Equal(SR.FormatID2005(topic), Assert.Single(result.Failures));
    }

    [Theory]
    [InlineData("living_room")]
    [InlineData("Living-Room-1")]
    [InlineData("_")]
    [InlineData("-")]
    [InlineData("")]
    public void Validate_EndpointWithValidHomeAssistantObjectId_Succeeds(string identifier)
    {
        var configuration = new OpenNettyMqttConfiguration();
        var options = new OpenNettyOptions { Endpoints = { CreateEndpoint((OpenNettySettings.HomeAssistantObjectId, identifier)) } };

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("living room")]
    [InlineData("living.room")]
    [InlineData("living/room")]
    [InlineData("salon_é")]
    [InlineData("light+1")]
    public void Validate_EndpointWithInvalidHomeAssistantObjectId_Fails(string identifier)
    {
        var configuration = new OpenNettyMqttConfiguration();
        var options = new OpenNettyOptions { Endpoints = { CreateEndpoint((OpenNettySettings.HomeAssistantObjectId, identifier)) } };

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Equal(SR.FormatID2018(identifier), Assert.Single(result.Failures));
    }

    [Fact]
    public void Validate_EndpointWithInvalidTopicAndObjectId_ReportsBothFailures()
    {
        var configuration = new OpenNettyMqttConfiguration();
        var options = new OpenNettyOptions
        {
            Endpoints =
            {
                CreateEndpoint(
                    (OpenNettySettings.MqttTopic, "home/+"),
                    (OpenNettySettings.HomeAssistantObjectId, "not valid"))
            }
        };

        var result = configuration.Validate(name: null, options);

        Assert.Equal([SR.FormatID2005("home/+"), SR.FormatID2018("not valid")], result.Failures, StringComparer.Ordinal);
    }

    [Fact]
    public void Validate_MultipleInvalidEndpoints_ReportsTheFailuresOfAllEndpoints()
    {
        var configuration = new OpenNettyMqttConfiguration();
        var options = new OpenNettyOptions
        {
            Endpoints =
            {
                CreateEndpoint((OpenNettySettings.MqttTopic, "first/*")),
                CreateEndpoint((OpenNettySettings.MqttTopic, "valid")),
                CreateEndpoint((OpenNettySettings.HomeAssistantObjectId, "second id"))
            }
        };

        var result = configuration.Validate(name: null, options);

        Assert.Equal([SR.FormatID2005("first/*"), SR.FormatID2018("second id")], result.Failures, StringComparer.Ordinal);
    }

    [Fact]
    public void Validate_OpenNettyOptions_IgnoresTheOptionsName()
    {
        var configuration = new OpenNettyMqttConfiguration();
        var options = new OpenNettyOptions { Endpoints = { CreateEndpoint((OpenNettySettings.MqttTopic, "+")) } };

        var result = configuration.Validate("name", options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_NullOpenNettyMqttOptions_ThrowsArgumentNullException()
    {
        var configuration = new OpenNettyMqttConfiguration();

        var exception = Assert.Throws<ArgumentNullException>(() => configuration.Validate(name: null, (OpenNettyMqttOptions) null!));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void Validate_DefaultMqttOptions_Succeeds()
    {
        var configuration = new OpenNettyMqttConfiguration();

        var result = configuration.Validate(name: null, CreateOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_PostConfiguredMqttOptions_Succeeds()
    {
        var configuration = new OpenNettyMqttConfiguration();
        var options = new OpenNettyMqttOptions();
        configuration.PostConfigure(name: null, options);

        var result = configuration.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("open+netty")]
    [InlineData("open*netty")]
    [InlineData("+")]
    [InlineData("*")]
    [InlineData("home/+/opennetty")]
    public void Validate_RootTopicWithWildcard_Fails(string topic)
    {
        var configuration = new OpenNettyMqttConfiguration();

        var result = configuration.Validate(name: null, CreateOptions(rootTopic: topic));

        Assert.True(result.Failed);
        Assert.Equal(SR.GetResourceString(SR.ID2001), Assert.Single(result.Failures));
    }

    [Theory]
    [InlineData("opennetty/")]
    [InlineData("home/opennetty/")]
    [InlineData("/")]
    public void Validate_RootTopicEndingWithSlash_Fails(string topic)
    {
        var configuration = new OpenNettyMqttConfiguration();

        var result = configuration.Validate(name: null, CreateOptions(rootTopic: topic));

        Assert.True(result.Failed);
        Assert.Equal(SR.GetResourceString(SR.ID2007), Assert.Single(result.Failures));
    }

    [Theory]
    [InlineData("opennetty")]
    [InlineData("/opennetty")]
    [InlineData("home/opennetty")]
    public void Validate_RootTopicNotEndingWithSlash_Succeeds(string topic)
    {
        var configuration = new OpenNettyMqttConfiguration();

        var result = configuration.Validate(name: null, CreateOptions(rootTopic: topic));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("homeassistant/")]
    [InlineData("home/assistant/")]
    [InlineData("/")]
    public void Validate_DiscoveryRootTopicEndingWithSlash_Fails(string topic)
    {
        var configuration = new OpenNettyMqttConfiguration();

        var result = configuration.Validate(name: null, CreateOptions(discoveryRootTopic: topic));

        Assert.True(result.Failed);
        Assert.Equal(SR.GetResourceString(SR.ID2008), Assert.Single(result.Failures));
    }

    [Theory]
    [InlineData("homeassistant", "homeassistant")]
    [InlineData("opennetty", "OPENNETTY")]
    [InlineData("Home/OpenNetty", "home/opennetty")]
    public void Validate_SameRootAndDiscoveryRootTopics_Fails(string rootTopic, string discoveryRootTopic)
    {
        var configuration = new OpenNettyMqttConfiguration();

        var result = configuration.Validate(name: null, CreateOptions(rootTopic, discoveryRootTopic));

        Assert.True(result.Failed);
        Assert.Equal(SR.GetResourceString(SR.ID2009), Assert.Single(result.Failures));
    }

    [Theory]
    [InlineData("opennetty", "opennetty2")]
    [InlineData("home", "home/assistant")]
    public void Validate_DifferentRootAndDiscoveryRootTopics_Succeeds(string rootTopic, string discoveryRootTopic)
    {
        var configuration = new OpenNettyMqttConfiguration();

        var result = configuration.Validate(name: null, CreateOptions(rootTopic, discoveryRootTopic));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_MultipleInvalidTopics_ReportsAllFailuresInOrder()
    {
        var configuration = new OpenNettyMqttConfiguration();

        var result = configuration.Validate(name: null, CreateOptions(rootTopic: "+/", discoveryRootTopic: "+/"));

        Assert.Equal(
        [
            SR.GetResourceString(SR.ID2001),
            SR.GetResourceString(SR.ID2007),
            SR.GetResourceString(SR.ID2008),
            SR.GetResourceString(SR.ID2009)
        ], result.Failures, StringComparer.Ordinal);
    }

    [Fact]
    public void Validate_OpenNettyMqttOptions_IgnoresTheOptionsName()
    {
        var configuration = new OpenNettyMqttConfiguration();

        var result = configuration.Validate("name", CreateOptions(rootTopic: "+"));

        Assert.True(result.Failed);
    }

    private static OpenNettyMqttOptions CreateOptions(string rootTopic = "opennetty", string discoveryRootTopic = "homeassistant")
        => new() { RootTopic = rootTopic, HomeAssistantDiscoveryRootTopic = discoveryRootTopic };

    private static OpenNettyEndpoint CreateEndpoint(params (OpenNettySetting Setting, string Value)[] settings) => new()
    {
        Name = "Endpoint",
        Protocol = OpenNettyProtocol.Scs,
        Settings = settings.ToImmutableDictionary(static setting => setting.Setting, static setting => setting.Value)
    };
}
