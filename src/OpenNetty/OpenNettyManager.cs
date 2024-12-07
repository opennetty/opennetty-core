/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;

namespace OpenNetty;

/// <summary>
/// Provides an easy way to resolve endpoints based on their name or address.
/// </summary>
public class OpenNettyManager
{
    private readonly IOptionsMonitor<OpenNettyOptions> _options;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyManager"/> class.
    /// </summary>
    /// <param name="options">The OpenNetty options.</param>
    public OpenNettyManager(IOptionsMonitor<OpenNettyOptions> options)
        => _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Iterates all the endpoints registered in the options.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// An <see cref="IAsyncEnumerable{T}"/> that can be used to iterate the endpoints registered in the options.
    /// </returns>
    public virtual async IAsyncEnumerable<OpenNettyEndpoint> EnumerateEndpointsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var endpoint in _options.CurrentValue.Endpoints.ToAsyncEnumerable())
        {
            yield return endpoint;
        }
    }

    /// <summary>
    /// Iterates all the gateways registered in the options.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// An <see cref="IAsyncEnumerable{T}"/> that can be used to iterate the gateways registered in the options.
    /// </returns>
    public virtual async IAsyncEnumerable<OpenNettyGateway> EnumerateGatewaysAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var gateway in _options.CurrentValue.Gateways.ToAsyncEnumerable())
        {
            yield return gateway;
        }
    }

    /// <summary>
    /// Resolves an endpoint using the specified name.
    /// </summary>
    /// <param name="name">The endpoint name.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation and whose result
    /// contains the resolved endpoint, or <see langword="null"/> if no matching endpoint could be resolved.
    /// </returns>
    public virtual ValueTask<OpenNettyEndpoint?> FindEndpointByNameAsync(
        string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        OpenNettyEndpoint? endpoint = null;

        for (var index = 0; index < _options.CurrentValue.Endpoints.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<OpenNettyEndpoint?>(cancellationToken);
            }

            if (string.Equals(_options.CurrentValue.Endpoints[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                if (endpoint is not null)
                {
                    return ValueTask.FromException<OpenNettyEndpoint?>(new InvalidOperationException(
                        "Multiple endpoints matching the specified address exist."));
                }

                endpoint = _options.CurrentValue.Endpoints[index];
            }
        }

        return ValueTask.FromResult(endpoint);
    }

    /// <summary>
    /// Resolves an endpoint using the specified address.
    /// </summary>
    /// <param name="address">The endpoint address.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation and whose result
    /// contains the resolved endpoint, or <see langword="null"/> if no matching endpoint could be resolved.
    /// </returns>
    public virtual ValueTask<OpenNettyEndpoint?> FindEndpointByAddressAsync(
        OpenNettyAddress address, CancellationToken cancellationToken = default)
    {
        OpenNettyEndpoint? endpoint = null;

        for (var index = 0; index < _options.CurrentValue.Endpoints.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<OpenNettyEndpoint?>(cancellationToken);
            }

            if (_options.CurrentValue.Endpoints[index].Address == address)
            {
                if (endpoint is not null)
                {
                    return ValueTask.FromException<OpenNettyEndpoint?>(new InvalidOperationException(
                        "Multiple endpoints matching the specified address exist."));
                }

                endpoint = _options.CurrentValue.Endpoints[index];
            }
        }

        return ValueTask.FromResult(endpoint);
    }

    /// <summary>
    /// Resolves all the endpoints matching the specified address.
    /// </summary>
    /// <param name="address">The endpoint address.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// An <see cref="IAsyncEnumerable{T}"/> that can be used to iterate the endpoints associated with the address.
    /// </returns>
    public virtual async IAsyncEnumerable<OpenNettyEndpoint> FindEndpointsByAddressAsync(
        OpenNettyAddress address, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (address.Type is OpenNettyAddressType.ScsLightPointArea)
        {
            var (extension, area) = OpenNettyAddress.ToScsLightPointAreaAddress(address);

            await foreach (var endpoint in EnumerateEndpointsAsync(cancellationToken))
            {
                if (endpoint.Protocol is not OpenNettyProtocol.Scs || endpoint.Address is null)
                {
                    continue;
                }

                if (endpoint.Address == address)
                {
                    yield return endpoint;
                }

                switch (endpoint.Address?.Type)
                {
                    case OpenNettyAddressType.ScsLightPointArea when
                        OpenNettyAddress.ToScsLightPointAreaAddress(endpoint.Address.Value) is var comparand &&
                        comparand.Extension == extension && comparand.Area == area:
                        yield return endpoint;
                        break;

                    case OpenNettyAddressType.ScsLightPointPointToPoint when
                        OpenNettyAddress.ToScsLightPointPointToPointAddress(endpoint.Address.Value) is var comparand &&
                        comparand.Extension == extension && comparand.Area == area:
                        yield return endpoint;
                        break;
                }
            }
        }

        else if (address.Type is OpenNettyAddressType.ScsLightPointGeneral)
        {
            var extension = OpenNettyAddress.ToScsLightPointGeneralAddress(address);

            await foreach (var endpoint in EnumerateEndpointsAsync(cancellationToken))
            {
                if (endpoint.Protocol is not OpenNettyProtocol.Scs || endpoint.Address is null)
                {
                    continue;
                }

                if (endpoint.Address == address)
                {
                    yield return endpoint;
                }

                switch (endpoint.Address?.Type)
                {
                    case OpenNettyAddressType.ScsLightPointArea when
                        OpenNettyAddress.ToScsLightPointAreaAddress(endpoint.Address.Value) is var comparand &&
                        comparand.Extension == extension:
                        yield return endpoint;
                        break;

                    case OpenNettyAddressType.ScsLightPointGeneral when
                        OpenNettyAddress.ToScsLightPointGeneralAddress(endpoint.Address.Value) is var comparand &&
                        comparand == extension:
                        yield return endpoint;
                        break;

                    case OpenNettyAddressType.ScsLightPointPointToPoint when
                        OpenNettyAddress.ToScsLightPointPointToPointAddress(endpoint.Address.Value) is var comparand &&
                        comparand.Extension == extension:
                        yield return endpoint;
                        break;
                }
            }
        }

        else if (address.Type is OpenNettyAddressType.ZigbeeAllDevicesAllUnits     or
                                 OpenNettyAddressType.ZigbeeAllDevicesSpecificUnit or
                                 OpenNettyAddressType.ZigbeeSpecificDeviceAllUnits)
        {
            await foreach (var endpoint in EnumerateEndpointsAsync(cancellationToken))
            {
                if (endpoint.Protocol is not OpenNettyProtocol.Zigbee || endpoint.Address is null)
                {
                    continue;
                }

                if (endpoint.Address == address)
                {
                    yield return endpoint;
                }

                if (MatchesZigbeeAddress(address, endpoint.Address.Value))
                {
                    yield return endpoint;
                }
            }
        }

        else
        {
            await foreach (var endpoint in EnumerateEndpointsAsync(cancellationToken))
            {
                if (endpoint.Address is not null && endpoint.Address == address)
                {
                    yield return endpoint;
                }
            }
        }

        static bool MatchesZigbeeAddress(OpenNettyAddress left, OpenNettyAddress right)
        {
            var first = OpenNettyAddress.ToZigbeeAddress(left);
            var second = OpenNettyAddress.ToZigbeeAddress(right);

            if (first is { Identifier: null, Unit: not 0 })
            {
                return second.Unit == first.Unit;
            }

            else if (first is { Identifier: null, Unit: 0 })
            {
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Resolves a gateway using the specified name.
    /// </summary>
    /// <param name="name">The gateway name.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation and whose result
    /// contains the resolved gateway, or <see langword="null"/> if no matching gateway could be resolved.
    /// </returns>
    public virtual ValueTask<OpenNettyGateway?> FindGatewayByNameAsync(
        string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        OpenNettyGateway? gateway = null;

        for (var index = 0; index < _options.CurrentValue.Gateways.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<OpenNettyGateway?>(cancellationToken);
            }

            if (string.Equals(_options.CurrentValue.Gateways[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                if (gateway is not null)
                {
                    return ValueTask.FromException<OpenNettyGateway?>(new InvalidOperationException(
                        "Multiple gateways matching the specified address exist."));
                }

                gateway = _options.CurrentValue.Gateways[index];
            }
        }

        return ValueTask.FromResult(gateway);
    }
}
