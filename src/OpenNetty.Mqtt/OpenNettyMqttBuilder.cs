/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.Extensions.FileProviders;
using MQTTnet.Client;
using MQTTnet.Formatter;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Exposes the necessary methods required to configure the OpenNetty MQTT services.
/// </summary>
public sealed class OpenNettyMqttBuilder
{
    /// <summary>
    /// Creates a new instance of <see cref="OpenNettyMqttBuilder"/>.
    /// </summary>
    /// <param name="services">The services collection.</param>
    public OpenNettyMqttBuilder(IServiceCollection services)
        => Services = services ?? throw new ArgumentNullException(nameof(services));

    /// <summary>
    /// Gets the services collection.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IServiceCollection Services { get; }

    /// <summary>
    /// Enables validation of options during application startup.
    /// </summary>
    /// <returns>The <see cref="OpenNettyMqttBuilder"/> instance.</returns>
    public OpenNettyMqttBuilder ValidateOnStart()
    {
        Services.AddOptionsWithValidateOnStart<OpenNettyMqttOptions>();

        return this;
    }

    /// <summary>
    /// Amends the default OpenNetty MQTT configuration.
    /// </summary>
    /// <param name="configuration">The delegate used to configure the OpenNetty MQTT options.</param>
    /// <remarks>This extension can be safely called multiple times.</remarks>
    /// <returns>The <see cref="OpenNettyMqttBuilder"/> instance.</returns>
    public OpenNettyMqttBuilder Configure(Action<OpenNettyMqttOptions> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Services.Configure(configuration);

        return this;
    }

    /// <summary>
    /// Sets the MQTT client options used by the OpenNetty MQTT integration.
    /// </summary>
    /// <param name="configuration">The delegate used to configure the MQTT client options.</param>
    /// <returns>The <see cref="OpenNettyMqttBuilder"/> instance.</returns>
    public OpenNettyMqttBuilder SetClientOptions(Action<MqttClientOptionsBuilder> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = new MqttClientOptionsBuilder();
        configuration(builder);

        return Configure(options => options.ClientOptions = builder.Build());
    }

    /// <summary>
    /// Sets the MQTT root topic dedicated to the OpenNetty MQTT integration.
    /// </summary>
    /// <param name="topic">The MQTT root topic dedicated to the OpenNetty MQTT integration.</param>
    /// <returns>The <see cref="OpenNettyMqttBuilder"/> instance.</returns>
    public OpenNettyMqttBuilder SetRootTopic(string topic)
    {
        ArgumentException.ThrowIfNullOrEmpty(topic);

        return Configure(options => options.RootTopic = topic);
    }

    /// <summary>
    /// Imports the OpenNetty MQTT configuration from the specified <paramref name="files"/>.
    /// </summary>
    /// <param name="files">The files.</param>
    /// <returns>The <see cref="OpenNettyMqttBuilder"/> instance.</returns>
    public OpenNettyMqttBuilder ImportFromXmlConfiguration(params ImmutableArray<IFileInfo> files)
    {
        if (files.Any(static file => !file.Exists))
        {
            throw new FileNotFoundException(SR.GetResourceString(SR.ID0070));
        }

        var builder = ImmutableArray.CreateBuilder<XDocument>(files.Length);

        foreach (var file in files)
        {
            using var stream = file.CreateReadStream();

            builder.Add(XDocument.Load(stream));
        }

        return ImportFromXmlConfiguration(builder.ToImmutable());
    }

    /// <summary>
    /// Imports the OpenNetty MQTT configuration from the specified <paramref name="documents"/>.
    /// </summary>
    /// <param name="documents">The document.</param>
    /// <returns>The <see cref="OpenNettyMqttBuilder"/> instance.</returns>
    public OpenNettyMqttBuilder ImportFromXmlConfiguration(params ImmutableArray<XDocument> documents)
    {
        if (documents.Any(static document => document.Root?.Name != "Configuration"))
        {
            throw new InvalidOperationException(SR.FormatID0071("Configuration"));
        }

        var configuration = documents
            .Select(static document => document.Root!.Element("Mqtt"))
            .Where(static element => element is not null)
            .ToList() switch
            {
                [XElement element] => element,

                [] => throw new InvalidOperationException(SR.FormatID0090("Mqtt")),
                _  => throw new InvalidOperationException(SR.FormatID0123("Mqtt"))
            };

        var builder = new MqttClientOptionsBuilder();

        builder.WithTcpServer(
            host: (string?) configuration.Attribute("Server") ?? throw new InvalidOperationException(SR.FormatID0091("Server")),
            port: (int?) configuration.Attribute("Port"));

        builder.WithProtocolVersion(MqttProtocolVersion.V500);

        if ((string?) configuration.Attribute("Username") is { Length: > 0 } username &&
            (string?) configuration.Attribute("Password") is { Length: > 0 } password)
        {
            builder.WithCredentials(username, password);
        }

        if ((string?) configuration.Attribute("ClientId") is { Length: > 0 } identifier)
        {
            builder.WithClientId(identifier);
        }

        builder.WithTlsOptions(builder =>
        {
            var certificates = GetServerCertificates(configuration);
            if (certificates is { Count: > 0 })
            {
                builder.UseTls()
                    .WithRevocationMode(X509RevocationMode.NoCheck)
                    .WithTrustChain(certificates);

                var host = (string?) configuration.Attribute("TlsServerTargetHost");
                if (!string.IsNullOrEmpty(host))
                {
                    builder.WithTargetHost(host);
                }

                certificates = GetClientCertificates(configuration);
                if (certificates is { Count: > 0 })
                {
                    builder.WithClientCertificates(certificates);
                }
            }

            else
            {
                builder.UseTls(false);
            }
        });

        return Configure(options =>
        {
            options.DisableHomeAssistantDiscovery = (bool?) configuration.Attribute("DisableHomeAssistantDiscovery") ?? false;

            var topics = (
                RootTopic: (string?) configuration.Attribute("RootTopic"),
                DiscoveryRootTopic: (string?) configuration.Attribute("HomeAssistantDiscoveryRootTopic"));

            if (!string.IsNullOrEmpty(topics.RootTopic))
            {
                options.RootTopic = topics.RootTopic;
            }

            if (!string.IsNullOrEmpty(topics.DiscoveryRootTopic))
            {
                options.HomeAssistantDiscoveryRootTopic = topics.DiscoveryRootTopic;
            }

            var culture = (string?) configuration.Attribute("HomeAssistantDiscoveryUICulture");
            if (!string.IsNullOrEmpty(culture))
            {
                options.HomeAssistantDiscoveryUICulture = CultureInfo.GetCultureInfo(culture);
            }

            options.ClientOptions = builder.Build();
        });

        static X509Certificate2Collection? GetServerCertificates(XElement element)
        {
            var path = (string?) element.Attribute("TlsServerCertificateAuthorityFile");
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var certificates = new X509Certificate2Collection();
            certificates.ImportFromPemFile(path);
            return certificates;
        }

        static X509Certificate2Collection? GetClientCertificates(XElement element)
        {
            var paths = (
                TlsClientCertificateFile: (string?) element.Attribute("TlsClientCertificateFile"),
                TlsClientCertificatePrivateKeyFile: (string?) element.Attribute("TlsClientCertificatePrivateKeyFile"));

            if (string.IsNullOrEmpty(paths.TlsClientCertificateFile))
            {
                return null;
            }

            if (string.IsNullOrEmpty(paths.TlsClientCertificatePrivateKeyFile))
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0098));
            }

            var certificate = X509Certificate2.CreateFromPemFile(
                paths.TlsClientCertificateFile, paths.TlsClientCertificatePrivateKeyFile);

            // Note: on Windows, the client certificate is exported and re-imported to work around a limitation
            // of the cryptographic stack that doesn't allow using an ephemeral key for TLS client authentication.
            return OperatingSystem.IsWindows() ?
                [X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), password: null)] :
                [certificate];
        }
    }

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override bool Equals([NotNullWhen(true)] object? obj) => base.Equals(obj);

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override int GetHashCode() => base.GetHashCode();

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override string? ToString() => base.ToString();
}
