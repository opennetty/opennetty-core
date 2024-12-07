/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.IO.Ports;
using System.Net;

namespace OpenNetty;

/// <summary>
/// Represents an OpenNetty gateway.
/// </summary>
public sealed class OpenNettyGateway
{
    /// <summary>
    /// Gets or sets the type of connection used to communicate with the gateway.
    /// </summary>
    public required OpenNettyConnectionType ConnectionType { get; init; }

    /// <summary>
    /// Gets or sets the device associated with the gateway.
    /// </summary>
    public required OpenNettyDevice Device { get; init; }

    /// <summary>
    /// Gets or sets the password associated with the gateway, if applicable (SCS only).
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Gets the protocol implemented by the gateway.
    /// </summary>
    public OpenNettyProtocol Protocol => Device.Definition.Protocol;

    /// <summary>
    /// Gets or sets the serial port associated with the gateway, if applicable.
    /// </summary>
    public SerialPort? SerialPort { get; init; }

    /// <summary>
    /// Gets or sets the unique name associated with the gateway.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets or sets the IP endpoint associated with the gateway, if applicable.
    /// </summary>
    public IPEndPoint? IPEndpoint { get; init; }

    /// <summary>
    /// Gets or sets the options associated with the gateway.
    /// </summary>
    public required OpenNettyGatewayOptions Options { get; init; }

    /// <inheritdoc/>
    public bool Equals(OpenNettyGateway? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
            ConnectionType == other.ConnectionType &&
            Device == other.Device &&
            IPEndpoint == other.IPEndpoint &&
            string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Password, other.Password, StringComparison.Ordinal) &&
            Protocol == other.Protocol &&
            string.Equals(SerialPort?.PortName, other.SerialPort?.PortName, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyGateway gateway && Equals(gateway);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ConnectionType);
        hash.Add(Device);
        hash.Add(IPEndpoint);
        hash.Add(Name);
        hash.Add(Password);
        hash.Add(Protocol);
        hash.Add(SerialPort?.PortName);

        return hash.ToHashCode();
    }

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current gateway.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current gateway.</returns>
    public override string ToString() => Name;

    /// <summary>
    /// Determines whether two <see cref="OpenNettyGateway"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyGateway? left, OpenNettyGateway? right)
        => ReferenceEquals(left, right) || (left is not null && right is not null && left.Equals(right));

    /// <summary>
    /// Determines whether two <see cref="OpenNettyGateway"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyGateway? left, OpenNettyGateway? right) => !(left == right);

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyGateway"/>
    /// class using the specified Internet Protocol endpoint.
    /// </summary>
    /// <param name="name">The gateway name.</param>
    /// <param name="device">The gateway device.</param>
    /// <param name="endpoint">The Internet Protocol endpoint.</param>
    /// <param name="password">The authentication password, if applicable.</param>
    /// <param name="options">The gateway options.</param>
    /// <returns>A new instance of the <see cref="OpenNettyGateway"/> class.</returns>
    public static OpenNettyGateway Create(
        string name,
        OpenNettyDevice device,
        IPEndPoint endpoint,
        string? password = null,
        OpenNettyGatewayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return new OpenNettyGateway
        {
            ConnectionType = OpenNettyConnectionType.Tcp,
            Device = device,
            IPEndpoint = endpoint,
            Name = name,
            Options = options ?? OpenNettyGatewayOptions.CreateDefaults(device),
            Password = password
        };
    }

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyGateway"/> class using the specified serial port.
    /// </summary>
    /// <param name="name">The gateway name.</param>
    /// <param name="device">The gateway device.</param>
    /// <param name="port">The serial port.</param>
    /// <param name="options">The gateway options.</param>
    /// <returns>A new instance of the <see cref="OpenNettyGateway"/> class.</returns>
    public static OpenNettyGateway Create(
        string name,
        OpenNettyDevice device,
        SerialPort port,
        OpenNettyGatewayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(port);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return new OpenNettyGateway
        {
            ConnectionType = OpenNettyConnectionType.Serial,
            Device = device,
            Name = name,
            Options = options ?? OpenNettyGatewayOptions.CreateDefaults(device),
            SerialPort = port
        };
    }

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyGateway"/>
    /// class using the specified Internet Protocol endpoint.
    /// </summary>
    /// <param name="name">The gateway name.</param>
    /// <param name="brand">The gateway brand.</param>
    /// <param name="model">The gateway model.</param>
    /// <param name="endpoint">The Internet Protocol endpoint.</param>
    /// <param name="password">The authentication password, if applicable.</param>
    /// <param name="options">The gateway options.</param>
    /// <returns>A new instance of the <see cref="OpenNettyGateway"/> class.</returns>
    public static OpenNettyGateway Create(
        string name,
        OpenNettyBrand brand,
        string model,
        IPEndPoint endpoint,
        string? password = null,
        OpenNettyGatewayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(model);

        var device = new OpenNettyDevice
        {
            Definition = OpenNettyDevices.GetDeviceByModel(brand, model) ??
                throw new InvalidOperationException(SR.FormatID0098(brand, model))
        };

        return Create(name, device, endpoint, password, options);
    }

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyGateway"/> class using the specified serial port.
    /// </summary>
    /// <param name="name">The gateway name.</param>
    /// <param name="brand">The gateway brand.</param>
    /// <param name="model">The gateway model.</param>
    /// <param name="port">The serial port.</param>
    /// <param name="options">The gateway options.</param>
    /// <returns>A new instance of the <see cref="OpenNettyGateway"/> class.</returns>
    public static OpenNettyGateway Create(
        string name,
        OpenNettyBrand brand,
        string model,
        SerialPort port,
        OpenNettyGatewayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(model);

        var device = new OpenNettyDevice
        {
            Definition = OpenNettyDevices.GetDeviceByModel(brand, model) ??
                throw new InvalidOperationException(SR.FormatID0098(brand, model))
        };

        return Create(name, device, port, options);
    }
}
