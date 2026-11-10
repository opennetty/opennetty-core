/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Globalization;
using MQTTnet.Client;
using Xunit;

namespace OpenNetty.Mqtt.Tests;

public sealed class OpenNettyMqttOptionsTests
{
    [Fact]
    public void ClientOptions_NewInstance_IsNull()
    {
        var options = new OpenNettyMqttOptions();

        Assert.Null(options.ClientOptions);
    }

    [Fact]
    public void ClientOptions_SetValue_IsReturned()
    {
        var clientOptions = new MqttClientOptions();
        var options = new OpenNettyMqttOptions { ClientOptions = clientOptions };

        Assert.Same(clientOptions, options.ClientOptions);
    }

    [Fact]
    public void ClientOptions_SetNull_ResetsTheValue()
    {
        var options = new OpenNettyMqttOptions { ClientOptions = new MqttClientOptions() };

        options.ClientOptions = null;

        Assert.Null(options.ClientOptions);
    }

    [Fact]
    public void DisableHomeAssistantDiscovery_NewInstance_IsFalse()
    {
        var options = new OpenNettyMqttOptions();

        Assert.False(options.DisableHomeAssistantDiscovery);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisableHomeAssistantDiscovery_SetValue_IsReturned(bool value)
    {
        var options = new OpenNettyMqttOptions { DisableHomeAssistantDiscovery = value };

        Assert.Equal(value, options.DisableHomeAssistantDiscovery);
    }

    [Fact]
    public void HomeAssistantDiscoveryUICulture_NewInstance_IsUnset()
    {
        var options = new OpenNettyMqttOptions();

        Assert.Null(options.HomeAssistantDiscoveryUICulture);
    }

    [Fact]
    public void HomeAssistantDiscoveryUICulture_SetValue_IsReturned()
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var options = new OpenNettyMqttOptions { HomeAssistantDiscoveryUICulture = culture };

        Assert.Same(culture, options.HomeAssistantDiscoveryUICulture);
    }

    [Fact]
    public void HomeAssistantDiscoveryRootTopic_NewInstance_IsUnset()
    {
        var options = new OpenNettyMqttOptions();

        Assert.Null(options.HomeAssistantDiscoveryRootTopic);
    }

    [Theory]
    [InlineData("homeassistant")]
    [InlineData("custom/discovery")]
    public void HomeAssistantDiscoveryRootTopic_SetValue_IsReturned(string value)
    {
        var options = new OpenNettyMqttOptions { HomeAssistantDiscoveryRootTopic = value };

        Assert.Equal(value, options.HomeAssistantDiscoveryRootTopic);
    }

    [Fact]
    public void RootTopic_NewInstance_IsUnset()
    {
        var options = new OpenNettyMqttOptions();

        Assert.Null(options.RootTopic);
    }

    [Theory]
    [InlineData("opennetty")]
    [InlineData("home/opennetty")]
    public void RootTopic_SetValue_IsReturned(string value)
    {
        var options = new OpenNettyMqttOptions { RootTopic = value };

        Assert.Equal(value, options.RootTopic);
    }
}
