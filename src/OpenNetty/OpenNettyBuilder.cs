/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO.Ports;
using System.Net;
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
            // Note: endpoint nodes are allowed to appear directly under the root configuration
            // node, or nested within a unit node that is itself nested within a device node.

            foreach (var element in documents.SelectMany(static document => document.Root!.Elements("Device")))
            {
                var device = CreateDevice(element);

                // Attach the settings to the device.
                device.Settings = CreateSettings(element);

                // If an explicit gateway name was specified, find the corresponding gateway and attach it.
                var name = (string?) element.Attribute("GatewayName");
                if (!string.IsNullOrEmpty(name))
                {
                    if (device.GetUnit(0).HasCapability(OpenNettyCapabilities.OpenWebNetGateway))
                    {
                        throw new InvalidOperationException(SR.FormatID0135("GatewayName"));
                    }

                    device.Gateway = FindGatewayByName(options.Gateways, name);
                }

                foreach (var gateway in element.Elements("Gateway"))
                {
                    options.Gateways.Add(CreateGateway(device, gateway));
                }

                foreach (var unit in element.Elements("Unit"))
                {
                    var identifier = (byte?) (uint?) unit.Attribute("Id")
                        ?? throw new InvalidOperationException(SR.FormatID0078("Id"));

                    // Attach the scenarios and settings to the unit.
                    device.GetUnit(identifier).Scenarios = [.. unit.Elements("Scenario").Select(CreateScenario)];
                    device.GetUnit(identifier).Settings = CreateSettings(unit);

                    foreach (var endpoint in unit.Elements("Endpoint"))
                    {
                        options.Endpoints.Add(CreateEndpoint(device.GetUnit(identifier), endpoint));
                    }
                }

                options.Devices.Add(device);
            }

            foreach (var element in documents.SelectMany(static document => document.Root!.Elements("Endpoint")))
            {
                var endpoint = CreateEndpoint(null, element);

                // If an explicit gateway name was specified, find the corresponding gateway and attach it.
                var name = (string?) element.Attribute("GatewayName");
                if (!string.IsNullOrEmpty(name))
                {
                    endpoint.Gateway = FindGatewayByName(options.Gateways, name);
                }

                options.Endpoints.Add(endpoint);
            }
        });

        static ImmutableHashSet<OpenNettyCapability> CreateCapabilities(XElement element)
            => element.Elements("Capability")
                      .Select(static element => (string?) element.Attribute("Name") ?? throw new InvalidOperationException(SR.FormatID0083("Name")))
                      .Select(static name => new OpenNettyCapability(name))
                      .ToImmutableHashSet();

        static OpenNettyDevice CreateDevice(XElement element)
        {
            if (!Enum.TryParse((string?) element.Attribute("Brand"), out OpenNettyBrand brand))
            {
                throw new InvalidOperationException(SR.FormatID0084("Brand"));
            }

            var model = (string?) element.Attribute("Model");
            if (string.IsNullOrEmpty(model))
            {
                throw new InvalidOperationException(SR.FormatID0084("Model"));
            }

            var definition = OpenNettyDevices.GetDeviceDefinitionByModel(brand, model);
            var identifier = CreateDeviceIdentifier(definition, element);

            return OpenNettyDevice.Create(brand, model, (string?) element.Attribute("Name"), identifier);
        }

        static OpenNettyDeviceIdentifier? CreateDeviceIdentifier(OpenNettyDeviceDefinition definition, XElement element)
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

            return null;
        }

        static OpenNettyEndpoint CreateEndpoint(OpenNettyUnit? unit, XElement element)
        {
            var name = (string?) element.Attribute("Name");

            var type = (string?) element.Attribute("Type") switch
            {
                "Nitoo"             => OpenNettyAddressType.Nitoo,
                "SCS dry contact"   => OpenNettyAddressType.ScsDryContact,
                "SCS light point"   => OpenNettyAddressType.ScsLightPoint,
                "SCS scenario plus" => OpenNettyAddressType.ScsScenarioPlus,
                "Zigbee"            => OpenNettyAddressType.Zigbee,

                // Try to infer common address types if no explicit type was specified.
                null => unit?.Device.Definition.Protocol switch
                {
                    // Note: gateway endpoints don't have an address attached.
                    _ when unit?.Device is not null && unit.Device.GetUnit(0).HasCapability(OpenNettyCapabilities.OpenWebNetGateway)
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
                OpenNettyAddressType.Nitoo => OpenNettyProtocol.Nitoo,

                OpenNettyAddressType.ScsDryContact or
                OpenNettyAddressType.ScsLightPoint or
                OpenNettyAddressType.ScsScenarioPlus => OpenNettyProtocol.Scs,

                OpenNettyAddressType.Zigbee => OpenNettyProtocol.Zigbee,

                null => unit?.Device.Definition.Protocol ?? throw new InvalidOperationException(SR.FormatID0080(name, "Type")),

                _ => throw new InvalidOperationException(SR.FormatID0080(name, "Type"))
            };

            var address = type switch
            {
                OpenNettyAddressType.Nitoo when (uint?) element.Attribute("Id") is uint identifier
                    => OpenNettyAddress.FromNitooAddress(
                        identifier: identifier,
                        unit      : (byte?) (uint?) element.Attribute("Unit") ?? unit?.Definition.Id ?? 0),

                OpenNettyAddressType.Nitoo when unit?.Device?.Identifier is OpenNettyDeviceIdentifier identifier
                    => OpenNettyAddress.FromNitooAddress(
                        identifier: identifier,
                        unit      : (byte?) (uint?) element.Attribute("Unit") ?? unit?.Definition.Id ?? 0),

                OpenNettyAddressType.ScsDryContact when (byte?) (uint?) element.Attribute("Id") is byte identifier
                    => OpenNettyAddress.FromScsDryContactAddress(identifier),

                OpenNettyAddressType.ScsLightPoint => OpenNettyAddress.FromScsLightPointAddress(
                    extension: (byte?) (uint?) element.Attribute("Extension") ?? 0,
                    general  : (bool?) element.Attribute("General") ?? false,
                    group    : (byte?) (uint?) element.Attribute("Group"),
                    area     : (byte?) (uint?) element.Attribute("Area"),
                    point    : (byte?) (uint?) element.Attribute("Point")),

                OpenNettyAddressType.ScsScenarioPlus when (ushort?) (uint?) element.Attribute("Id") is ushort identifier
                    => OpenNettyAddress.FromScsScenarioPlusAddress(identifier),

                OpenNettyAddressType.Zigbee when (string?) element.Attribute("Id") is string identifier
                    => OpenNettyAddress.FromHexadecimalZigbeeAddress(
                        identifier: identifier,
                        unit      : (byte?) (uint?) element.Attribute("Unit") ?? unit?.Definition.Id ?? 0),

                OpenNettyAddressType.Zigbee when unit?.Device?.Identifier is OpenNettyDeviceIdentifier identifier
                    => OpenNettyAddress.FromZigbeeAddress(
                        identifier: identifier,
                        unit      : (byte?) (uint?) element.Attribute("Unit") ?? unit?.Definition.Id ?? 0),

                null => (OpenNettyAddress?) null,

                _ => throw new InvalidOperationException(SR.FormatID0080(name, "Type"))
            };

            return new OpenNettyEndpoint
            {
                Address = address,
                Capabilities = CreateCapabilities(element),
                Medium = unit?.Device?.Definition.Medium,
                Name = name ?? OpenNettyUtilities.ComputeDefaultEndpointName(protocol, address, unit),
                Protocol = protocol,
                Settings = CreateSettings(element),
                Unit = unit
            };
        }

        static OpenNettyGateway CreateGateway(OpenNettyDevice device, XElement element) => (string?) element.Attribute("Type") switch
        {
            "Serial" => OpenNettyGateway.Create(
                device: device,
                port  : new SerialPort(
                    portName: (string?) element.Attribute("Port") ?? throw new InvalidOperationException(SR.FormatID0075("Port")),
                    baudRate: (int?) element.Attribute("BaudRate") switch
                    {
                        int value => value,

                        null when device.Definition.GetUnitDefinition(0).GetIntegerSetting(OpenNettySettings.SerialPortBaudRate) is long setting
                            => (int) setting,

                        null => throw new InvalidOperationException(SR.FormatID0075("BaudRate")),
                    },
                    parity: (string?) element.Attribute("Parity") switch
                    {
                        "None"  => Parity.None,
                        "Odd"   => Parity.Odd,
                        "Even"  => Parity.Even,
                        "Mark"  => Parity.Mark,
                        "Space" => Parity.Space,

                        null when device.Definition.GetUnitDefinition(0).GetStringSetting(OpenNettySettings.SerialPortParity) is string value
                            => value switch
                            {
                                "None"  => Parity.None,
                                "Odd"   => Parity.Odd,
                                "Even"  => Parity.Even,
                                "Mark"  => Parity.Mark,
                                "Space" => Parity.Space,

                                _ => throw new InvalidOperationException(SR.FormatID0093(value))
                            },

                        null or { Length: 0 } => throw new InvalidOperationException(SR.FormatID0075("Parity")),

                        string value => throw new InvalidOperationException(SR.FormatID0093(value))
                    },
                    dataBits: (int?) element.Attribute("DataBits") switch
                    {
                        int value => value,

                        null when device.Definition.GetUnitDefinition(0).GetIntegerSetting(OpenNettySettings.SerialPortDataBits) is long value
                            => (int) value,

                        null => throw new InvalidOperationException(SR.FormatID0075("DataBits")),
                    },
                    stopBits: (string?) element.Attribute("StopBits") switch
                    {
                        "1"   => StopBits.One,
                        "1.5" => StopBits.OnePointFive,
                        "2"   => StopBits.Two,

                        null when device.Definition.GetUnitDefinition(0).GetStringSetting(OpenNettySettings.SerialPortStopBits) is string value
                            => value switch
                            {
                                "1"   => StopBits.One,
                                "1.5" => StopBits.OnePointFive,
                                "2"   => StopBits.Two,

                                _ => throw new InvalidOperationException(SR.FormatID0094(value))
                            },

                        null or { Length: 0 } => throw new InvalidOperationException(SR.FormatID0075("StopBits")),

                        string value => throw new InvalidOperationException(SR.FormatID0094(value))
                    })),

            "Tcp" when IPAddress.TryParse((string?) element.Attribute("Server"), out IPAddress? address)
                => OpenNettyGateway.Create(
                    device  : device,
                    endpoint: new IPEndPoint(address, port: (int?) element.Attribute("Port") ?? 20_000),
                    password: (string?) element.Attribute("Password")),

            "Tcp" => OpenNettyGateway.Create(
                device  : device,
                endpoint: new DnsEndPoint(
                    host: (string?) element.Attribute("Server") ?? throw new InvalidOperationException(SR.FormatID0076("Server")),
                    port: (int?) element.Attribute("Port") ?? 20_000),
                password: (string?) element.Attribute("Password")),

            null or { Length: 0 } => throw new InvalidOperationException(SR.FormatID0074("Type")),

            _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0077))
        };

        static OpenNettyScenario CreateScenario(XElement element) => new()
        {
            EndpointName = (string?) element.Attribute("EndpointName") ?? throw new InvalidOperationException(SR.FormatID0088("EndpointName")),
            FunctionCode = (byte?) (uint?) element.Attribute("FunctionCode") ?? throw new InvalidOperationException(SR.FormatID0088("FunctionCode"))
        };

        static ImmutableDictionary<OpenNettySetting, string> CreateSettings(XElement element)
            => element.Elements("Setting").ToImmutableDictionary(
                static element => new OpenNettySetting((string?) element.Attribute("Name") ?? throw new InvalidOperationException(SR.FormatID0086("Name"))),
                static element => (string?) element.Attribute("Value") ?? throw new InvalidOperationException(SR.FormatID0086("Name")));

        static OpenNettyGateway FindGatewayByName(IReadOnlyList<OpenNettyGateway> gateways, string name)
        {
            for (var index = 0; index < gateways.Count; index++)
            {
                var gateway = gateways[index];
                if (string.Equals(gateway.Device.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return gateway;
                }
            }

            throw new InvalidOperationException(SR.FormatID0089(name));
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
