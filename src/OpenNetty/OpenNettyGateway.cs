/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Diagnostics.CodeAnalysis;
using System.IO.Ports;
using System.Net;

namespace OpenNetty;

/// <summary>
/// Represents an OpenNetty gateway.
/// </summary>
public sealed record class OpenNettyGateway : IEquatable<OpenNettyGateway>
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
    /// Gets or sets the DNS or IP endpoint associated with the gateway, if applicable.
    /// </summary>
    public EndPoint? Endpoint { get; init; }

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
    /// Gets or sets the options associated with the gateway.
    /// </summary>
    public required OpenNettyGatewayOptions Options { get; init; }

    /// <inheritdoc/>
    /// <remarks>
    /// Note: two gateways are considered equal if they have the same connection type,
    /// device, endpoint, protocol, and serial port, regardless of their other properties.
    /// </remarks>
    public bool Equals([NotNullWhen(true)] OpenNettyGateway? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
            ConnectionType == other.ConnectionType &&
            Device == other.Device &&
            Endpoint == other.Endpoint &&
            Protocol == other.Protocol &&
            string.Equals(SerialPort?.PortName, other.SerialPort?.PortName, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ConnectionType);
        hash.Add(Device);
        hash.Add(Endpoint?.Serialize());
        hash.Add(Protocol);
        hash.Add(SerialPort?.PortName, StringComparer.OrdinalIgnoreCase);

        return hash.ToHashCode();
    }

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current gateway.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current gateway.</returns>
    public override string ToString() => Device.Name ?? string.Empty;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyGateway"/> class using the specified endpoint.
    /// </summary>
    /// <param name="device">The gateway device.</param>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="password">The authentication password, if applicable.</param>
    /// <param name="options">The gateway options.</param>
    /// <returns>A new instance of the <see cref="OpenNettyGateway"/> class.</returns>
    public static OpenNettyGateway Create(
        OpenNettyDevice device,
        EndPoint endpoint,
        string? password = null,
        OpenNettyGatewayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(endpoint);

        return new OpenNettyGateway
        {
            ConnectionType = OpenNettyConnectionType.Tcp,
            Device = device,
            Endpoint = endpoint,
            Options = options ?? OpenNettyGatewayOptions.CreateDefaults(device),
            Password = password
        };
    }

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyGateway"/> class using the specified serial port.
    /// </summary>
    /// <param name="device">The gateway device.</param>
    /// <param name="port">The serial port.</param>
    /// <param name="options">The gateway options.</param>
    /// <returns>A new instance of the <see cref="OpenNettyGateway"/> class.</returns>
    public static OpenNettyGateway Create(
        OpenNettyDevice device,
        SerialPort port,
        OpenNettyGatewayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(port);

        return new OpenNettyGateway
        {
            ConnectionType = OpenNettyConnectionType.Serial,
            Device = device,
            Options = options ?? OpenNettyGatewayOptions.CreateDefaults(device),
            SerialPort = port
        };
    }
}
