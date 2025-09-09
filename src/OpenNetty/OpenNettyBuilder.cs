/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.IO.Ports;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.FileProviders;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Exposes the necessary methods required to configure the OpenNetty services.
/// </summary>
public sealed class OpenNettyBuilder
{
    /// <summary>
    /// Creates a new instance of <see cref="OpenNettyBuilder"/>.
    /// </summary>
    /// <param name="services">The services collection.</param>
    public OpenNettyBuilder(IServiceCollection services)
        => Services = services ?? throw new ArgumentNullException(nameof(services));

    /// <summary>
    /// Gets the services collection.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IServiceCollection Services { get; }

    /// <summary>
    /// Amends the default OpenNetty configuration.
    /// </summary>
    /// <param name="configuration">The delegate used to configure the OpenNetty options.</param>
    /// <remarks>This extension can be safely called multiple times.</remarks>
    /// <returns>The <see cref="OpenNettyBuilder"/> instance.</returns>
    public OpenNettyBuilder Configure(Action<OpenNettyOptions> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Services.Configure(configuration);

        return this;
    }

    /// <summary>
    /// Adds a device to the list of registered devices.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <returns>The <see cref="OpenNettyBuilder"/> instance.</returns>
    public OpenNettyBuilder AddDevice(OpenNettyDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return Configure(options => options.Devices.Add(device));
    }

    /// <summary>
    /// Adds multiple devices to the list of registered devices.
    /// </summary>
    /// <param name="devices">The devices.</param>
    /// <returns>The <see cref="OpenNettyBuilder"/> instance.</returns>
    public OpenNettyBuilder AddDevices(IEnumerable<OpenNettyDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        return Configure(options => options.Devices.AddRange(devices));
    }

    /// <summary>
    /// Adds an endpoint to the list of registered endpoints.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <returns>The <see cref="OpenNettyBuilder"/> instance.</returns>
    public OpenNettyBuilder AddEndpoint(OpenNettyEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return Configure(options => options.Endpoints.Add(endpoint));
    }

    /// <summary>
    /// Adds multiple endpoints to the list of registered endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoints.</param>
    /// <returns>The <see cref="OpenNettyBuilder"/> instance.</returns>
    public OpenNettyBuilder AddEndpoints(IEnumerable<OpenNettyEndpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return Configure(options => options.Endpoints.AddRange(endpoints));
    }

    /// <summary>
    /// Adds a gateway to the list of registered gateways.
    /// </summary>
    /// <param name="gateway">The gateway.</param>
    /// <returns>The <see cref="OpenNettyBuilder"/> instance.</returns>
    public OpenNettyBuilder AddGateway(OpenNettyGateway gateway)
    {
        ArgumentNullException.ThrowIfNull(gateway);

        return Configure(options => options.Gateways.Add(gateway));
    }

    /// <summary>
    /// Adds multiple gateways to the list of registered gateways.
    /// </summary>
    /// <param name="gateways">The gateways.</param>
    /// <returns>The <see cref="OpenNettyBuilder"/> instance.</returns>
    public OpenNettyBuilder AddGateways(IEnumerable<OpenNettyGateway> gateways)
    {
        ArgumentNullException.ThrowIfNull(gateways);

        return Configure(options => options.Gateways.AddRange(gateways));
    }

    /// <summary>
    /// Imports the OpenNetty configuration from the specified <paramref name="file"/>.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>The <see cref="OpenNettyBuilder"/> instance.</returns>
    public OpenNettyBuilder ImportFromXmlConfiguration(IFileInfo file)
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
    /// Imports the OpenNetty configuration from the specified <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The <see cref="OpenNettyBuilder"/> instance.</returns>
    public OpenNettyBuilder ImportFromXmlConfiguration(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0077));
        }

        return ImportFromXmlConfiguration(XDocument.Load(path));
    }

    /// <summary>
    /// Imports the OpenNetty configuration from the specified <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The <see cref="OpenNettyBuilder"/> instance.</returns>
    public OpenNettyBuilder ImportFromXmlConfiguration(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return ImportFromXmlConfiguration(XDocument.Load(stream));
    }

    /// <summary>
    /// Imports the OpenNetty configuration from the specified <paramref name="document"/>.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The <see cref="OpenNettyBuilder"/> instance.</returns>
    public OpenNettyBuilder ImportFromXmlConfiguration(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Root?.Name != "Configuration")
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0078));
        }

        List<OpenNettyDevice> devices = [];
        List<OpenNettyEndpoint> endpoints = [];
        List<OpenNettyGateway> gateways = [];

        foreach (var device in document.Root.Descendants("Device"))
        {
            // Ensure a device with an identical serial number wasn't already added to the list of devices.
            var number = (string?) device.Attribute("SerialNumber");
            if (number is not null && devices.Exists(device => device.SerialNumber == number))
            {
                throw new InvalidOperationException(SR.FormatID0115(number));
            }

            devices.Add(GetDevice(device));
        }

        foreach (var gateway in document.Root.Descendants("Gateway"))
        {
            if (gateway.Parent?.Name != "Device")
            {
                throw new NotSupportedException(SR.GetResourceString(SR.ID0080));
            }

            var device = GetDevice(gateway.Parent);

            gateways.Add((string?) gateway.Attribute("Type") switch
            {
                "Serial" => OpenNettyGateway.Create(
                    name  : (string?) gateway.Attribute("Name") ?? throw new InvalidOperationException(SR.FormatID0081("Name")),
                    device: device,
                    port  : new SerialPort(
                        portName: (string?) gateway.Attribute("Port") ?? throw new InvalidOperationException(SR.FormatID0082("Port")),
                        baudRate: (int?) gateway.Attribute("BaudRate") switch
                        {
                            int value => value,

                            null when device.Definition.Settings.TryGetValue(OpenNettySettings.SerialPortBaudRate, out string? setting)
                                => int.Parse(setting, CultureInfo.InvariantCulture),

                            null => throw new InvalidOperationException(SR.FormatID0082("BaudRate")),
                        },
                        parity: (string?) gateway.Attribute("Parity") switch
                        {
                            "None"  => Parity.None,
                            "Odd"   => Parity.Odd,
                            "Even"  => Parity.Even,
                            "Mark"  => Parity.Mark,
                            "Space" => Parity.Space,

                            null when device.Definition.Settings.TryGetValue(OpenNettySettings.SerialPortParity, out string? setting)
                                => setting switch
                                {
                                    "None"  => Parity.None,
                                    "Odd"   => Parity.Odd,
                                    "Even"  => Parity.Even,
                                    "Mark"  => Parity.Mark,
                                    "Space" => Parity.Space,

                                    string value => throw new InvalidOperationException(SR.FormatID0106(value))
                                },

                            null or { Length: 0 } => throw new InvalidOperationException(SR.FormatID0082("Parity")),

                            string value => throw new InvalidOperationException(SR.FormatID0106(value))
                        },
                        dataBits: (int?) gateway.Attribute("DataBits") switch
                        {
                            int value => value,

                            null when device.Definition.Settings.TryGetValue(OpenNettySettings.SerialPortDataBits, out string? setting)
                                => int.Parse(setting, CultureInfo.InvariantCulture),

                            null => throw new InvalidOperationException(SR.FormatID0082("DataBits")),
                        },
                        stopBits: (string?) gateway.Attribute("StopBits") switch
                        {
                            "1"   => StopBits.One,
                            "1.5" => StopBits.OnePointFive,
                            "2"   => StopBits.Two,

                            null when device.Definition.Settings.TryGetValue(OpenNettySettings.SerialPortStopBits, out string? setting)
                                => setting switch
                                {
                                    "1"   => StopBits.One,
                                    "1.5" => StopBits.OnePointFive,
                                    "2"   => StopBits.Two,

                                    string value => throw new InvalidOperationException(SR.FormatID0107(value))
                                },

                            null or { Length: 0 } => throw new InvalidOperationException(SR.FormatID0082("StopBits")),

                            string value => throw new InvalidOperationException(SR.FormatID0107(value))
                        })),

                "Tcp" => OpenNettyGateway.Create(
                    name    : (string?) gateway.Attribute("Name") ?? throw new InvalidOperationException(SR.FormatID0081("Name")),
                    device  : device,
                    endpoint: new IPEndPoint(
                        address: IPAddress.Parse((string?) gateway.Attribute("Server") ?? throw new InvalidOperationException(SR.FormatID0083("Server"))),
                        port   : (int?) gateway.Attribute("Port") ?? 20_000),
                    password: (string?) gateway.Attribute("Password")),

                null or { Length: 0 } => throw new InvalidOperationException(SR.FormatID0081("Type")),

                _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0084))
            });
        }

        foreach (var endpoint in document.Root.Descendants("Endpoint"))
        {
            // Ensure an endpoint with an identical name wasn't already added to the list of endpoints.
            var name = (string?) endpoint.Attribute("Name");
            if (name is not null && endpoints.Exists(endpoint => endpoint.Name == name))
            {
                throw new InvalidOperationException(SR.FormatID0085(name));
            }

            var device = endpoint.Parent?.Name == "Device" ? GetDevice(endpoint.Parent) :
                         endpoint.Parent?.Name == "Unit" && endpoint.Parent.Parent?.Name == "Device" ? GetDevice(endpoint.Parent.Parent) : null;

            var unit = device is not null && endpoint.Parent?.Name == "Unit" ? GetUnit(endpoint.Parent,
                (byte?) (uint?) endpoint.Parent.Attribute("Id") ?? throw new InvalidOperationException(SR.FormatID0086("Id"))) : null;

            var type = (string?) endpoint.Attribute("Type") switch
            {
                "Nitoo"           => OpenNettyAddressType.Nitoo,
                "SCS light point" => OpenNettyAddressType.ScsLightPoint,
                "Zigbee"          => OpenNettyAddressType.Zigbee,

                null => (OpenNettyAddressType?) null,

                string value => throw new InvalidOperationException(SR.FormatID0087(value))
            };

            // Try to infer common address types if no explicit type was specified.
            type ??= device?.Definition.Protocol switch
            {
                // Note: gateway endpoints don't have an address attached.
                _ when device is not null && device.Definition.Capabilities.Contains(OpenNettyCapabilities.OpenWebNetGateway) => null,

                OpenNettyProtocol.Nitoo  => OpenNettyAddressType.Nitoo,
                OpenNettyProtocol.Zigbee => OpenNettyAddressType.Zigbee,

                // Note: SCS addresses are never inferred automatically and a type MUST be explicitly attached.

                _ => throw new InvalidOperationException(SR.FormatID0088(name, "Type"))
            };

            var protocol = type switch
            {
                null => device?.Definition.Protocol ?? throw new InvalidOperationException(SR.FormatID0088(name, "Type")),

                OpenNettyAddressType.Nitoo         => OpenNettyProtocol.Nitoo,
                OpenNettyAddressType.ScsLightPoint => OpenNettyProtocol.Scs,
                OpenNettyAddressType.Zigbee        => OpenNettyProtocol.Zigbee,

                _ => throw new InvalidOperationException(SR.FormatID0088(name, "Type"))
            };

            var address = type switch
            {
                null => (OpenNettyAddress?) null,

                OpenNettyAddressType.Nitoo => OpenNettyAddress.FromNitooAddress(
                    identifier: uint.Parse(device?.SerialNumber ?? throw new InvalidOperationException(SR.FormatID0089("SerialNumber")), CultureInfo.InvariantCulture),
                    unit      : (byte?) (uint?) endpoint.Parent?.Attribute("Id") ?? 0),

                OpenNettyAddressType.ScsLightPoint => OpenNettyAddress.FromScsLightPointAddress(
                    extension: (byte?) (uint?) endpoint.Attribute("Extension") ?? 0,
                    general  : (bool?) endpoint.Attribute("General") ?? false,
                    group    : (byte?) (uint?) endpoint.Attribute("Group"),
                    area     : (byte?) (uint?) endpoint.Attribute("Area"),
                    point    : (byte?) (uint?) endpoint.Attribute("Point")),

                OpenNettyAddressType.Zigbee => OpenNettyAddress.FromHexadecimalZigbeeAddress(
                    identifier: device?.SerialNumber ?? throw new InvalidOperationException(SR.FormatID0094("SerialNumber")),
                    unit      : (byte?) (uint?) endpoint.Parent?.Attribute("Id") ?? 0),

                _ => throw new InvalidOperationException(SR.FormatID0088(name, "Type"))
            };

            endpoints.Add(new OpenNettyEndpoint
            {
                Address = address,
                Capabilities = device is null && unit is null ? GetCapabilities(endpoint) : [],
                Description = (string?) endpoint.Attribute("Description"),
                Device = device,
                Gateway = (string?) endpoint.Attribute("Gateway") is string gateway ? FindGatewayByName(gateways, gateway) : null,
                Medium = device?.Definition.Medium,
                Name = name ?? ComputeDefaultEndpointName(protocol, address, device, unit),
                Protocol = protocol,
                Settings = GetSettings(endpoint),
                Unit = unit
            });

            static string ComputeDefaultEndpointName(
                OpenNettyProtocol protocol, OpenNettyAddress? address, OpenNettyDevice? device, OpenNettyUnit? unit)
            {
                var builder = new StringBuilder(Enum.GetName(protocol));
                builder.Append('/');

                switch (protocol)
                {
                    case OpenNettyProtocol.Nitoo or OpenNettyProtocol.Zigbee:
                        if (string.IsNullOrEmpty(device?.SerialNumber))
                        {
                            throw new InvalidOperationException(SR.GetResourceString(SR.ID0118));
                        }

                        builder.Append(device.SerialNumber);

                        if (unit is not null)
                        {
                            builder.Append('/');
                            builder.Append(unit.Definition.Id);
                        }
                        break;

                    case OpenNettyProtocol.Scs:
                        if (address is null)
                        {
                            throw new InvalidOperationException(SR.GetResourceString(SR.ID0119));
                        }

                        var (extension, general, group, area, point) = OpenNettyAddress.ToScsLightPointAddress(address.Value);

                        if (OpenNettyAddress.IsScsLightPointAreaAddress(address.Value))
                        {
                            builder.Append("light-point-area");
                            builder.Append('/');
                            builder.Append(extension);
                            builder.Append('/');
                            builder.Append(area);
                        }

                        else if (OpenNettyAddress.IsScsLightPointGeneralAddress(address.Value))
                        {
                            builder.Append("light-point-general");
                            builder.Append('/');
                            builder.Append(extension);
                        }

                        else if (OpenNettyAddress.IsScsLightPointGroupAddress(address.Value))
                        {
                            builder.Append("light-point-group");
                            builder.Append('/');
                            builder.Append(extension);
                            builder.Append('/');
                            builder.Append(group);
                        }

                        else if (OpenNettyAddress.IsScsLightPointPointToPointAddress(address.Value))
                        {
                            builder.Append("light-point-point-to-point");
                            builder.Append('/');
                            builder.Append(extension);
                            builder.Append('/');
                            builder.Append(area);
                            builder.Append('/');
                            builder.Append(point);
                        }

                        builder.Append(address.Value.ToString());
                        break;
                }

                return builder.ToString();
            }
        }

        return Configure(options =>
        {
            options.Devices.AddRange(devices);
            options.Endpoints.AddRange(endpoints);
            options.Gateways.AddRange(gateways);
        });

        static ImmutableHashSet<OpenNettyCapability> GetCapabilities(XElement element) =>
            element.Elements("Capability")
                   .Select(static element => (string?) element.Attribute("Name") ?? throw new InvalidOperationException(SR.FormatID0096("Name")))
                   .Select(static name => new OpenNettyCapability(name))
                   .ToImmutableHashSet();

        static OpenNettyDevice GetDevice(XElement element)
        {
            var brand = (string?) element.Attribute("Brand");
            if (string.IsNullOrEmpty(brand))
            {
                throw new InvalidOperationException(SR.FormatID0097("Brand"));
            }

            var model = (string?) element.Attribute("Model");
            if (string.IsNullOrEmpty(model))
            {
                throw new InvalidOperationException(SR.FormatID0097("Model"));
            }

            var definition = OpenNettyDevices.GetDeviceByModel(Enum.Parse<OpenNettyBrand>(brand), model)
                ?? throw new InvalidOperationException(SR.FormatID0098(brand, model));

            return new OpenNettyDevice
            {
                Definition = definition,
                Identity = definition.Identities.Single(identity =>
                    identity.Brand == Enum.Parse<OpenNettyBrand>(brand) && identity.Model == model),
                SerialNumber = (string?) element.Attribute("SerialNumber"),
                Settings = GetSettings(element),
                Units = [.. element.Elements("Unit").Select(static element =>
                    GetUnit(element, (byte?) (uint?) element.Attribute("Id") ?? throw new InvalidOperationException(SR.FormatID0086("Id"))))]
            };
        }

        static ImmutableDictionary<OpenNettySetting, string> GetSettings(XElement element) =>
            element.Elements("Setting").ToImmutableDictionary(
                element => new OpenNettySetting((string?) element.Attribute("Name") ?? throw new InvalidOperationException(SR.FormatID0099("Name"))),
                element => (string?) element.Attribute("Value") ?? throw new InvalidOperationException(SR.FormatID0099("Name")));

        static OpenNettyScenario GetScenario(XElement element) => new()
        {
            EndpointName = (string?) element.Attribute("Endpoint") ?? throw new InvalidOperationException(SR.FormatID0101("Endpoint")),
            FunctionCode = (byte?) (uint?) element.Attribute("FunctionCode") ?? throw new InvalidOperationException(SR.FormatID0101("FunctionCode"))
        };

        static OpenNettyUnit GetUnit(XElement element, byte unit)
        {
            var brand = (string?) element.Parent?.Attribute("Brand");
            if (string.IsNullOrEmpty(brand))
            {
                throw new InvalidOperationException(SR.FormatID0097("Brand"));
            }

            var model = (string?) element.Parent?.Attribute("Model");
            if (string.IsNullOrEmpty(model))
            {
                throw new InvalidOperationException(SR.FormatID0097("Model"));
            }

            var definition = OpenNettyDevices.GetUnitByModel(Enum.Parse<OpenNettyBrand>(brand), model, unit)
                ?? throw new InvalidOperationException(SR.FormatID0100(brand, model, unit));

            return new()
            {
                Definition = definition,
                Scenarios = [.. element.Elements("Scenario").Select(GetScenario)],
                Settings = GetSettings(element)
            };
        }

        static OpenNettyGateway FindGatewayByName(IReadOnlyList<OpenNettyGateway> gateways, string name)
        {
            for (var index = 0; index < gateways.Count; index++)
            {
                var gateway = gateways[index];
                if (string.Equals(gateway.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return gateway;
                }
            }

            throw new InvalidOperationException(SR.FormatID0102(name));
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
