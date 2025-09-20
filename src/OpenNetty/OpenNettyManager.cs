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
    /// Iterates all the devices registered in the options.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// An <see cref="IAsyncEnumerable{T}"/> that can be used to iterate the devices registered in the options.
    /// </returns>
    public virtual async IAsyncEnumerable<OpenNettyDevice> EnumerateDevicesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var device in _options.CurrentValue.Devices.ToAsyncEnumerable())
        {
            yield return device;
        }
    }

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
    /// <remarks>
    /// Note: the name lookup is case-sensitive.
    /// </remarks>
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

            if (string.Equals(_options.CurrentValue.Endpoints[index].Name, name, StringComparison.Ordinal))
            {
                if (endpoint is not null)
                {
                    return ValueTask.FromException<OpenNettyEndpoint?>(
                        new InvalidOperationException(SR.GetResourceString(SR.ID0102)));
                }

                endpoint = _options.CurrentValue.Endpoints[index];
            }
        }

        return ValueTask.FromResult(endpoint);
    }

    /// <summary>
    /// Resolves an endpoint using the specified predicate.
    /// </summary>
    /// <param name="predicate">The predicate.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation and whose result
    /// contains the resolved endpoint, or <see langword="null"/> if no matching endpoint could be resolved.
    /// </returns>
    public virtual ValueTask<OpenNettyEndpoint?> FindEndpointAsync(
        Func<OpenNettyEndpoint, bool> predicate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        OpenNettyEndpoint? endpoint = null;

        for (var index = 0; index < _options.CurrentValue.Endpoints.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<OpenNettyEndpoint?>(cancellationToken);
            }

            if (predicate(_options.CurrentValue.Endpoints[index]))
            {
                if (endpoint is not null)
                {
                    return ValueTask.FromException<OpenNettyEndpoint?>(
                        new InvalidOperationException(SR.GetResourceString(SR.ID0102)));
                }

                endpoint = _options.CurrentValue.Endpoints[index];
            }
        }

        return ValueTask.FromResult(endpoint);
    }

    /// <summary>
    /// Resolves an endpoint using the specified gateway and address.
    /// </summary>
    /// <param name="gateway">The gateway used to communicate with the endpoint.</param>
    /// <param name="address">The endpoint address.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation and whose result
    /// contains the resolved endpoint, or <see langword="null"/> if no matching endpoint could be resolved.
    /// </returns>
    public virtual ValueTask<OpenNettyEndpoint?> FindEndpointByAddressAsync(
        OpenNettyGateway gateway, OpenNettyAddress address, CancellationToken cancellationToken = default)
    {
        OpenNettyEndpoint? endpoint = null;

        for (var index = 0; index < _options.CurrentValue.Endpoints.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<OpenNettyEndpoint?>(cancellationToken);
            }

            if (_options.CurrentValue.Endpoints[index].Address == address &&
                _options.CurrentValue.Endpoints[index].Gateway == gateway)
            {
                if (endpoint is not null)
                {
                    return ValueTask.FromException<OpenNettyEndpoint?>(
                        new InvalidOperationException(SR.GetResourceString(SR.ID0102)));
                }

                endpoint = _options.CurrentValue.Endpoints[index];
            }
        }

        return ValueTask.FromResult(endpoint);
    }

    /// <summary>
    /// Resolves all the endpoints matching the specified gateway and address.
    /// </summary>
    /// <param name="gateway">The gateway used to communicate with the endpoint.</param>
    /// <param name="address">The endpoint address.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// An <see cref="IAsyncEnumerable{T}"/> that can be used to iterate the endpoints associated with the address.
    /// </returns>
    public virtual async IAsyncEnumerable<OpenNettyEndpoint> FindEndpointsByAddressAsync(
        OpenNettyGateway gateway, OpenNettyAddress address,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (address.Type is OpenNettyAddressType.Nitoo)
        {
            await foreach (var endpoint in EnumerateEndpointsAsync(cancellationToken))
            {
                if (endpoint.Protocol is OpenNettyProtocol.Nitoo &&
                    endpoint.Gateway == gateway &&
                    endpoint.Address is not null && endpoint.Address == address)
                {
                    yield return endpoint;
                }
            }
        }

        else if (address.Type is OpenNettyAddressType.ScsLightPoint)
        {
            var (extension, general, group, area, point) = OpenNettyAddress.ToScsLightPointAddress(address);

            await foreach (var endpoint in EnumerateEndpointsAsync(cancellationToken))
            {
                if (endpoint.Protocol is not OpenNettyProtocol.Scs || endpoint.Gateway != gateway || endpoint.Address is null)
                {
                    continue;
                }

                if (endpoint.Address == address)
                {
                    yield return endpoint;
                }

                else if (OpenNettyAddress.IsScsLightPointAreaAddress(address))
                {
                    var comparand = OpenNettyAddress.ToScsLightPointAddress(endpoint.Address.Value);

                    if ((OpenNettyAddress.IsScsLightPointAreaAddress(endpoint.Address.Value) ||
                         OpenNettyAddress.IsScsLightPointPointToPointAddress(endpoint.Address.Value)) &&
                        comparand.Extension == extension && comparand.Area == area)
                    {
                        yield return endpoint;
                    }
                }

                else if (OpenNettyAddress.IsScsLightPointGeneralAddress(address))
                {
                    var comparand = OpenNettyAddress.ToScsLightPointAddress(endpoint.Address.Value);

                    if ((OpenNettyAddress.IsScsLightPointAreaAddress(endpoint.Address.Value) ||
                         OpenNettyAddress.IsScsLightPointGeneralAddress(endpoint.Address.Value) ||
                         OpenNettyAddress.IsScsLightPointPointToPointAddress(endpoint.Address.Value)) &&
                        comparand.Extension == extension)
                    {
                        yield return endpoint;
                    }
                }
            }
        }

        else if (address.Type is OpenNettyAddressType.ScsScenarioPlus)
        {
            await foreach (var endpoint in EnumerateEndpointsAsync(cancellationToken))
            {
                if (endpoint.Protocol is OpenNettyProtocol.Scs &&
                    endpoint.Gateway == gateway &&
                    endpoint.Address is not null && endpoint.Address == address)
                {
                    yield return endpoint;
                }
            }
        }

        else if (address.Type is OpenNettyAddressType.Zigbee)
        {
            await foreach (var endpoint in EnumerateEndpointsAsync(cancellationToken))
            {
                if (endpoint.Protocol is not OpenNettyProtocol.Zigbee || endpoint.Gateway != gateway || endpoint.Address is null)
                {
                    continue;
                }

                if (endpoint.Address == address)
                {
                    yield return endpoint;
                }

                else if (OpenNettyAddress.ToZigbeeAddress(address) is not { Identifier: not 0, Unit: not 0 } &&
                    MatchesZigbeeAddress(address, endpoint.Address.Value))
                {
                    yield return endpoint;
                }
            }
        }

        static bool MatchesZigbeeAddress(OpenNettyAddress left, OpenNettyAddress right)
        {
            var first = OpenNettyAddress.ToZigbeeAddress(left);
            var second = OpenNettyAddress.ToZigbeeAddress(right);

            if (first is { Identifier: 0, Unit: not 0 })
            {
                return second.Unit == first.Unit;
            }

            else if (first is { Identifier: 0, Unit: 0 })
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
                    return ValueTask.FromException<OpenNettyGateway?>(
                        new InvalidOperationException(SR.GetResourceString(SR.ID0102)));
                }

                gateway = _options.CurrentValue.Gateways[index];
            }
        }

        return ValueTask.FromResult(gateway);
    }
}
