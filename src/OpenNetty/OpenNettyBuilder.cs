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
    /// Enables validation of options during application startup.
    /// </summary>
    /// <returns>The <see cref="OpenNettyBuilder"/> instance.</returns>
    public OpenNettyBuilder ValidateOnStart()
    {
        Services.AddOptionsWithValidateOnStart<OpenNettyOptions>();

        return this;
    }

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
    public OpenNettyBuilder AddDevices(params ImmutableArray<OpenNettyDevice> devices)
    {
        if (devices.Any(static device => device is null))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0124), nameof(devices));
        }

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
    public OpenNettyBuilder AddEndpoints(params ImmutableArray<OpenNettyEndpoint> endpoints)
    {
        if (endpoints.Any(static endpoint => endpoint is null))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0124), nameof(endpoints));
        }

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
    public OpenNettyBuilder AddGateways(params ImmutableArray<OpenNettyGateway> gateways)
    {
        if (gateways.Any(static gateway => gateway is null))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0124), nameof(gateways));
        }

        return Configure(options => options.Gateways.AddRange(gateways));
    }

    /// <summary>
    /// Imports the OpenNetty configuration from the specified <paramref name="files"/>.
    /// </summary>
    /// <param name="files">The files.</param>
    /// <returns>The <see cref="OpenNettyBuilder"/> instance.</returns>
    public OpenNettyBuilder ImportFromXmlConfiguration(params ImmutableArray<IFileInfo> files)
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
    /// Imports the OpenNetty configuration from the specified <paramref name="documents"/>.
    /// </summary>
    /// <param name="documents">The documents.</param>
    /// <returns>The <see cref="OpenNettyBuilder"/> instance.</returns>
    public OpenNettyBuilder ImportFromXmlConfiguration(params ImmutableArray<XDocument> documents)
    {
        if (documents.Any(static document => document.Root?.Name != "Configuration"))
        {
            throw new InvalidOperationException(SR.FormatID0071("Configuration"));
        }

        return Configure(options =>
        {
            foreach (var gateway in documents.SelectMany(static document => document.Root!.Elements("Device").Elements("Gateway")))
            {
                var device = GetDevice(options.Gateways, gateway.Parent ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0073)));

                options.Gateways.Add((string?) gateway.Attribute("Type") switch
                {
                    "Serial" => OpenNettyGateway.Create(
                        name  : (string?) gateway.Attribute("Name") ?? throw new InvalidOperationException(SR.FormatID0074("Name")),
                        device: device,
                        port  : new SerialPort(
                            portName: (string?) gateway.Attribute("Port") ?? throw new InvalidOperationException(SR.FormatID0075("Port")),
                            baudRate: (int?) gateway.Attribute("BaudRate") switch
                            {
                                int value => value,

                                null when device.Definition.Settings.TryGetValue(OpenNettySettings.SerialPortBaudRate, out string? setting)
                                    => int.Parse(setting, CultureInfo.InvariantCulture),

                                null => throw new InvalidOperationException(SR.FormatID0075("BaudRate")),
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

                                        string value => throw new InvalidOperationException(SR.FormatID0093(value))
                                    },

                                null or { Length: 0 } => throw new InvalidOperationException(SR.FormatID0075("Parity")),

                                string value => throw new InvalidOperationException(SR.FormatID0093(value))
                            },
                            dataBits: (int?) gateway.Attribute("DataBits") switch
                            {
                                int value => value,

                                null when device.Definition.Settings.TryGetValue(OpenNettySettings.SerialPortDataBits, out string? setting)
                                    => int.Parse(setting, CultureInfo.InvariantCulture),

                                null => throw new InvalidOperationException(SR.FormatID0075("DataBits")),
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

                                        string value => throw new InvalidOperationException(SR.FormatID0094(value))
                                    },

                                null or { Length: 0 } => throw new InvalidOperationException(SR.FormatID0075("StopBits")),

                                string value => throw new InvalidOperationException(SR.FormatID0094(value))
                            })),

                    "Tcp" when IPAddress.TryParse((string?) gateway.Attribute("Server"), out IPAddress? address)
                        => OpenNettyGateway.Create(
                            name    : (string?) gateway.Attribute("Name") ?? throw new InvalidOperationException(SR.FormatID0074("Name")),
                            device  : device,
                            endpoint: new IPEndPoint(address, port: (int?) gateway.Attribute("Port") ?? 20_000),
                            password: (string?) gateway.Attribute("Password")),

                    "Tcp" => OpenNettyGateway.Create(
                        name    : (string?) gateway.Attribute("Name") ?? throw new InvalidOperationException(SR.FormatID0074("Name")),
                        device  : device,
                        endpoint: new DnsEndPoint(
                            host: (string?) gateway.Attribute("Server") ?? throw new InvalidOperationException(SR.FormatID0076("Server")),
                            port: (int?) gateway.Attribute("Port") ?? 20_000),
                        password: (string?) gateway.Attribute("Password")),

                    null or { Length: 0 } => throw new InvalidOperationException(SR.FormatID0074("Type")),

                    _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0077))
                });
            }

            foreach (var device in documents.SelectMany(static document => document.Root!.Elements("Device")))
            {
                options.Devices.Add(GetDevice(options.Gateways, device));
            }

            // Note: endpoint nodes are allowed to appear directly under the root configuration node,
            // nested within a device node or nested within a unit node that is nested within a device node.
            foreach (var endpoint in documents.SelectMany(static document => document.Root!.Elements("Endpoint"))
                .Concat(documents.SelectMany(static document => document.Root!.Elements("Device").Elements("Endpoint")))
                .Concat(documents.SelectMany(static document => document.Root!.Elements("Device").Elements("Unit").Elements("Endpoint"))))
            {
                var name = (string?) endpoint.Attribute("Name");

                var device = endpoint.Parent?.Name == "Device"
                    ? GetDevice(options.Gateways, endpoint.Parent)
                    :  endpoint.Parent?.Name == "Unit" && endpoint.Parent.Parent?.Name == "Device"
                        ? GetDevice(options.Gateways, endpoint.Parent.Parent)
                        : null;

                var unit = device is not null && endpoint.Parent?.Name == "Unit" ? GetUnit(endpoint.Parent,
                    (byte?) (uint?) endpoint.Parent.Attribute("Id") ?? throw new InvalidOperationException(SR.FormatID0078("Id"))) : null;

                var type = (string?) endpoint.Attribute("Type") switch
                {
                    "Nitoo"             => OpenNettyAddressType.Nitoo,
                    "SCS light point"   => OpenNettyAddressType.ScsLightPoint,
                    "SCS scenario plus" => OpenNettyAddressType.ScsScenarioPlus,
                    "Zigbee"            => OpenNettyAddressType.Zigbee,

                    // Try to infer common address types if no explicit type was specified.
                    null => device?.Definition.Protocol switch
                    {
                        // Note: gateway endpoints don't have an address attached.
                        _ when device is not null && device.Definition.HasCapability(OpenNettyCapabilities.OpenWebNetGateway)
                            => null as OpenNettyAddressType?,

                        OpenNettyProtocol.Nitoo  => OpenNettyAddressType.Nitoo,
                        OpenNettyProtocol.Zigbee => OpenNettyAddressType.Zigbee,

                        // Note: SCS units/modules supporting ON/OFF switching or shutter
                        // control are assumed to use SCS light point addresses by default.
                        OpenNettyProtocol.Scs when unit is not null &&
                            (unit.HasCapability(OpenNettyCapabilities.OnOffSwitchControl) ||
                             unit.HasCapability(OpenNettyCapabilities.BasicShutterControl) ||
                             unit.HasCapability(OpenNettyCapabilities.AdvancedShutterControl))
                            => OpenNettyAddressType.ScsLightPoint,

                        _ => throw new InvalidOperationException(SR.FormatID0080(name, "Type"))
                    },

                    string value => throw new InvalidOperationException(SR.FormatID0079(value))
                };

                var protocol = type switch
                {
                    OpenNettyAddressType.Nitoo                                                 => OpenNettyProtocol.Nitoo,
                    OpenNettyAddressType.ScsLightPoint or OpenNettyAddressType.ScsScenarioPlus => OpenNettyProtocol.Scs,
                    OpenNettyAddressType.Zigbee                                                => OpenNettyProtocol.Zigbee,

                    null => device?.Definition.Protocol ?? throw new InvalidOperationException(SR.FormatID0080(name, "Type")),

                    _ => throw new InvalidOperationException(SR.FormatID0080(name, "Type"))
                };

                var address = type switch
                {
                    OpenNettyAddressType.Nitoo when (uint?) endpoint.Attribute("Id") is uint identifier
                        => OpenNettyAddress.FromNitooAddress(
                            identifier: identifier,
                            unit      : (byte?) (uint?) endpoint.Attribute("Unit") ?? unit?.Definition.Id ?? 0),

                    OpenNettyAddressType.Nitoo when device is not null
                        => OpenNettyAddress.FromNitooAddress(
                            identifier: device.Identifier,
                            unit      : (byte?) (uint?) endpoint.Attribute("Unit") ?? unit?.Definition.Id ?? 0),

                    OpenNettyAddressType.ScsLightPoint => OpenNettyAddress.FromScsLightPointAddress(
                        extension: (byte?) (uint?) endpoint.Attribute("Extension") ?? 0,
                        general  : (bool?) endpoint.Attribute("General") ?? false,
                        group    : (byte?) (uint?) endpoint.Attribute("Group"),
                        area     : (byte?) (uint?) endpoint.Attribute("Area"),
                        point    : (byte?) (uint?) endpoint.Attribute("Point")),

                    OpenNettyAddressType.ScsScenarioPlus when (ushort?) (uint?) endpoint.Attribute("Id") is ushort identifier
                        => OpenNettyAddress.FromScsScenarioPlusAddress(identifier),

                    OpenNettyAddressType.Zigbee when (string?) endpoint.Attribute("Id") is string identifier
                        => OpenNettyAddress.FromHexadecimalZigbeeAddress(
                            identifier: identifier,
                            unit      : (byte?) (uint?) endpoint.Attribute("Unit") ?? unit?.Definition.Id ?? 0),

                    OpenNettyAddressType.Zigbee when device is not null
                        => OpenNettyAddress.FromZigbeeAddress(
                            identifier: device.Identifier,
                            unit      : (byte?) (uint?) endpoint.Attribute("Unit") ?? unit?.Definition.Id ?? 0),

                    null => (OpenNettyAddress?) null,

                    _ => throw new InvalidOperationException(SR.FormatID0080(name, "Type"))
                };

                options.Endpoints.Add(new OpenNettyEndpoint
                {
                    Address = address,
                    Capabilities = GetCapabilities(endpoint),
                    Description = (string?) endpoint.Attribute("Description"),
                    Device = device,
                    Gateway = device is not null && device.HasCapability(OpenNettyCapabilities.OpenWebNetGateway)
                        ? options.Gateways.Single(gateway => gateway.Device == device)
                        : (string?) endpoint.Attribute("GatewayName") is string gateway ?
                            FindGatewayByName(options.Gateways, gateway) :
                            device?.Gateway ??
                            options.Gateways.FirstOrDefault(gateway => gateway.Protocol == protocol) ??
                            throw new InvalidOperationException(SR.FormatID0106(protocol)),
                    Medium = device?.Definition.Medium,
                    Name = name ?? ComputeDefaultEndpointName(protocol, address, device, unit),
                    Protocol = protocol,
                    Settings = GetSettings(endpoint),
                    Unit = unit
                });
            }
        });

        static string ComputeDefaultEndpointName(
            OpenNettyProtocol protocol, OpenNettyAddress? address, OpenNettyDevice? device, OpenNettyUnit? unit)
        {
            var builder = new StringBuilder(Enum.GetName(protocol));
            builder.Append('/');

            switch (protocol)
            {
                case OpenNettyProtocol.Nitoo or OpenNettyProtocol.Zigbee:
                    if (device is null)
                    {
                        throw new InvalidOperationException(SR.GetResourceString(SR.ID0104));
                    }

                    builder.Append(new string(device.Identifier.ToString().Where(char.IsAsciiDigit).ToArray()));

                    if (unit is not null)
                    {
                        builder.Append('/');
                        builder.Append(unit.Definition.Id);
                    }
                    break;

                case OpenNettyProtocol.Scs when address?.Type is OpenNettyAddressType.ScsLightPoint:
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

                case OpenNettyProtocol.Scs when address?.Type is OpenNettyAddressType.ScsScenarioPlus:
                    builder.Append("scs-scenario-plus");
                    builder.Append('/');
                    builder.Append(OpenNettyAddress.ToScsScenarioPlusAddress(address.Value));
                    break;

                case OpenNettyProtocol.Scs:
                    throw new InvalidOperationException(SR.GetResourceString(SR.ID0105));
            }

            return builder.ToString();
        }

        static ImmutableHashSet<OpenNettyCapability> GetCapabilities(XElement element) =>
            element.Elements("Capability")
                   .Select(static element => (string?) element.Attribute("Name") ?? throw new InvalidOperationException(SR.FormatID0083("Name")))
                   .Select(static name => new OpenNettyCapability(name))
                   .ToImmutableHashSet();

        static OpenNettyDevice GetDevice(IReadOnlyList<OpenNettyGateway> gateways, XElement element)
        {
            var brand = (string?) element.Attribute("Brand");
            if (string.IsNullOrEmpty(brand))
            {
                throw new InvalidOperationException(SR.FormatID0084("Brand"));
            }

            var model = (string?) element.Attribute("Model");
            if (string.IsNullOrEmpty(model))
            {
                throw new InvalidOperationException(SR.FormatID0084("Model"));
            }

            var definition = OpenNettyDevices.GetDeviceByModel(Enum.Parse<OpenNettyBrand>(brand), model)
                ?? throw new InvalidOperationException(SR.FormatID0085(brand, model));

            var gateway = definition.HasCapability(OpenNettyCapabilities.OpenWebNetGateway)
                ? null
                : (string?) element.Attribute("GatewayName") switch
                {
                    string value => FindGatewayByName(gateways, value),

                    _ => gateways.FirstOrDefault(gateway => gateway.Protocol == definition.Protocol)
                        ?? throw new InvalidOperationException(SR.FormatID0106(definition.Protocol))
                };

            return new OpenNettyDevice
            {
                Definition = definition,
                Gateway = gateway,
                Identifier = GetIdentifier(definition, element),
                Identity = definition.Identities.Single(identity =>
                    identity.Brand == Enum.Parse<OpenNettyBrand>(brand) && identity.Model == model),
                Settings = GetSettings(element),
                Units = [.. element.Elements("Unit").Select(static element =>
                    GetUnit(element, (byte?) (uint?) element.Attribute("Id")
                        ?? throw new InvalidOperationException(SR.FormatID0078("Id"))))]
            };
        }

        static OpenNettyDeviceIdentifier GetIdentifier(OpenNettyDeviceDefinition definition, XElement element)
        {
            if (definition.Protocol is OpenNettyProtocol.Nitoo)
            {
                var identifier = (uint?) element.Attribute("SerialNumber");
                if (identifier is not null)
                {
                    return OpenNettyDeviceIdentifier.FromNitooSerialNumber(identifier.Value);
                }
            }

            else if (definition.Protocol is OpenNettyProtocol.Scs)
            {
                var identifier = (string?) element.Attribute("SerialNumber");
                if (!string.IsNullOrEmpty(identifier))
                {
                    return OpenNettyDeviceIdentifier.FromScsSerialNumber(identifier);
                }
            }

            else if (definition.Protocol is OpenNettyProtocol.Zigbee)
            {
                var identifier = (string?) element.Attribute("SerialNumber");
                if (!string.IsNullOrEmpty(identifier))
                {
                    return OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(identifier);
                }
            }

            var address = (string?) element.Attribute("MacAddress");
            if (!string.IsNullOrEmpty(address))
            {
                return OpenNettyDeviceIdentifier.FromMacAddress(address);
            }

            throw new InvalidOperationException(SR.GetResourceString(SR.FormatID0108("SerialNumber", "MacAddress")));
        }

        static ImmutableDictionary<OpenNettySetting, string> GetSettings(XElement element) =>
            element.Elements("Setting").ToImmutableDictionary(
                element => new OpenNettySetting((string?) element.Attribute("Name") ?? throw new InvalidOperationException(SR.FormatID0086("Name"))),
                element => (string?) element.Attribute("Value") ?? throw new InvalidOperationException(SR.FormatID0086("Name")));

        static OpenNettyScenario GetScenario(XElement element) => new()
        {
            EndpointName = (string?) element.Attribute("EndpointName") ?? throw new InvalidOperationException(SR.FormatID0088("EndpointName")),
            FunctionCode = (byte?) (uint?) element.Attribute("FunctionCode") ?? throw new InvalidOperationException(SR.FormatID0088("FunctionCode"))
        };

        static OpenNettyUnit GetUnit(XElement element, byte unit)
        {
            var brand = (string?) element.Parent?.Attribute("Brand");
            if (string.IsNullOrEmpty(brand))
            {
                throw new InvalidOperationException(SR.FormatID0084("Brand"));
            }

            var model = (string?) element.Parent?.Attribute("Model");
            if (string.IsNullOrEmpty(model))
            {
                throw new InvalidOperationException(SR.FormatID0084("Model"));
            }

            var definition = OpenNettyDevices.GetUnitByModel(Enum.Parse<OpenNettyBrand>(brand), model, unit)
                ?? throw new InvalidOperationException(SR.FormatID0087(unit, brand, model));

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

            throw new InvalidOperationException(SR.FormatID0089(name));
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
