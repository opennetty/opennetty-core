/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
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
    /// Imports the OpenNetty MQTT configuration from the specified <paramref name="file"/>.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>The <see cref="OpenNettyMqttBuilder"/> instance.</returns>
    public OpenNettyMqttBuilder ImportFromXmlConfiguration(IFileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!file.Exists)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0077));
        }

        using var stream = file.CreateReadStream();
        return ImportFromXmlConfiguration(stream);
    }

    /// <summary>
    /// Imports the OpenNetty MQTT configuration from the specified <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The <see cref="OpenNettyMqttBuilder"/> instance.</returns>
    public OpenNettyMqttBuilder ImportFromXmlConfiguration(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0077));
        }

        return ImportFromXmlConfiguration(XDocument.Load(path));
    }

    /// <summary>
    /// Imports the OpenNetty MQTT configuration from the specified <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The <see cref="OpenNettyMqttBuilder"/> instance.</returns>
    public OpenNettyMqttBuilder ImportFromXmlConfiguration(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return ImportFromXmlConfiguration(XDocument.Load(stream));
    }

    /// <summary>
    /// Imports the OpenNetty MQTT configuration from the specified <paramref name="document"/>.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The <see cref="OpenNettyMqttBuilder"/> instance.</returns>
    public OpenNettyMqttBuilder ImportFromXmlConfiguration(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Root?.Name != "Configuration")
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0078));
        }

        var element = document.Root.Element("Mqtt") ?? throw new InvalidOperationException(SR.FormatID0103("Mqtt"));
        var builder = new MqttClientOptionsBuilder();

        builder.WithTcpServer(
            host: (string?) element.Attribute("Server") ?? throw new InvalidOperationException(SR.FormatID0104("Server")),
            port: (int?) element.Attribute("Port"));

        builder.WithProtocolVersion(MqttProtocolVersion.V500);

        var username = (string?) element.Attribute("Username");
        var password = (string?) element.Attribute("Password");

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            builder.WithCredentials(username, password);
        }

        builder.WithTlsOptions(builder =>
        {
            var certificates = GetServerCertificates(element);
            if (certificates is { Count: > 0 })
            {
                builder.UseTls()
                    .WithRevocationMode(X509RevocationMode.NoCheck)
                    .WithTrustChain(certificates);

                var host = (string?) element.Attribute("TlsServerTargetHost");
                if (!string.IsNullOrEmpty(host))
                {
                    builder.WithTargetHost(host);
                }

                certificates = GetClientCertificates(element);
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
            var topic = (string?) element.Attribute("RootTopic");
            if (!string.IsNullOrEmpty(topic))
            {
                options.RootTopic = topic;
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
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0111));
            }

            var certificate = X509Certificate2.CreateFromPemFile(
                paths.TlsClientCertificateFile, paths.TlsClientCertificatePrivateKeyFile);

            // Note: on Windows, the client certificate is exported and re-imported to work around a limitation
            // of the cryptographic stack that doesn't allow using an ephemeral key for TLS client authentication.
            return OperatingSystem.IsWindows() ?
#if SUPPORTS_CERTIFICATE_LOADER
                [X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), password: null)] :
#else
                [new X509Certificate2(certificate.Export(X509ContentType.Pkcs12))] :
#endif
                [certificate];
        }
    }

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override bool Equals(object? obj) => base.Equals(obj);

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override int GetHashCode() => base.GetHashCode();

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override string? ToString() => base.ToString();
}
