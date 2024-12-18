/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Polly;

namespace OpenNetty;

/// <summary>
/// Represents a low-level service that can be used to send and receive common OpenWebNet messages.
/// </summary>
public class OpenNettyService : IOpenNettyService
{
    private readonly OpenNettyLogger<OpenNettyService> _logger;
    private readonly IOptionsMonitor<OpenNettyOptions> _options;
    private readonly IOpenNettyPipeline _pipeline;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyService"/> class.
    /// </summary>
    /// <param name="logger">The OpenNetty logger.</param>
    /// <param name="options">The OpenNetty options.</param>
    /// <param name="pipeline">The OpenNetty pipeline.</param>
    public OpenNettyService(
        OpenNettyLogger<OpenNettyService> logger,
        IOptionsMonitor<OpenNettyOptions> options,
        IOpenNettyPipeline pipeline)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc/>
    public virtual async IAsyncEnumerable<(OpenNettyAddress Address, ImmutableArray<string> Values)> EnumerateDimensionsAsync(
        OpenNettyProtocol protocol,
        OpenNettyDimension dimension,
        OpenNettyAddress? address = null,
        OpenNettyMedium? medium = null,
        OpenNettyMode? mode = null,
        Func<OpenNettyDimension, ValueTask<bool>>? filter = null,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions options = OpenNettyTransmissionOptions.None,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0064));
        }

        if (protocol is OpenNettyProtocol.Nitoo)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0030));
        }

        if (gateway is not null && gateway.Protocol != protocol)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0031));
        }

        // If no gateway was explicitly specified, try to resolve it from the options.
        gateway ??= _options.CurrentValue.Gateways.Find(gateway => gateway.Protocol == protocol) ??
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0032));

        var message = OpenNettyMessage.CreateDimensionRequest(protocol, dimension, address, medium, mode);

        // Note: acknowledgement validation is deliberately disabled while sending the DIMENSION REQUEST frame
        // as it's used by the OWN gateway to indicate when it's done pushing additional DIMENSION READ frames.
        options |= OpenNettyTransmissionOptions.IgnoreAcknowledgementValidation;

        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)), gateway);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyLogger<OpenNettyService>>(nameof(OpenNettyLogger<OpenNettyService>)), _logger);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyMessage>(nameof(OpenNettyMessage)), message);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyTransmissionOptions>(nameof(OpenNettyTransmissionOptions)), options);

        // Monitor ACKNOWLEDGEMENT and DIMENSION READ replies received by generic and command sessions.
        var notifications = _pipeline.Where(notification => notification.Gateway == gateway)
            .SelectMany(async notification => notification switch
            {
                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol: OpenNettyProtocol.Scs, Type: OpenNettySessionType.Command } session,
                    Message: { Type    : OpenNettyMessageType.Acknowledgement or
                                         OpenNettyMessageType.NegativeAcknowledgement } message }
                    when message.Protocol == protocol
                        => AsyncObservable.Return<(OpenNettySession Session, OpenNettyMessage Message)>((session, message)),

                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol : OpenNettyProtocol.Scs, Type: OpenNettySessionType.Command } session,
                    Message: { Type     : OpenNettyMessageType.DimensionRead,
                               Address  : not null,
                               Dimension: not null } message }
                    // Note: if a filter was not explicitly set, filter out dimensions that don't match the requested one.
                    when message.Protocol == protocol && (filter is null || await filter(message.Dimension.Value))
                        => AsyncObservable.Return<(OpenNettySession Session, OpenNettyMessage Message)>((session, message)),

                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol: OpenNettyProtocol.Zigbee, Type: OpenNettySessionType.Generic } session,
                    Message: { Type    : OpenNettyMessageType.Acknowledgement             or
                                         OpenNettyMessageType.BusyNegativeAcknowledgement or
                                         OpenNettyMessageType.NegativeAcknowledgement } message }
                    when message.Protocol == protocol
                        => AsyncObservable.Return<(OpenNettySession Session, OpenNettyMessage Message)>((session, message)),

                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol : OpenNettyProtocol.Zigbee, Type: OpenNettySessionType.Generic } session,
                    Message: { Type     : OpenNettyMessageType.DimensionRead,
                               Address  : not null,
                               Dimension: not null } message }
                    // Note: if a filter was not explicitly set, filter out dimensions that don't match the requested one.
                    when message.Protocol == protocol && (filter is null || await filter(message.Dimension.Value))
                        => AsyncObservable.Return<(OpenNettySession Session, OpenNettyMessage Message)>((session, message)),

                _ => AsyncObservable.Empty<(OpenNettySession Session, OpenNettyMessage Message)>()
            })
            .Replay();

        // Connect the observable before sending the message to ensure
        // the notifications are not missed due to a race condition.
        await using var connection = await notifications.ConnectAsync();

        OpenNettySession session;

        try
        {
            session = await gateway.Options.OutgoingMessageResiliencePipeline.ExecuteAsync(async context =>
                await SendRawMessageAsync(message, gateway, options, context.CancellationToken), context);
        }

        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }

        await foreach (var notification in notifications
            .Where(notification => notification.Session == session)
            .OfType<(OpenNettySession Session, OpenNettyMessage Message), (OpenNettySession Session, OpenNettyMessage Message)?>()
            .Timeout(gateway.Options.MultipleDimensionReplyTimeout, AsyncObservable.Return<(OpenNettySession Session, OpenNettyMessage Message)?>(null))
            .ToAsyncEnumerable())
        {
            switch (notification?.Message.Type)
            {
                case null or OpenNettyMessageType.Acknowledgement:
                    yield break;

                case OpenNettyMessageType.BusyNegativeAcknowledgement:
                    throw new OpenNettyException(OpenNettyErrorCode.GatewayBusy, SR.GetResourceString(SR.ID0017));

                case OpenNettyMessageType.NegativeAcknowledgement:
                    throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0018));
            }

            yield return (notification.Value.Message.Address!.Value, notification.Value.Message.Values);
        }
    }

    /// <inheritdoc/>
    public virtual async IAsyncEnumerable<(OpenNettyAddress Address, OpenNettyCommand Command)> EnumerateStatusesAsync(
        OpenNettyProtocol protocol,
        OpenNettyCategory category,
        OpenNettyAddress? address = null,
        OpenNettyMedium? medium = null,
        OpenNettyMode? mode = null,
        Func<OpenNettyCommand, ValueTask<bool>>? filter = null,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions options = OpenNettyTransmissionOptions.None,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0064));
        }

        if (protocol is OpenNettyProtocol.Nitoo)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0030));
        }

        if (gateway is not null && gateway.Protocol != protocol)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0031));
        }

        // If no gateway was explicitly specified, try to resolve it from the options.
        gateway ??= _options.CurrentValue.Gateways.Find(gateway => gateway.Protocol == protocol) ??
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0032));

        var message = OpenNettyMessage.CreateStatusRequest(protocol, category, address, medium, mode);

        // Note: acknowledgement validation is deliberately disabled while sending the STATUS REQUEST frame
        // as it's used by the gateway to indicate when it's done pushing additional BUS COMMAND frames.
        options |= OpenNettyTransmissionOptions.IgnoreAcknowledgementValidation;

        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)), gateway);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyLogger<OpenNettyService>>(nameof(OpenNettyLogger<OpenNettyService>)), _logger);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyMessage>(nameof(OpenNettyMessage)), message);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyTransmissionOptions>(nameof(OpenNettyTransmissionOptions)), options);

        // Monitor ACKNOWLEDGEMENT and BUS COMMAND replies received by generic and command sessions.
        var notifications = _pipeline.Where(notification => notification.Gateway == gateway)
            .SelectMany(async notification => notification switch
            {
                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol: OpenNettyProtocol.Scs, Type: OpenNettySessionType.Command } session,
                    Message: { Type    : OpenNettyMessageType.Acknowledgement or
                                         OpenNettyMessageType.NegativeAcknowledgement } message }
                    when message.Protocol == protocol
                        => AsyncObservable.Return<(OpenNettySession Session, OpenNettyMessage Message)>((session, message)),

                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol: OpenNettyProtocol.Scs, Type: OpenNettySessionType.Command } session,
                    Message: { Type    : OpenNettyMessageType.BusCommand,
                               Command : not null,
                               Address : not null } message }
                    when message.Protocol == protocol &&
                        // Note: if a filter was not explicitly set, filter out commands whose category doesn't match the requested one.
                        (filter is not null ? await filter(message.Command.Value) : message.Command.Value.Category == category)
                        => AsyncObservable.Return<(OpenNettySession Session, OpenNettyMessage Message)>((session, message)),

                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol: OpenNettyProtocol.Zigbee, Type: OpenNettySessionType.Generic } session,
                    Message: { Type    : OpenNettyMessageType.Acknowledgement             or
                                         OpenNettyMessageType.BusyNegativeAcknowledgement or
                                         OpenNettyMessageType.NegativeAcknowledgement } message }
                    when message.Protocol == protocol
                        => AsyncObservable.Return<(OpenNettySession Session, OpenNettyMessage Message)>((session, message)),

                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol: OpenNettyProtocol.Zigbee, Type: OpenNettySessionType.Generic } session,
                    Message: { Type    : OpenNettyMessageType.BusCommand,
                               Command : not null,
                               Address : not null } message }
                    when message.Protocol == protocol &&
                        // Note: if a filter was not explicitly set, filter out commands whose category doesn't match the requested one.
                        (filter is not null ? await filter(message.Command.Value) : message.Command.Value.Category == category)
                        => AsyncObservable.Return<(OpenNettySession Session, OpenNettyMessage Message)>((session, message)),

                _ => AsyncObservable.Empty<(OpenNettySession Session, OpenNettyMessage Message)>()
            })
            .Replay();

        // Connect the observable before sending the message to ensure
        // the notifications are not missed due to a race condition.
        await using var connection = await notifications.ConnectAsync();

        OpenNettySession session;

        try
        {
            session = await gateway.Options.OutgoingMessageResiliencePipeline.ExecuteAsync(async context =>
                await SendRawMessageAsync(message, gateway, options, context.CancellationToken), context);
        }

        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }

        await foreach (var notification in notifications
            .Where(notification => notification.Session == session)
            .OfType<(OpenNettySession Session, OpenNettyMessage Message), (OpenNettySession Session, OpenNettyMessage Message)?>()
            .Timeout(gateway.Options.MultipleStatusReplyTimeout, AsyncObservable.Return<(OpenNettySession Session, OpenNettyMessage Message)?>(null))
            .ToAsyncEnumerable())
        {
            switch (notification?.Message.Type)
            {
                case null or OpenNettyMessageType.Acknowledgement:
                    yield break;

                case OpenNettyMessageType.BusyNegativeAcknowledgement:
                    throw new OpenNettyException(OpenNettyErrorCode.GatewayBusy, SR.GetResourceString(SR.ID0017));

                case OpenNettyMessageType.NegativeAcknowledgement:
                    throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0018));
            }

            yield return (notification.Value.Message.Address!.Value, notification.Value.Message.Command!.Value);
        }
    }

    /// <inheritdoc/>
    public virtual async ValueTask ExecuteCommandAsync(
        OpenNettyProtocol protocol,
        OpenNettyCommand command,
        OpenNettyAddress? address = null,
        OpenNettyMedium? medium = null,
        OpenNettyMode? mode = null,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions options = OpenNettyTransmissionOptions.None,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0064));
        }

        if (gateway is not null && gateway.Protocol != protocol)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0031));
        }

        // If no gateway was explicitly specified, try to resolve it from the options.
        gateway ??= _options.CurrentValue.Gateways.Find(gateway => gateway.Protocol == protocol) ??
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0032));

        var message = OpenNettyMessage.CreateCommand(protocol, command, address, medium, mode);

        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)), gateway);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyLogger<OpenNettyService>>(nameof(OpenNettyLogger<OpenNettyService>)), _logger);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyMessage>(nameof(OpenNettyMessage)), message);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyTransmissionOptions>(nameof(OpenNettyTransmissionOptions)), options);

        try
        {
            await gateway.Options.OutgoingMessageResiliencePipeline.ExecuteAsync(async context =>
                await SendRawMessageAsync(message, gateway, options, context.CancellationToken), context);
        }

        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    /// <inheritdoc/>
    public virtual async ValueTask<ImmutableArray<string>> GetDimensionAsync(
        OpenNettyProtocol protocol,
        OpenNettyDimension dimension,
        OpenNettyAddress? address = null,
        OpenNettyMedium? medium = null,
        OpenNettyMode? mode = null,
        Func<OpenNettyDimension, ValueTask<bool>>? filter = null,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions options = OpenNettyTransmissionOptions.None,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0064));
        }

        if (gateway is not null && gateway.Protocol != protocol)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0031));
        }

        // If no gateway was explicitly specified, try to resolve it from the options.
        gateway ??= _options.CurrentValue.Gateways.Find(gateway => gateway.Protocol == protocol) ??
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0032));

        var message = OpenNettyMessage.CreateDimensionRequest(protocol, dimension, address, medium, mode);

        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)), gateway);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyLogger<OpenNettyService>>(nameof(OpenNettyLogger<OpenNettyService>)), _logger);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyMessage>(nameof(OpenNettyMessage)), message);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyTransmissionOptions>(nameof(OpenNettyTransmissionOptions)), options);

        // Monitor ACKNOWLEDGEMENT and DIMENSION READ replies sent by the same address
        // as the message was sent to and received by generic and command sessions.
        var notifications = _pipeline.Where(notification => notification.Gateway == gateway)
            .SelectMany(async notification => notification switch
            {
                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol : OpenNettyProtocol.Scs, Type: OpenNettySessionType.Command } session,
                    Message: { Type     : OpenNettyMessageType.DimensionRead,
                               Dimension: not null } message }
                    when message.Protocol == protocol && message.Address == address &&
                        // Note: if a filter was not explicitly set, filter out dimensions that don't match the requested one.
                        (filter is not null ? await filter(message.Dimension.Value) : message.Dimension.Value == dimension)
                        => AsyncObservable.Return<(OpenNettySession Session, OpenNettyMessage Message)>((session, message)),

                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol : OpenNettyProtocol.Nitoo or OpenNettyProtocol.Zigbee, Type: OpenNettySessionType.Generic } session,
                    Message: { Type     : OpenNettyMessageType.DimensionRead,
                               Dimension: not null } message }
                    when message.Protocol == protocol && message.Address == address &&
                        // Note: if a filter was not explicitly set, filter out dimensions that don't match the requested one.
                        (filter is not null ? await filter(message.Dimension.Value) : message.Dimension.Value == dimension)
                        => AsyncObservable.Return<(OpenNettySession Session, OpenNettyMessage Message)>((session, message)),

                _ => AsyncObservable.Empty<(OpenNettySession Session, OpenNettyMessage Message)>()
            })
            .Replay();

        // Connect the observable before sending the message to ensure
        // the notifications are not missed due to a race condition.
        await using var connection = await notifications.ConnectAsync();

        try
        {
            return await gateway.Options.OutgoingMessageResiliencePipeline.ExecuteAsync(async context =>
            {
                var session = await SendRawMessageAsync(message, gateway, options, context.CancellationToken);

                return (await notifications
                    .FirstOrDefault(notification => notification.Session == session)
                    .OfType<(OpenNettySession Session, OpenNettyMessage Message), (OpenNettySession Session, OpenNettyMessage Message)?>()
                    .Timeout(gateway.Options.UniqueDimensionReplyTimeout, AsyncObservable.Return<(OpenNettySession Session, OpenNettyMessage Message)?>(null))
                    .RunAsync(cancellationToken))?.Message.Values ?? throw new OpenNettyException(
                        OpenNettyErrorCode.NoDimensionReceived, SR.GetResourceString(SR.ID0033));
            }, context);
        }

        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    /// <inheritdoc/>
    public virtual async ValueTask<OpenNettyCommand> GetStatusAsync(
        OpenNettyProtocol protocol,
        OpenNettyCategory category,
        OpenNettyAddress? address = null,
        OpenNettyMedium? medium = null,
        OpenNettyMode? mode = null,
        Func<OpenNettyCommand, ValueTask<bool>>? filter = null,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions options = OpenNettyTransmissionOptions.None,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0064));
        }

        if (gateway is not null && gateway.Protocol != protocol)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0031));
        }

        // If no gateway was explicitly specified, try to resolve it from the options.
        gateway ??= _options.CurrentValue.Gateways.Find(gateway => gateway.Protocol == protocol) ??
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0032));

        var message = OpenNettyMessage.CreateStatusRequest(protocol, category, address, medium, mode);

        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)), gateway);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyLogger<OpenNettyService>>(nameof(OpenNettyLogger<OpenNettyService>)), _logger);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyMessage>(nameof(OpenNettyMessage)), message);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyTransmissionOptions>(nameof(OpenNettyTransmissionOptions)), options);

        // Monitor ACKNOWLEDGEMENT and BUS COMMAND replies sent by the same address
        // as the message was sent to and received by generic and command sessions.
        var notifications = _pipeline.Where(notification => notification.Gateway == gateway)
            .SelectMany(async notification => notification switch
            {
                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol : OpenNettyProtocol.Scs, Type: OpenNettySessionType.Command } session,
                    Message: { Type     : OpenNettyMessageType.BusCommand,
                               Command  : OpenNettyCommand command } message }
                    when message.Protocol == protocol && message.Address == address &&
                        // Note: if a filter was not explicitly set, filter out commands whose category doesn't match the requested one.
                        (filter is not null ? await filter(command) : command.Category == category)
                        => AsyncObservable.Return<(OpenNettySession Session, OpenNettyMessage Message)>((session, message)),

                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol : OpenNettyProtocol.Nitoo or OpenNettyProtocol.Zigbee, Type: OpenNettySessionType.Generic } session,
                    Message: { Type     : OpenNettyMessageType.BusCommand,
                               Command  : OpenNettyCommand command } message }
                    when message.Protocol == protocol && message.Address == address &&
                        // Note: if a filter was not explicitly set, filter out commands whose category doesn't match the requested one.
                        (filter is not null ? await filter(command) : command.Category == category)
                        => AsyncObservable.Return<(OpenNettySession Session, OpenNettyMessage Message)>((session, message)),

                _ => AsyncObservable.Empty<(OpenNettySession Session, OpenNettyMessage Message)>()
            })
            .Replay();

        // Connect the observable before sending the message to ensure
        // the notifications are not missed due to a race condition.
        await using var connection = await notifications.ConnectAsync();

        try
        {
            return await gateway.Options.OutgoingMessageResiliencePipeline.ExecuteAsync(async context =>
            {
                var session = await SendRawMessageAsync(message, gateway, options, context.CancellationToken);

                return (await notifications
                    .FirstOrDefault(notification => notification.Session == session)
                    .OfType<(OpenNettySession Session, OpenNettyMessage Message), (OpenNettySession Session, OpenNettyMessage Message)?>()
                    .Timeout(gateway.Options.UniqueStatusReplyTimeout, AsyncObservable.Return<(OpenNettySession Session, OpenNettyMessage Message)?>(null))
                    .RunAsync(cancellationToken))?.Message.Command ?? throw new OpenNettyException(
                        OpenNettyErrorCode.NoStatusReceived, SR.GetResourceString(SR.ID0034));
            }, context);
        }

        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    /// <inheritdoc/>
    public virtual IAsyncObservable<(OpenNettyAddress? Address, OpenNettyCommand Command)> ObserveStatusesAsync(
        OpenNettyProtocol protocol,
        OpenNettyCategory category,
        OpenNettyGateway? gateway = null)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0064));
        }

        if (gateway is not null && gateway.Protocol != protocol)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0031));
        }

        return _pipeline.Where(notification => gateway is null || notification.Gateway == gateway)
            .SelectMany(notification => notification switch
            {
                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol: OpenNettyProtocol.Scs, Type: OpenNettySessionType.Command } session,
                    Message: { Type    : OpenNettyMessageType.BusCommand,
                               Command : OpenNettyCommand command,
                               Address : OpenNettyAddress address } message }
                    when message.Protocol == protocol && message.Category == category
                        => AsyncObservable.Return<(OpenNettyAddress? Address, OpenNettyCommand Command)>((address, command)),

                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol: OpenNettyProtocol.Nitoo or OpenNettyProtocol.Zigbee, Type: OpenNettySessionType.Generic } session,
                    Message: { Type    : OpenNettyMessageType.BusCommand,
                               Command : OpenNettyCommand command,
                               Address : OpenNettyAddress address } message }
                    when message.Protocol == protocol && message.Category == category
                        => AsyncObservable.Return<(OpenNettyAddress? Address, OpenNettyCommand Command)>((address, command)),

                _ => AsyncObservable.Empty<(OpenNettyAddress? Address, OpenNettyCommand Command)>()
            })
            .Retry();
    }

    /// <inheritdoc/>
    public virtual IAsyncObservable<(OpenNettyAddress? Address, OpenNettyDimension Dimension, ImmutableArray<string> Values)> ObserveDimensionsAsync(
        OpenNettyProtocol protocol,
        OpenNettyCategory category,
        OpenNettyGateway? gateway = null)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0064));
        }

        if (gateway is not null && gateway.Protocol != protocol)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0031));
        }

        return _pipeline.Where(notification => gateway is null || notification.Gateway == gateway)
            .SelectMany(notification => notification switch
            {
                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol : OpenNettyProtocol.Scs, Type: OpenNettySessionType.Command } session,
                    Message: { Type     : OpenNettyMessageType.DimensionRead,
                               Address  : OpenNettyAddress address,
                               Dimension: OpenNettyDimension dimension,
                               Values   : [..] values } message }
                    when message.Protocol == protocol && message.Category == category
                        => AsyncObservable.Return<(OpenNettyAddress? Address, OpenNettyDimension Dimension, ImmutableArray<string> Values)>((address, dimension, values)),

                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol : OpenNettyProtocol.Nitoo or OpenNettyProtocol.Zigbee, Type: OpenNettySessionType.Generic } session,
                    Message: { Type     : OpenNettyMessageType.DimensionRead,
                               Address  : OpenNettyAddress address,
                               Dimension: OpenNettyDimension dimension,
                               Values   : [..] values } message }
                    when message.Protocol == protocol && message.Category == category
                        => AsyncObservable.Return<(OpenNettyAddress? Address, OpenNettyDimension Dimension, ImmutableArray<string> Values)>((address, dimension, values)),

                _ => AsyncObservable.Empty<(OpenNettyAddress? Address, OpenNettyDimension Dimension, ImmutableArray<string> Values)>()
            })
            .Retry();
    }

    /// <inheritdoc/>
    public virtual IAsyncObservable<OpenNettyMessage> ObserveEventsAsync(
        OpenNettyProtocol protocol,
        OpenNettyGateway? gateway = null)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0064));
        }

        if (gateway is not null && gateway.Protocol != protocol)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0031));
        }

        return _pipeline.Where(notification => gateway is null || notification.Gateway == gateway)
            .SelectMany(notification => notification switch
            {
                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol : OpenNettyProtocol.Scs, Type: OpenNettySessionType.Command } session,
                    Message: OpenNettyMessage message }
                    when message.Protocol == protocol => AsyncObservable.Return(message),

                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol : OpenNettyProtocol.Nitoo or OpenNettyProtocol.Zigbee, Type: OpenNettySessionType.Generic } session,
                    Message: OpenNettyMessage message }
                    when message.Protocol == protocol &&
                         message.Type is not (OpenNettyMessageType.Acknowledgement or
                                              OpenNettyMessageType.BusyNegativeAcknowledgement or
                                              OpenNettyMessageType.NegativeAcknowledgement)
                        => AsyncObservable.Return(message),

                _ => AsyncObservable.Empty<OpenNettyMessage>()
            })
            .Retry();
    }

    /// <inheritdoc/>
    public virtual async ValueTask SendMessageAsync(
        OpenNettyMessage message,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions options = OpenNettyTransmissionOptions.None,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (gateway is not null && gateway.Protocol != message.Protocol)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0031));
        }

        // If no gateway was explicitly specified, try to resolve it from the options.
        gateway ??= _options.CurrentValue.Gateways.Find(gateway => gateway.Protocol == message.Protocol) ??
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0032));

        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)), gateway);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyLogger<OpenNettyService>>(nameof(OpenNettyLogger<OpenNettyService>)), _logger);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyMessage>(nameof(OpenNettyMessage)), message);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyTransmissionOptions>(nameof(OpenNettyTransmissionOptions)), options);

        try
        {
            await gateway.Options.OutgoingMessageResiliencePipeline.ExecuteAsync(async context =>
                await SendRawMessageAsync(message, gateway, options, context.CancellationToken), context);
        }

        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    /// <inheritdoc/>
    public virtual async ValueTask SetDimensionAsync(
        OpenNettyProtocol protocol,
        OpenNettyDimension dimension,
        ImmutableArray<string> values,
        OpenNettyAddress? address = null,
        OpenNettyMedium? medium = null,
        OpenNettyMode? mode = null,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions options = OpenNettyTransmissionOptions.None,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0064));
        }

        if (gateway is not null && gateway.Protocol != protocol)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0031));
        }

        // If no gateway was explicitly specified, try to resolve it from the options.
        gateway ??= _options.CurrentValue.Gateways.Find(gateway => gateway.Protocol == protocol) ??
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0032));

        var message = OpenNettyMessage.CreateDimensionSet(protocol, dimension, values, address, medium, mode);

        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)), gateway);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyLogger<OpenNettyService>>(nameof(OpenNettyLogger<OpenNettyService>)), _logger);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyMessage>(nameof(OpenNettyMessage)), message);
        context.Properties.Set(new ResiliencePropertyKey<OpenNettyTransmissionOptions>(nameof(OpenNettyTransmissionOptions)), options);

        try
        {
            await gateway.Options.OutgoingMessageResiliencePipeline.ExecuteAsync(async context =>
                await SendRawMessageAsync(message, gateway, options, context.CancellationToken), context);
        }

        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    /// <summary>
    /// Sends a raw OpenNetty message and waits until it is processed by a worker.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="gateway">The gateway used to send the message.</param>
    /// <param name="options">The transmission options to use.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation
    /// and whose result returns the session used by the worker to send the message to the gateway.
    /// </returns>
    /// <exception cref="OpenNettyException">An error occurred while processing the message.</exception>
    protected virtual async ValueTask<OpenNettySession> SendRawMessageAsync(
        OpenNettyMessage message,
        OpenNettyGateway gateway,
        OpenNettyTransmissionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(gateway);

        switch (message)
        {
            // Note: Zigbee gateways are affected by a bug that affects how STATUS REQUEST frames are handled
            // and prevents a proper acknowledgement frame from being returned. To ensure the session is not
            // blocked until the timeout is reached when sending STATUS REQUEST frames, acknowledgement validation
            // is deliberately disabled: in this case, the requests are assumed to be accepted by the gateway.
            case { Protocol: OpenNettyProtocol.Zigbee, Type: OpenNettyMessageType.StatusRequest }:

            // Note: Nitoo gateways don't return acknowledgement frames for these specific dimensions:
            case { Protocol : OpenNettyProtocol.Nitoo,
                   Type     : OpenNettyMessageType.DimensionRequest,
                   Address  : null,
                   Dimension: OpenNettyDimension dimension }
                when dimension == OpenNettyDimensions.Management.FirmwareVersion ||
                     dimension == OpenNettyDimensions.Management.HardwareVersion ||
                     dimension == OpenNettyDimensions.Management.DeviceIdentifier:
                options |= OpenNettyTransmissionOptions.IgnoreAcknowledgementValidation;
                break;
        }

        // Create a new transaction that will be used to correlate the
        // incoming notifications with the message that is being sent.
        var transaction = OpenNettyTransaction.Create();

        var notifications = _pipeline
            .Where(notification => notification.Gateway == gateway)
            .Where(notification => notification switch
            {
                OpenNettyNotifications.GatewayBusy              value => value.Transaction == transaction,
                OpenNettyNotifications.InvalidAction            value => value.Transaction == transaction,
                OpenNettyNotifications.InvalidFrame             value => value.Transaction == transaction,
                OpenNettyNotifications.MessageSent              value => value.Transaction == transaction,
                OpenNettyNotifications.NoAcknowledgmentReceived value => value.Transaction == transaction,
                OpenNettyNotifications.NoActionReceived         value => value.Transaction == transaction,

                _ => false
            })
            .Replay();

        // Connect the observable before sending the message to ensure
        // the notifications are not missed due to a race condition.
        await using var connection = await notifications.ConnectAsync();

        // Inform the workers that a frame needs to be processed.
        var notification = new OpenNettyNotifications.MessageReady
        {
            Gateway = gateway,
            Message = message,
            Options = options,
            Transaction = transaction
        };

        await _pipeline.PublishAsync(notification, cancellationToken);

        // Retrieve the notification indicating whether the session acknowledged or rejected the message.
        // If no notification is received, assume the message couldn't be processed by a worker.
        switch (await notifications
            .FirstOrDefault()
            .Timeout(gateway.Options.OutgoingMessageProcessingTimeout, AsyncObservable.Return(default(OpenNettyNotification)))
            .RunAsync(cancellationToken))
        {
            case OpenNettyNotifications.MessageSent { Session: OpenNettySession session }:
                return session;

            case OpenNettyNotifications.GatewayBusy:
                throw new OpenNettyException(OpenNettyErrorCode.GatewayBusy, SR.GetResourceString(SR.ID0017));

            case OpenNettyNotifications.InvalidAction:
                throw new OpenNettyException(OpenNettyErrorCode.InvalidAction, SR.GetResourceString(SR.ID0035));

            case OpenNettyNotifications.InvalidFrame:
                throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0018));

            case OpenNettyNotifications.NoAcknowledgmentReceived:
                throw new OpenNettyException(OpenNettyErrorCode.NoAcknowledgementReceived, SR.GetResourceString(SR.ID0016));

            case OpenNettyNotifications.NoActionReceived:
                throw new OpenNettyException(OpenNettyErrorCode.NoActionReceived, SR.GetResourceString(SR.ID0036));

            case null or _:
                throw new OpenNettyException(OpenNettyErrorCode.NoWorkerAvailable, SR.GetResourceString(SR.ID0037));
        }
    }
}

