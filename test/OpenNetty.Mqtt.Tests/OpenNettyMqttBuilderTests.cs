/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using MQTTnet.Client;
using MQTTnet.Formatter;
using NSubstitute;
using Xunit;

namespace OpenNetty.Mqtt.Tests;

public sealed class OpenNettyMqttBuilderTests : IDisposable
{
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "certificates", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Constructor_NullServices_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new OpenNettyMqttBuilder(null!));

        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void Services_ReturnsTheServiceCollectionPassedToTheConstructor()
    {
        var services = new ServiceCollection();

        var builder = new OpenNettyMqttBuilder(services);

        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void ValidateOnStart_ReturnsTheSameBuilder()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        Assert.Same(builder, builder.ValidateOnStart());
    }

    [Fact]
    public void ValidateOnStart_InvalidOptions_ThrowsWhenStartupValidationIsExecuted()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateValidator(ValidateOptionsResult.Fail("The options are invalid.")));
        new OpenNettyMqttBuilder(services).ValidateOnStart();
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Equal("The options are invalid.", Assert.Single(exception.Failures));
    }

    [Fact]
    public void ValidateOnStart_ValidOptions_DoesNotThrowWhenStartupValidationIsExecuted()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateValidator(ValidateOptionsResult.Success));
        new OpenNettyMqttBuilder(services).ValidateOnStart();
        using var provider = services.BuildServiceProvider();

        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateOnStart_NotCalled_DoesNotRegisterStartupValidation()
    {
        var services = new ServiceCollection();
        _ = new OpenNettyMqttBuilder(services).Configure(static _ => { });
        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IStartupValidator>());
    }

    [Fact]
    public void Configure_NullDelegate_ThrowsArgumentNullException()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentNullException>(() => builder.Configure(null!));

        Assert.Equal("configuration", exception.ParamName);
    }

    [Fact]
    public void Configure_ReturnsTheSameBuilder()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        Assert.Same(builder, builder.Configure(static _ => { }));
    }

    [Fact]
    public void Configure_Delegate_IsInvokedWhenOptionsAreResolved()
    {
        var services = new ServiceCollection();
        var options = default(OpenNettyMqttOptions);
        var builder = new OpenNettyMqttBuilder(services).Configure(configured => options = configured);

        Assert.Null(options);
        var resolved = GetOptions(builder.Services);

        Assert.Same(resolved, options);
    }

    [Fact]
    public void Configure_CalledMultipleTimes_InvokesAllDelegatesInOrder()
    {
        var services = new ServiceCollection();
        new OpenNettyMqttBuilder(services)
            .Configure(options => options.RootTopic = "first")
            .Configure(options => options.RootTopic += "-second")
            .Configure(options => options.DisableHomeAssistantDiscovery = true);

        var options = GetOptions(services);

        Assert.Equal("first-second", options.RootTopic);
        Assert.True(options.DisableHomeAssistantDiscovery);
    }

    [Fact]
    public void SetClientOptions_NullDelegate_ThrowsArgumentNullException()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentNullException>(() => builder.SetClientOptions(null!));

        Assert.Equal("configuration", exception.ParamName);
    }

    [Fact]
    public void SetClientOptions_ReturnsTheSameBuilder()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        Assert.Same(builder, builder.SetClientOptions(static _ => { }));
    }

    [Fact]
    public void SetClientOptions_Delegate_IsInvokedImmediately()
    {
        var invoked = false;
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        builder.SetClientOptions(_ => invoked = true);

        Assert.True(invoked);
    }

    [Fact]
    public void SetClientOptions_Delegate_ConfiguresTheClientOptions()
    {
        var services = new ServiceCollection();

        new OpenNettyMqttBuilder(services).SetClientOptions(static builder => builder
            .WithTcpServer("mqtt.example.com", 1884)
            .WithClientId("client"));

        var clientOptions = Assert.IsType<MqttClientOptions>(GetOptions(services).ClientOptions);
        var endpoint = GetEndpoint(GetOptions(services));
        Assert.Equal("mqtt.example.com", endpoint.Host);
        Assert.Equal(1884, endpoint.Port);
        Assert.Equal("client", clientOptions.ClientId);
    }

    [Fact]
    public void SetClientOptions_CalledTwice_UsesTheLastClientOptions()
    {
        var services = new ServiceCollection();

        new OpenNettyMqttBuilder(services)
            .SetClientOptions(static builder => builder.WithTcpServer("first.example.com").WithClientId("first"))
            .SetClientOptions(static builder => builder.WithTcpServer("second.example.com").WithClientId("second"));

        var options = GetOptions(services);
        Assert.Equal("second", options.ClientOptions!.ClientId);
        Assert.Equal("second.example.com", GetEndpoint(options).Host);
    }

    [Fact]
    public void SetRootTopic_NullTopic_ThrowsArgumentNullException()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentNullException>(() => builder.SetRootTopic(null!));

        Assert.Equal("topic", exception.ParamName);
    }

    [Fact]
    public void SetRootTopic_EmptyTopic_ThrowsArgumentException()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentException>(() => builder.SetRootTopic(string.Empty));

        Assert.Equal("topic", exception.ParamName);
    }

    [Fact]
    public void SetRootTopic_ReturnsTheSameBuilder()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        Assert.Same(builder, builder.SetRootTopic("opennetty"));
    }

    [Theory]
    [InlineData("custom")]
    [InlineData("home/opennetty")]
    public void SetRootTopic_Topic_IsAppliedToTheOptions(string topic)
    {
        var services = new ServiceCollection();

        new OpenNettyMqttBuilder(services).SetRootTopic(topic);

        Assert.Equal(topic, GetOptions(services).RootTopic);
    }

    [Fact]
    public void SetRootTopic_CalledTwice_UsesTheLastTopic()
    {
        var services = new ServiceCollection();

        new OpenNettyMqttBuilder(services).SetRootTopic("first").SetRootTopic("second");

        Assert.Equal("second", GetOptions(services).RootTopic);
    }

    [Fact]
    public void ImportFromXmlConfiguration_MissingFile_ThrowsFileNotFoundException()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());
        var existing = CreateFile(Configuration(Mqtt()));
        var missing = CreateFile(content: null);

        var exception = Assert.Throws<FileNotFoundException>(() => builder.ImportFromXmlConfiguration(existing, missing));

        Assert.Equal(SR.GetResourceString(SR.ID0070), exception.Message);
    }

    [Fact]
    public void ImportFromXmlConfiguration_NoFile_ThrowsInvalidOperationException()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.ImportFromXmlConfiguration(ImmutableArray<IFileInfo>.Empty));

        Assert.Equal(SR.FormatID0090("Mqtt"), exception.Message);
    }

    [Fact]
    public void ImportFromXmlConfiguration_Files_ImportsTheContentOfAllFiles()
    {
        var services = new ServiceCollection();
        var first = CreateFile(Configuration());
        var second = CreateFile(Configuration("<Mqtt Server=\"mqtt.example.com\" RootTopic=\"imported\" />"));

        new OpenNettyMqttBuilder(services).ImportFromXmlConfiguration(first, second);

        var options = GetOptions(services);
        Assert.Equal("imported", options.RootTopic);
        Assert.Equal("mqtt.example.com", GetEndpoint(options).Host);
    }

    [Fact]
    public void ImportFromXmlConfiguration_Files_AreDisposedAfterBeingRead()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(Configuration(Mqtt())));
        var file = Substitute.For<IFileInfo>();
        file.Exists.Returns(true);
        file.CreateReadStream().Returns(stream);

        new OpenNettyMqttBuilder(new ServiceCollection()).ImportFromXmlConfiguration(file);

        Assert.False(stream.CanRead);
    }

    [Fact]
    public void ImportFromXmlConfiguration_Files_ReturnsTheSameBuilder()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        Assert.Same(builder, builder.ImportFromXmlConfiguration(CreateFile(Configuration(Mqtt()))));
    }

    [Fact]
    public void ImportFromXmlConfiguration_MqttNodeInMultipleFiles_ThrowsInvalidOperationException()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        var exception = Assert.Throws<InvalidOperationException>(() => builder.ImportFromXmlConfiguration(
            CreateFile(Configuration(Mqtt())), CreateFile(Configuration(Mqtt()))));

        Assert.Equal(SR.FormatID0123("Mqtt"), exception.Message);
    }

    [Theory]
    [InlineData("<Root />")]
    [InlineData("<configuration />")]
    public void ImportFromXmlConfiguration_DocumentWithUnexpectedRoot_ThrowsInvalidOperationException(string xml)
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        var exception = Assert.Throws<InvalidOperationException>(() => builder.ImportFromXmlConfiguration(XDocument.Parse(xml)));

        Assert.Equal(SR.FormatID0071("Configuration"), exception.Message);
    }

    [Fact]
    public void ImportFromXmlConfiguration_DocumentWithoutRoot_ThrowsInvalidOperationException()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        var exception = Assert.Throws<InvalidOperationException>(() => builder.ImportFromXmlConfiguration(new XDocument()));

        Assert.Equal(SR.FormatID0071("Configuration"), exception.Message);
    }

    [Fact]
    public void ImportFromXmlConfiguration_OneInvalidDocument_ThrowsInvalidOperationException()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.ImportFromXmlConfiguration(XDocument.Parse(Configuration(Mqtt())), XDocument.Parse("<Root />")));

        Assert.Equal(SR.FormatID0071("Configuration"), exception.Message);
    }

    [Fact]
    public void ImportFromXmlConfiguration_NoDocument_ThrowsInvalidOperationException()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.ImportFromXmlConfiguration(ImmutableArray<XDocument>.Empty));

        Assert.Equal(SR.FormatID0090("Mqtt"), exception.Message);
    }

    [Fact]
    public void ImportFromXmlConfiguration_DocumentWithoutMqttNode_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0090("Mqtt"), Configuration());
    }

    [Fact]
    public void ImportFromXmlConfiguration_MqttNodeNotDirectlyUnderTheRoot_IsIgnored()
    {
        AssertInvalid(SR.FormatID0090("Mqtt"), Configuration("<Other><Mqtt Server=\"localhost\" /></Other>"));
    }

    [Fact]
    public void ImportFromXmlConfiguration_MqttNodeInMultipleDocuments_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0123("Mqtt"), Configuration(Mqtt()), Configuration(Mqtt()));
    }

    [Fact]
    public void ImportFromXmlConfiguration_MqttNodeInOneOfSeveralDocuments_IsImported()
    {
        var options = Import(Configuration(), Configuration(Mqtt("RootTopic=\"custom\"")), Configuration());

        Assert.Equal("custom", options.RootTopic);
    }

    [Fact]
    public void ImportFromXmlConfiguration_Documents_ReturnsTheSameBuilder()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        Assert.Same(builder, builder.ImportFromXmlConfiguration(XDocument.Parse(Configuration(Mqtt()))));
    }

    [Fact]
    public void ImportFromXmlConfiguration_MissingServer_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0091("Server"), Configuration("<Mqtt Port=\"1883\" />"));
    }

    [Fact]
    public void ImportFromXmlConfiguration_Server_IsUsedAsTheTcpHost()
    {
        var options = Import(Configuration("<Mqtt Server=\"mqtt.example.com\" />"));

        Assert.Equal("mqtt.example.com", GetEndpoint(options).Host);
    }

    [Fact]
    public void ImportFromXmlConfiguration_Port_IsUsedAsTheTcpPort()
    {
        var options = Import(Configuration(Mqtt("Port=\"8883\"")));

        Assert.Equal(8883, GetEndpoint(options).Port);
    }

    [Fact]
    public void ImportFromXmlConfiguration_MissingPort_UsesTheDefaultMqttPort()
    {
        var options = Import(Configuration(Mqtt()));

        Assert.Equal(1883, GetEndpoint(options).Port);
    }

    [Fact]
    public void ImportFromXmlConfiguration_InvalidPort_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Import(Configuration(Mqtt("Port=\"invalid\""))));
    }

    [Fact]
    public void ImportFromXmlConfiguration_Always_UsesMqttVersion5()
    {
        var options = Import(Configuration(Mqtt()));

        Assert.Equal(MqttProtocolVersion.V500, options.ClientOptions!.ProtocolVersion);
    }

    [Fact]
    public void ImportFromXmlConfiguration_UsernameAndPassword_AreUsedAsCredentials()
    {
        var options = Import(Configuration(Mqtt("Username=\"user\" Password=\"secret\"")));

        var clientOptions = options.ClientOptions!;
        Assert.NotNull(clientOptions.Credentials);
        Assert.Equal("user", clientOptions.Credentials.GetUserName(clientOptions));
        Assert.Equal("secret", Encoding.UTF8.GetString(clientOptions.Credentials.GetPassword(clientOptions)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Username=\"user\"")]
    [InlineData("Password=\"secret\"")]
    [InlineData("Username=\"\" Password=\"secret\"")]
    [InlineData("Username=\"user\" Password=\"\"")]
    [InlineData("Username=\"\" Password=\"\"")]
    public void ImportFromXmlConfiguration_IncompleteCredentials_AreIgnored(string attributes)
    {
        var options = Import(Configuration(Mqtt(attributes)));

        Assert.Null(options.ClientOptions!.Credentials);
    }

    [Fact]
    public void ImportFromXmlConfiguration_ClientId_IsUsedAsTheClientIdentifier()
    {
        var options = Import(Configuration(Mqtt("ClientId=\"my-client\"")));

        Assert.Equal("my-client", options.ClientOptions!.ClientId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ClientId=\"\"")]
    public void ImportFromXmlConfiguration_MissingClientId_UsesTheDefaultClientIdentifier(string attributes)
    {
        var options = Import(Configuration(Mqtt(attributes)));

        Assert.Equal(new MqttClientOptions().ClientId.Length, options.ClientOptions!.ClientId.Length);
        Assert.False(string.IsNullOrEmpty(options.ClientOptions.ClientId));
    }

    [Fact]
    public void ImportFromXmlConfiguration_NoCertificateAuthority_DisablesTls()
    {
        var options = Import(Configuration(Mqtt()));

        Assert.False(GetTlsOptions(options).UseTls);
    }

    [Fact]
    public void ImportFromXmlConfiguration_EmptyCertificateAuthority_DisablesTls()
    {
        var options = Import(Configuration(Mqtt("TlsServerCertificateAuthorityFile=\"\"")));

        Assert.False(GetTlsOptions(options).UseTls);
    }

    [Fact]
    public void ImportFromXmlConfiguration_NoCertificateAuthority_IgnoresTheOtherTlsAttributes()
    {
        var options = Import(Configuration(Mqtt("TlsServerTargetHost=\"host\" TlsClientCertificateFile=\"missing.pem\"")));

        var tls = GetTlsOptions(options);
        Assert.False(tls.UseTls);
        Assert.Null(tls.ClientCertificatesProvider);
    }

    [Fact]
    public void ImportFromXmlConfiguration_CertificateAuthority_EnablesTlsWithTheTrustChain()
    {
        var authority = CreateCertificateFile("authority");

        var options = Import(Configuration(Mqtt($"TlsServerCertificateAuthorityFile=\"{authority.Certificate}\"")));

        var tls = GetTlsOptions(options);
        Assert.True(tls.UseTls);
        Assert.Equal(X509RevocationMode.NoCheck, tls.RevocationMode);
        Assert.Single(tls.TrustChain);
        Assert.Null(tls.ClientCertificatesProvider);
    }

    [Fact]
    public void ImportFromXmlConfiguration_CertificateAuthorityWithTargetHost_UsesTheTargetHost()
    {
        var authority = CreateCertificateFile("authority");

        var options = Import(Configuration(Mqtt(
            $"TlsServerCertificateAuthorityFile=\"{authority.Certificate}\" TlsServerTargetHost=\"broker.local\"")));

        Assert.Equal("broker.local", GetTlsOptions(options).TargetHost);
    }

    [Theory]
    [InlineData("")]
    [InlineData("TlsServerTargetHost=\"\"")]
    public void ImportFromXmlConfiguration_CertificateAuthorityWithoutTargetHost_DoesNotOverrideTheTargetHost(string attributes)
    {
        var authority = CreateCertificateFile("authority");

        var options = Import(Configuration(Mqtt($"TlsServerCertificateAuthorityFile=\"{authority.Certificate}\" {attributes}")));

        Assert.Null(GetTlsOptions(options).TargetHost);
    }

    [Fact]
    public void ImportFromXmlConfiguration_CertificateAuthorityWithClientCertificate_UsesTheClientCertificate()
    {
        var authority = CreateCertificateFile("authority");
        var client = CreateCertificateFile("client");

        var options = Import(Configuration(Mqtt(
            $"TlsServerCertificateAuthorityFile=\"{authority.Certificate}\" " +
            $"TlsClientCertificateFile=\"{client.Certificate}\" TlsClientCertificatePrivateKeyFile=\"{client.PrivateKey}\"")));

        var tls = GetTlsOptions(options);
        Assert.True(tls.UseTls);
        var certificate = Assert.Single(tls.ClientCertificatesProvider.GetCertificates().Cast<X509Certificate2>());
        Assert.True(certificate.HasPrivateKey);
        Assert.Equal("CN=client", certificate.Subject);
    }

    [Fact]
    public void ImportFromXmlConfiguration_ClientCertificateWithoutPrivateKey_ThrowsInvalidOperationException()
    {
        var authority = CreateCertificateFile("authority");
        var client = CreateCertificateFile("client");

        var exception = Assert.Throws<InvalidOperationException>(() => Import(Configuration(Mqtt(
            $"TlsServerCertificateAuthorityFile=\"{authority.Certificate}\" TlsClientCertificateFile=\"{client.Certificate}\""))));

        Assert.Equal(SR.GetResourceString(SR.ID0098), exception.Message);
    }

    [Fact]
    public void ImportFromXmlConfiguration_ClientCertificateWithEmptyPrivateKey_ThrowsInvalidOperationException()
    {
        var authority = CreateCertificateFile("authority");
        var client = CreateCertificateFile("client");

        var exception = Assert.Throws<InvalidOperationException>(() => Import(Configuration(Mqtt(
            $"TlsServerCertificateAuthorityFile=\"{authority.Certificate}\" " +
            $"TlsClientCertificateFile=\"{client.Certificate}\" TlsClientCertificatePrivateKeyFile=\"\""))));

        Assert.Equal(SR.GetResourceString(SR.ID0098), exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("TlsClientCertificateFile=\"\"")]
    [InlineData("TlsClientCertificatePrivateKeyFile=\"key.pem\"")]
    public void ImportFromXmlConfiguration_NoClientCertificate_DoesNotConfigureClientCertificates(string attributes)
    {
        var authority = CreateCertificateFile("authority");

        var options = Import(Configuration(Mqtt($"TlsServerCertificateAuthorityFile=\"{authority.Certificate}\" {attributes}")));

        var tls = GetTlsOptions(options);
        Assert.True(tls.UseTls);
        Assert.Null(tls.ClientCertificatesProvider);
    }

    [Fact]
    public void ImportFromXmlConfiguration_MissingCertificateAuthorityFile_ThrowsException()
    {
        var path = Path.Combine(_directory, "missing.pem");

        Assert.ThrowsAny<Exception>(() => Import(Configuration(Mqtt($"TlsServerCertificateAuthorityFile=\"{path}\""))));
    }

    [Fact]
    public void ImportFromXmlConfiguration_NoDiscoveryAttribute_KeepsHomeAssistantDiscoveryEnabled()
    {
        var options = Import(Configuration(Mqtt()));

        Assert.False(options.DisableHomeAssistantDiscovery);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void ImportFromXmlConfiguration_DisableHomeAssistantDiscovery_IsApplied(string value, bool expected)
    {
        var options = Import(Configuration(Mqtt($"DisableHomeAssistantDiscovery=\"{value}\"")));

        Assert.Equal(expected, options.DisableHomeAssistantDiscovery);
    }

    [Fact]
    public void ImportFromXmlConfiguration_InvalidDisableHomeAssistantDiscovery_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Import(Configuration(Mqtt("DisableHomeAssistantDiscovery=\"maybe\""))));
    }

    [Fact]
    public void ImportFromXmlConfiguration_MissingDisableHomeAssistantDiscovery_ResetsThePreviousValue()
    {
        var services = new ServiceCollection();
        new OpenNettyMqttBuilder(services)
            .Configure(static options => options.DisableHomeAssistantDiscovery = true)
            .ImportFromXmlConfiguration(XDocument.Parse(Configuration(Mqtt())));

        Assert.False(GetOptions(services).DisableHomeAssistantDiscovery);
    }

    [Fact]
    public void ImportFromXmlConfiguration_RootTopic_IsApplied()
    {
        var options = Import(Configuration(Mqtt("RootTopic=\"custom/root\"")));

        Assert.Equal("custom/root", options.RootTopic);
    }

    [Theory]
    [InlineData("")]
    [InlineData("RootTopic=\"\"")]
    public void ImportFromXmlConfiguration_MissingRootTopic_PreservesThePreviousValue(string attributes)
    {
        var services = new ServiceCollection();
        new OpenNettyMqttBuilder(services)
            .SetRootTopic("previous")
            .ImportFromXmlConfiguration(XDocument.Parse(Configuration(Mqtt(attributes))));

        Assert.Equal("previous", GetOptions(services).RootTopic);
    }

    [Fact]
    public void ImportFromXmlConfiguration_HomeAssistantDiscoveryRootTopic_IsApplied()
    {
        var options = Import(Configuration(Mqtt("HomeAssistantDiscoveryRootTopic=\"custom/discovery\"")));

        Assert.Equal("custom/discovery", options.HomeAssistantDiscoveryRootTopic);
    }

    [Theory]
    [InlineData("")]
    [InlineData("HomeAssistantDiscoveryRootTopic=\"\"")]
    public void ImportFromXmlConfiguration_MissingHomeAssistantDiscoveryRootTopic_PreservesThePreviousValue(string attributes)
    {
        var services = new ServiceCollection();
        new OpenNettyMqttBuilder(services)
            .Configure(static options => options.HomeAssistantDiscoveryRootTopic = "previous")
            .ImportFromXmlConfiguration(XDocument.Parse(Configuration(Mqtt(attributes))));

        Assert.Equal("previous", GetOptions(services).HomeAssistantDiscoveryRootTopic);
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    [InlineData("de")]
    public void ImportFromXmlConfiguration_HomeAssistantDiscoveryUICulture_IsApplied(string culture)
    {
        var options = Import(Configuration(Mqtt($"HomeAssistantDiscoveryUICulture=\"{culture}\"")));

        Assert.Equal(CultureInfo.GetCultureInfo(culture), options.HomeAssistantDiscoveryUICulture);
    }

    [Theory]
    [InlineData("")]
    [InlineData("HomeAssistantDiscoveryUICulture=\"\"")]
    public void ImportFromXmlConfiguration_MissingHomeAssistantDiscoveryUICulture_PreservesThePreviousValue(string attributes)
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var services = new ServiceCollection();
        new OpenNettyMqttBuilder(services)
            .Configure(options => options.HomeAssistantDiscoveryUICulture = culture)
            .ImportFromXmlConfiguration(XDocument.Parse(Configuration(Mqtt(attributes))));

        Assert.Same(culture, GetOptions(services).HomeAssistantDiscoveryUICulture);
    }

    [Fact]
    public void ImportFromXmlConfiguration_InvalidHomeAssistantDiscoveryUICulture_ThrowsCultureNotFoundException()
    {
        Assert.Throws<CultureNotFoundException>(() => Import(Configuration(Mqtt("HomeAssistantDiscoveryUICulture=\"not a culture!\""))));
    }

    [Fact]
    public void ImportFromXmlConfiguration_Configuration_OverridesThePreviousClientOptions()
    {
        var services = new ServiceCollection();
        new OpenNettyMqttBuilder(services)
            .SetClientOptions(static builder => builder.WithTcpServer("previous").WithClientId("previous"))
            .ImportFromXmlConfiguration(XDocument.Parse(Configuration(Mqtt("ClientId=\"imported\""))));

        Assert.Equal("imported", GetOptions(services).ClientOptions!.ClientId);
    }

    [Fact]
    public void ImportFromXmlConfiguration_Configuration_CanBeOverriddenByLaterCalls()
    {
        var services = new ServiceCollection();
        new OpenNettyMqttBuilder(services)
            .ImportFromXmlConfiguration(XDocument.Parse(Configuration(Mqtt("RootTopic=\"imported\""))))
            .SetRootTopic("overridden");

        Assert.Equal("overridden", GetOptions(services).RootTopic);
    }

    [Fact]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        Assert.True(builder.Equals((object) builder));
        Assert.False(builder.Equals(new OpenNettyMqttBuilder(new ServiceCollection())));
        Assert.False(builder.Equals(null));
    }

    [Fact]
    public void GetHashCode_SameInstance_IsStable()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        Assert.Equal(builder.GetHashCode(), builder.GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsTheTypeName()
    {
        var builder = new OpenNettyMqttBuilder(new ServiceCollection());

        Assert.Equal(typeof(OpenNettyMqttBuilder).FullName, builder.ToString());
    }

    private static IValidateOptions<OpenNettyMqttOptions> CreateValidator(ValidateOptionsResult result)
    {
        var validator = Substitute.For<IValidateOptions<OpenNettyMqttOptions>>();
        validator.Validate(Arg.Any<string?>(), Arg.Any<OpenNettyMqttOptions>()).Returns(result);

        return validator;
    }

    private static OpenNettyMqttOptions GetOptions(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<OpenNettyMqttOptions>>().Value;
    }

    private static string Configuration(params string[] nodes) => $"<Configuration>{string.Concat(nodes)}</Configuration>";

    private static string Mqtt(string attributes = "") => $"<Mqtt Server=\"localhost\" {attributes} />";

    private static OpenNettyMqttOptions Import(params string[] documents)
    {
        var services = new ServiceCollection();

        new OpenNettyMqttBuilder(services).ImportFromXmlConfiguration([.. documents.Select(static document => XDocument.Parse(document))]);

        return GetOptions(services);
    }

    private static void AssertInvalid(string expectedMessage, params string[] documents)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Import(documents));

        Assert.Equal(expectedMessage, exception.Message);
    }

    private static DnsEndPoint GetEndpoint(OpenNettyMqttOptions options)
    {
        var channel = Assert.IsType<MqttClientTcpOptions>(options.ClientOptions!.ChannelOptions);

        return Assert.IsType<DnsEndPoint>(channel.RemoteEndpoint);
    }

    private static MqttClientTlsOptions GetTlsOptions(OpenNettyMqttOptions options)
    {
        var channel = Assert.IsType<MqttClientTcpOptions>(options.ClientOptions!.ChannelOptions);

        return channel.TlsOptions;
    }

    private (string Certificate, string PrivateKey) CreateCertificateFile(string name)
    {
        Directory.CreateDirectory(_directory);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        var paths = (Certificate: Path.Combine(_directory, name + ".pem"), PrivateKey: Path.Combine(_directory, name + ".key"));
        File.WriteAllText(paths.Certificate, certificate.ExportCertificatePem());
        File.WriteAllText(paths.PrivateKey, key.ExportPkcs8PrivateKeyPem());

        return paths;
    }

    private static IFileInfo CreateFile(string? content)
    {
        var file = Substitute.For<IFileInfo>();
        file.Exists.Returns(content is not null);

        if (content is not null)
        {
            file.CreateReadStream().Returns(_ => new MemoryStream(Encoding.UTF8.GetBytes(content)));
        }

        return file;
    }
}
