/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Globalization;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using static OpenNetty.OpenNettyEvents;

namespace OpenNetty;

/// <summary>
/// Contains the logic necessary to infer high-level events from incoming
/// and outgoing notifications dispatched by the OpenNetty pipeline.
/// </summary>
public sealed class OpenNettyCoordinator : IOpenNettyHandler
{
    private readonly OpenNettyController _controller;
    private readonly OpenNettyEvents _events;
    private readonly ILogger<OpenNettyCoordinator> _logger;
    private readonly OpenNettyManager _manager;
    private readonly IOpenNettyPipeline _pipeline;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyCoordinator"/> class.
    /// </summary>
    /// <param name="controller">The OpenNetty controller.</param>
    /// <param name="events">The OpenNetty events.</param>
    /// <param name="logger">The OpenNetty logger.</param>
    /// <param name="manager">The OpenNetty manager.</param>
    /// <param name="pipeline">The OpenNetty pipeline.</param>
    public OpenNettyCoordinator(
        OpenNettyController controller,
        OpenNettyEvents events,
        ILogger<OpenNettyCoordinator> logger,
        OpenNettyManager manager,
        IOpenNettyPipeline pipeline)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc/>
    async ValueTask<IAsyncDisposable> IOpenNettyHandler.SubscribeAsync() => StableCompositeAsyncDisposable.Create(
    [
        // Note: this event handler is responsible for monitoring incoming and outgoing frames to detect state
        // changes affecting - directly or indirectly (e.g via a Nitoo PnL scenario) - registered endpoints.
        await _pipeline.SelectMany(static notification => notification switch
        {
            OpenNettyNotifications.MessageReceived {
                Session.Type: OpenNettySessionType.Generic,
                Message: {
                    Protocol: OpenNettyProtocol.Nitoo,
                    Type    : OpenNettyMessageType.BusCommand    or
                              OpenNettyMessageType.DimensionRead or
                              OpenNettyMessageType.DimensionSet } message }
                => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            OpenNettyNotifications.MessageReceived {
                Session.Type: OpenNettySessionType.Event,
                Message: {
                    Protocol: OpenNettyProtocol.Scs,
                    Type    : OpenNettyMessageType.BusCommand    or
                              OpenNettyMessageType.DimensionRead or
                              OpenNettyMessageType.DimensionSet } message }
                => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            OpenNettyNotifications.MessageReceived {
                Session.Type: OpenNettySessionType.Generic,
                Message: {
                    Protocol: OpenNettyProtocol.Zigbee,
                    Type    : OpenNettyMessageType.BusCommand    or
                              OpenNettyMessageType.DimensionRead or
                              OpenNettyMessageType.DimensionSet } message }
                => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            // Note: unlike SCS (and Zigbee devices when the supervision mode is enabled), Nitoo devices
            // never report back state changes to the OpenWebNet gateway when the change originates from
            // the gateway itself. To ensure events are correctly reported, the outgoing Nitoo BUS COMMAND
            // and DIMENSION SET messages that have been acknowledged by the gateway (and optionally validated
            // by the remote device using a special "VALID ACTION" BUS COMMAND message) are monitored here.
            OpenNettyNotifications.MessageSent {
                Session.Type: OpenNettySessionType.Generic,
                Message: {
                    Protocol: OpenNettyProtocol.Nitoo,
                    Type    : OpenNettyMessageType.BusCommand or OpenNettyMessageType.DimensionSet } message }
                => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            // Note: while Zigbee devices report back state changes to the gateway when the supervisor mode is
            // enabled, this mode suffers from two major limitations: it is not recommended to use it on more than
            // one Zigbee gateway at a time and state changes are generally throttled and are only reported after
            // a delay of up to 3 seconds following the last state change (intermediate changes are also skipped).
            //
            // To mitigate these limitations, the outgoing Zigbee BUS COMMAND and DIMENSION SET messages that
            // have been acknowledged by the gateway are monitored so that changes can be reported immediately.
            OpenNettyNotifications.MessageSent {
                Session.Type: OpenNettySessionType.Generic,
                Message: {
                    Protocol: OpenNettyProtocol.Zigbee,
                    Type    : OpenNettyMessageType.BusCommand or OpenNettyMessageType.DimensionSet } message }
                => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            _ => AsyncObservable.Empty<(OpenNettyNotification Notification, OpenNettyMessage Message)>()
        })
        .Do(onNext: async arguments =>
        {
            var (notification, message) = arguments;

            // Important: to ensure the order of events is preserved, this RX event handler processes incoming and outgoing
            // messages sequentially. As such, it is critical that this asynchronous method completes as quickly as possible.

            switch ((notification, message))
            {
                // The switch state and the brightness level of an endpoint can be inferred from 6 types of messages:
                //
                //   - From an "OFF" or "ON" BUS COMMAND message:
                //     * For Nitoo and Zigbee devices, the message MAY be incoming or outgoing.
                //     * For SCS devices, the message MUST be incoming.
                //
                //   - From an incoming or outgoing "ON%" BUS COMMAND message:
                //     * For SCS devices, the message MUST be incoming.
                //     * For Zigbee devices, the message MAY be incoming or outgoing.
                //
                //   - For SCS and Zigbee devices, from an incoming "DIMMER SPEED LEVEL" or "DIMMER STATUS" DIMENSION READ message.
                //   - For Nitoo and Zigbee devices, from an outgoing "DIMMER SPEED LEVEL" DIMENSION SET message.
                //   - For Nitoo devices, from an incoming "UNIT DESCRIPTION=129" DIMENSION READ message (non-dimmable devices).
                //   - For Nitoo devices, from an incoming "UNIT DESCRIPTION=143" DIMENSION READ message (dimmable devices).

                case (OpenNettyNotifications.MessageReceived or OpenNettyNotifications.MessageSent,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Nitoo or OpenNettyProtocol.Zigbee,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand,
                                         Address : not null }) or
                     (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Scs,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand,
                                         Address : not null })
                    // Note: ON/OFF BUS COMMAND frames can be parameterized.
                    when message.Command?.WithParameters([]) == OpenNettyCommands.Lighting.On ||
                         message.Command?.WithParameters([]) == OpenNettyCommands.Lighting.Off:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        List<Task> tasks = [];

                        switch (endpoint.Protocol)
                        {
                            // Note: outgoing ON/OFF commands sent using broadcast or multicast transmission
                            // generally don't affect the local output of a Nitoo lighting device. As such,
                            // the switch state/brightness of the endpoint is only reported if the message was
                            // received (e.g by a different unit on the same device) or was sent in unicast.
                            case OpenNettyProtocol.Nitoo when notification is OpenNettyNotifications.MessageReceived:
                            case OpenNettyProtocol.Nitoo when notification is OpenNettyNotifications.MessageSent && message.Mode is OpenNettyMode.Unicast:
                            case OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee:
                                if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                                {
                                    tasks.Add(ReportStateAsync(endpoint, cancellationToken).AsTask());
                                }
                                break;
                        }

                        if (notification is OpenNettyNotifications.MessageReceived)
                        {
                            // For Nitoo devices, if the ON/OFF command was emitted by a different unit
                            // on the same device, reflect the state change on the linked endpoint.
                            if (endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Definition.AssociatedUnitId: byte unit })
                            {
                                tasks.Add(Task.Run(async () =>
                                {
                                    var endpoint = await _manager.FindEndpointByAddressAsync(notification.Gateway,
                                        OpenNettyAddress.FromNitooAddress(
                                            OpenNettyAddress.ToNitooAddress(message.Address.Value).Identifier, unit));

                                    if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                                    {
                                        await ReportStateAsync(endpoint, cancellationToken);
                                    }
                                }, cancellationToken));
                            }

                            if (endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioEvent))
                            {
                                // For Nitoo devices, if the message was sent using a broadcast or multicast transmission,
                                // reflect the state change on all the other endpoints that are part of the same scenario.
                                if (endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Scenarios: [_, ..] scenarios } &&
                                    message.Mode is OpenNettyMode.Broadcast or OpenNettyMode.Multicast)
                                {
                                    var endpoints = scenarios.ToAsyncEnumerable()
                                        .Where(static scenario => scenario.FunctionCode is < 105)
                                        .SelectAwait(scenario => _manager.FindEndpointByNameAsync(scenario.EndpointName))
                                        .Where(static endpoint => endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit: OpenNettyUnit })
                                        .OfType<OpenNettyEndpoint>()
                                        .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState));

                                    tasks.Add(Parallel.ForEachAsync(endpoints, ReportStateAsync));
                                }

                                // If the message was received and was emitted by either a Zigbee device or a Nitoo
                                // device using a broadcast transmission, it is also considered an ON/OFF scenario.
                                if (endpoint.Protocol is OpenNettyProtocol.Zigbee ||
                                   (endpoint.Protocol is OpenNettyProtocol.Nitoo && message.Mode is OpenNettyMode.Broadcast))
                                {
                                    if (message.Command == OpenNettyCommands.Lighting.On)
                                    {
                                        tasks.Add(_events.PublishAsync(new OnScenarioReportedEventArgs(endpoint), cancellationToken).AsTask());
                                    }

                                    else
                                    {
                                        tasks.Add(_events.PublishAsync(new OffScenarioReportedEventArgs(endpoint), cancellationToken).AsTask());
                                    }
                                }
                            }
                        }

                        await Task.WhenAll(tasks);
                    });

                    async ValueTask ReportStateAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken)
                    {
                        if (message.Command == OpenNettyCommands.Lighting.On)
                        {
                            await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint,
                                OpenNettyModels.Lighting.SwitchState.On), cancellationToken);

                            // Note: if the endpoint was configured to use the push-button mode, dispatch an OFF state
                            // event immediately after switching it on (or receiving a notification indicating it was
                            // switched on), as Nitoo devices using this mode don't automatically report the OFF state.
                            if (endpoint.GetStringSetting(OpenNettySettings.SwitchMode) is OpenNettySettings.SwitchModes.PushButton)
                            {
                                await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint,
                                    OpenNettyModels.Lighting.SwitchState.Off), cancellationToken);
                            }
                        }

                        else
                        {
                            await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint,
                                OpenNettyModels.Lighting.SwitchState.Off), cancellationToken);
                        }
                        
                        // Note: for Nitoo devices supporting dimming, an ON command always changes the brightness to 100%.
                        if (message.Command == OpenNettyCommands.Lighting.On && endpoint.Protocol is OpenNettyProtocol.Nitoo &&
                           (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                            endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState)))
                        {
                            await _events.PublishAsync(new BrightnessReportedEventArgs(endpoint, 100), cancellationToken);
                        }
                    }
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Scs,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand,
                                         Address : not null }) or
                     (OpenNettyNotifications.MessageReceived or OpenNettyNotifications.MessageSent,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Zigbee,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand,
                                         Address : not null })
                    when message.Command == OpenNettyCommands.Lighting.On20 ||
                         message.Command == OpenNettyCommands.Lighting.On30 ||
                         message.Command == OpenNettyCommands.Lighting.On40 ||
                         message.Command == OpenNettyCommands.Lighting.On50 ||
                         message.Command == OpenNettyCommands.Lighting.On60 ||
                         message.Command == OpenNettyCommands.Lighting.On70 ||
                         message.Command == OpenNettyCommands.Lighting.On80 ||
                         message.Command == OpenNettyCommands.Lighting.On90 ||
                         message.Command == OpenNettyCommands.Lighting.On100:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        // SCS devices configured to use the PUL mode never react to area and general commands.
                        if (message.Address.Value.Type is OpenNettyAddressType.ScsLightPoint &&
                            (OpenNettyAddress.IsScsLightPointAreaAddress(message.Address.Value) ||
                             OpenNettyAddress.IsScsLightPointGeneralAddress(message.Address.Value)) &&
                            endpoint.GetStringSetting(OpenNettySettings.SwitchMode) is OpenNettySettings.SwitchModes.PushButton)
                        {
                            return;
                        }

                        if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                        {
                            await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint,
                                OpenNettyModels.Lighting.SwitchState.On), cancellationToken);
                        }

                        // Note: brightness level changes reported via ON% BUS COMMAND frames are deliberately
                        // ignored for endpoints that support advanced dimming, as this method often gives very
                        // imprecise results that are inconsistent with the brightness level retrieved using a
                        // "DIMMER LEVEL SPEED" or "DIMMER STATUS" DIMENSION REQUEST frame (e.g when setting the
                        // brightness to 30%, a F418U2 SCS dimmer correctly reports the "130" value when using
                        // a DIMENSION REQUEST but returns "5" (50%) when using a STATUS REQUEST). To avoid that,
                        // a specialized event handler is responsible for monitoring ON% BUS COMMAND frames and
                        // retrieving the exact brightness level using a "DIMMER LEVEL SPEED" DIMENSION REQUEST.
                        if (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) &&
                           !endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                        {
                            await _events.PublishAsync(new BrightnessReportedEventArgs(endpoint, (byte)
                                (message.Command == OpenNettyCommands.Lighting.On20 ? 20 :
                                 message.Command == OpenNettyCommands.Lighting.On30 ? 30 :
                                 message.Command == OpenNettyCommands.Lighting.On40 ? 40 :
                                 message.Command == OpenNettyCommands.Lighting.On50 ? 50 :
                                 message.Command == OpenNettyCommands.Lighting.On60 ? 60 :
                                 message.Command == OpenNettyCommands.Lighting.On70 ? 70 :
                                 message.Command == OpenNettyCommands.Lighting.On80 ? 80 :
                                 message.Command == OpenNettyCommands.Lighting.On90 ? 90 : 100)), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : not null,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 } value, ..] })
                    when dimension == OpenNettyDimensions.Lighting.DimmerLevelSpeed ||
                         dimension == OpenNettyDimensions.Lighting.DimmerStatus:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        // SCS devices configured to use the PUL mode never react to area and general commands.
                        if (message.Address.Value.Type is OpenNettyAddressType.ScsLightPoint &&
                            (OpenNettyAddress.IsScsLightPointAreaAddress(message.Address.Value) ||
                             OpenNettyAddress.IsScsLightPointGeneralAddress(message.Address.Value)) &&
                            endpoint.GetStringSetting(OpenNettySettings.SwitchMode) is OpenNettySettings.SwitchModes.PushButton)
                        {
                            return;
                        }

                        var level = (byte) (byte.Parse(value, CultureInfo.InvariantCulture) - 100);

                        if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                        {
                            await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint, level is not 0 ?
                                OpenNettyModels.Lighting.SwitchState.On :
                                OpenNettyModels.Lighting.SwitchState.Off), cancellationToken);
                        }

                        // Note: the special brightness level "0" always indicates that the output is switched off.
                        // To avoid overriding the last known level (which is typically restored by SCS devices when
                        // receiving an ON command), the brightness level is only reported if it's higher than zero.
                        // 
                        // Note: while Nitoo devices normally don't restore the last known brightness level when
                        // receiving an ON command, the same rule applies for consistency with MyHome/SCS devices.
                        if (level is not 0 && (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                                               endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState)))
                        {
                            await _events.PublishAsync(new BrightnessReportedEventArgs(endpoint, level), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageSent,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo or OpenNettyProtocol.Zigbee,
                                         Type     : OpenNettyMessageType.DimensionSet,
                                         Address  : not null,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 } value, ..] })
                    when dimension == OpenNettyDimensions.Lighting.DimmerLevelSpeed:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        // Note: for Nitoo devices, the brightness level is expressed differently.
                        var level = message.Protocol is OpenNettyProtocol.Nitoo ?
                            (byte) (byte.Parse(value, CultureInfo.InvariantCulture) - 100) :
                            (byte) Math.Round(decimal.Parse(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);

                        switch (endpoint.Protocol)
                        {
                            // Note: outgoing dimmer level commands sent using broadcast or multicast transmission
                            // generally don't affect the local output of a Nitoo lighting device. As such, the switch
                            // state/brightness of the endpoint is only reported if the message was sent in unicast.
                            case OpenNettyProtocol.Nitoo when message.Mode is OpenNettyMode.Unicast:
                            case OpenNettyProtocol.Zigbee:
                                if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                                {
                                    await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint, level is not 0 ?
                                        OpenNettyModels.Lighting.SwitchState.On :
                                        OpenNettyModels.Lighting.SwitchState.Off), cancellationToken);
                                }

                                // Note: the special brightness level "0" always indicates that the output is switched off.
                                // To avoid overriding the last known level (which is typically restored by SCS devices when
                                // receiving an ON command), the brightness level is only reported if it's higher than zero.
                                // 
                                // Note: while Nitoo devices normally don't restore the last known brightness level when
                                // receiving an ON command, the same rule applies for consistency with MyHome/SCS devices.
                                if (level is not 0 && (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                                                       endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState)))
                                {
                                    await _events.PublishAsync(new BrightnessReportedEventArgs(endpoint, level), cancellationToken);
                                }
                                break;
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : not null,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : ["129", { Length: > 0 } value] })
                    when dimension == OpenNettyDimensions.Diagnostics.UnitDescription:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                        {
                            await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint, value is "128" or "129" or "130" ?
                                OpenNettyModels.Lighting.SwitchState.On :
                                OpenNettyModels.Lighting.SwitchState.Off), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : not null,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : ["143", { Length: > 0 } value, ..] })
                    when dimension == OpenNettyDimensions.Diagnostics.UnitDescription:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        var level = byte.Parse(value, CultureInfo.InvariantCulture);

                        if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                        {
                            await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint, level is not 0 ?
                                OpenNettyModels.Lighting.SwitchState.On :
                                OpenNettyModels.Lighting.SwitchState.Off), cancellationToken);
                        }

                        // Note: the special brightness level "0" always indicates that the output is switched off.
                        // To avoid overriding the last known level (which is typically restored by SCS devices when
                        // receiving an ON command), the brightness level is only reported if it's higher than zero.
                        // 
                        // Note: while Nitoo devices normally don't restore the last known brightness level when
                        // receiving an ON command, the same rule applies for consistency with MyHome/SCS devices.
                        if (level is not 0 && (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                                               endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState)))
                        {
                            await _events.PublishAsync(new BrightnessReportedEventArgs(endpoint, level), cancellationToken);
                        }
                    });
                    break;
                }

                // The shutter state and the position of an endpoint can be inferred from 3 types of messages:
                //
                //   - From an incoming or outgoing "STOP", "UP" or "DOWN" BUS COMMAND message:
                //     * For Nitoo and Zigbee devices, the message MAY be incoming or outgoing.
                //     * For SCS devices, the message MUST be incoming.
                //
                //   - For SCS and Zigbee devices, from an incoming "SHUTTER STATUS" DIMENSION READ message.
                //   - For Nitoo devices, from an incoming "UNIT DESCRIPTION=139" DIMENSION READ message.

                case (OpenNettyNotifications.MessageReceived or OpenNettyNotifications.MessageSent,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Nitoo or OpenNettyProtocol.Zigbee,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand,
                                         Address : not null }) or
                     (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Scs,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand,
                                         Address : not null })
                    when message.Command == OpenNettyCommands.Automation.Stop ||
                         message.Command == OpenNettyCommands.Automation.Up   ||
                         message.Command == OpenNettyCommands.Automation.Down:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        List<Task> tasks = [];

                        switch (endpoint.Protocol)
                        {
                            // Note: outgoing STOP/UP/DOWN commands sent using broadcast or multicast transmission
                            // generally don't affect the local output of a Nitoo automation device. As such,
                            // the shutter state/position of the endpoint is only reported if the message was
                            // received (e.g by a different unit on the same device) or was sent in unicast.
                            case OpenNettyProtocol.Nitoo when notification is OpenNettyNotifications.MessageReceived:
                            case OpenNettyProtocol.Nitoo when notification is OpenNettyNotifications.MessageSent && message.Mode is OpenNettyMode.Unicast:
                            case OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee:
                                if (endpoint.HasCapability(OpenNettyCapabilities.BasicShutterState))
                                {
                                    tasks.Add(ReportStateAsync(endpoint, cancellationToken).AsTask());
                                }
                                break;
                        }

                        if (notification is OpenNettyNotifications.MessageReceived)
                        {
                            // For Nitoo devices, if the STOP/UP/DOWN command was emitted by a different
                            // unit on the same device, reflect the state change on the linked endpoint.
                            if (endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Definition.AssociatedUnitId: byte unit })
                            {
                                tasks.Add(Task.Run(async () =>
                                {
                                    var endpoint = await _manager.FindEndpointByAddressAsync(notification.Gateway,
                                        OpenNettyAddress.FromNitooAddress(
                                            OpenNettyAddress.ToNitooAddress(message.Address.Value).Identifier, unit));

                                    if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.BasicShutterState))
                                    {
                                        await ReportStateAsync(endpoint, cancellationToken);
                                    }
                                }, cancellationToken));
                            }

                            if (endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioEvent))
                            {
                                // For Nitoo devices, if the message was sent using a broadcast or multicast transmission,
                                // reflect the state change on all the other endpoints that are part of the same scenario.
                                if (endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Scenarios: [_, ..] scenarios } &&
                                    message.Mode is OpenNettyMode.Broadcast or OpenNettyMode.Multicast)
                                {
                                    var endpoints = scenarios.ToAsyncEnumerable()
                                        .Where(static scenario => scenario.FunctionCode is < 110 or 111 or 112)
                                        .SelectAwait(scenario => _manager.FindEndpointByNameAsync(scenario.EndpointName))
                                        .Where(static endpoint => endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit: OpenNettyUnit })
                                        .OfType<OpenNettyEndpoint>()
                                        .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.BasicShutterState));

                                    tasks.Add(Parallel.ForEachAsync(endpoints, ReportStateAsync));
                                }

                                // If the message was received and was emitted by either a Zigbee device or a Nitoo device
                                // using a broadcast transmission, it is also considered an STOP/UP/DOWN scenario.
                                if (endpoint.Protocol is OpenNettyProtocol.Zigbee ||
                                   (endpoint.Protocol is OpenNettyProtocol.Nitoo && message.Mode is OpenNettyMode.Broadcast))
                                {
                                    if (message.Command == OpenNettyCommands.Automation.Stop)
                                    {
                                        tasks.Add(_events.PublishAsync(new ShutterStopScenarioReportedEventArgs(endpoint), cancellationToken).AsTask());
                                    }

                                    else if (message.Command == OpenNettyCommands.Automation.Up)
                                    {
                                        tasks.Add(_events.PublishAsync(new ShutterUpScenarioReportedEventArgs(endpoint), cancellationToken).AsTask());
                                    }

                                    else
                                    {
                                        tasks.Add(_events.PublishAsync(new ShutterDownScenarioReportedEventArgs(endpoint), cancellationToken).AsTask());
                                    }
                                }
                            }
                        }

                        await Task.WhenAll(tasks);
                    });

                    async ValueTask ReportStateAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken)
                        => await _events.PublishAsync(new ShutterStateReportedEventArgs(endpoint,
                            message.Command == OpenNettyCommands.Automation.Stop ? OpenNettyModels.Automation.ShutterState.Stopped :
                            message.Command == OpenNettyCommands.Automation.Up   ? OpenNettyModels.Automation.ShutterState.Opening :
                            message.Command == OpenNettyCommands.Automation.Down ? OpenNettyModels.Automation.ShutterState.Closing :
                            throw new InvalidDataException(SR.GetResourceString(SR.ID0068))), cancellationToken);
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Zigbee,
                                         Type     : OpenNettyMessageType.DimensionSet,
                                         Address  : not null,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 } status, { Length: > 0 } position, ..] })
                    when dimension == OpenNettyDimensions.Automation.ShutterStatus:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState) &&
                            endpoint.GetStringSetting(OpenNettySettings.FunctionType) is not (null or OpenNettySettings.FunctionTypes.AutomationActuator))
                        {
                            await _events.PublishAsync(new ShutterStateReportedEventArgs(endpoint,
                                (status, byte.Parse(position, CultureInfo.InvariantCulture)) switch
                                {
                                    ("10",       0        ) => OpenNettyModels.Automation.ShutterState.Closed,
                                    ("10", >= 1 and <= 100) => OpenNettyModels.Automation.ShutterState.Open,
                                    ("10",       _        ) => OpenNettyModels.Automation.ShutterState.Stopped,

                                    ("11" or "13", _) => OpenNettyModels.Automation.ShutterState.Opening,
                                    ("12" or "14", _) => OpenNettyModels.Automation.ShutterState.Closing,

                                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                                }), cancellationToken);

                            await _events.PublishAsync(new ShutterPositionReportedEventArgs(endpoint,
                                byte.Parse(position, CultureInfo.InvariantCulture)), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : not null,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : ["139", { Length: > 0 } value, ..] })
                    when dimension == OpenNettyDimensions.Diagnostics.UnitDescription:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.BasicShutterState))
                        {
                            await _events.PublishAsync(new ShutterStateReportedEventArgs(endpoint, value switch
                            {
                                "100" or "102" => OpenNettyModels.Automation.ShutterState.Opening,
                                "0"   or "103" => OpenNettyModels.Automation.ShutterState.Closing,
                                      _        => OpenNettyModels.Automation.ShutterState.Stopped
                            }), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : not null,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 }, { Length: > 0 }, { Length: > 0 }] values })
                    when dimension == OpenNettyDimensions.TemperatureControl.SmartMeterIndexes:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                        {
                            await _events.PublishAsync(new SmartMeterIndexesReportedEventArgs(endpoint,
                                OpenNettyModels.TemperatureControl.SmartMeterIndexes.CreateFromDimensionValues(values)), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : not null,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 } value] })
                    when dimension == OpenNettyDimensions.TemperatureControl.SmartMeterRateType:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                        {
                            await _events.PublishAsync(new SmartMeterRateTypeReportedEventArgs(endpoint, value switch
                            {
                                "2" => OpenNettyModels.TemperatureControl.SmartMeterRateType.OffPeak,
                                "3" => OpenNettyModels.TemperatureControl.SmartMeterRateType.Peak,

                                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                            }), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : not null,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : ["7", { Length: > 0 } value] })
                    when dimension == OpenNettyDimensions.Diagnostics.UnitDescription:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                        {
                            await _events.PublishAsync(new SmartMeterRateTypeReportedEventArgs(endpoint, value switch
                            {
                                "32" or "33" => OpenNettyModels.TemperatureControl.SmartMeterRateType.OffPeak,
                                "48" or "49" => OpenNettyModels.TemperatureControl.SmartMeterRateType.Peak,

                                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                            }), cancellationToken);

                            await _events.PublishAsync(new SmartMeterPowerCutModeReportedEventArgs(endpoint, value is "33" or "49"), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : not null,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : ["133", { Length: > 0 } value] })
                    when dimension == OpenNettyDimensions.Diagnostics.UnitDescription:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                        {
                            switch (value)
                            {
                                // Note: the value "0" is ambiguous and appears to be used to represent two cases:
                                //
                                //  - When the user selected the "automatic" mode but hot water is not currently
                                //    being produced (e.g because the off-peak signal was not yet received).
                                //
                                //  - When no hot water is produced because the user selected the "forced off" mode.
                                //
                                // Since the value is ambiguous, it is not possible to reliably determine the actual
                                // water heating mode: in this case, the mode is not immediately reported and an another
                                // event handler is responsible for reporting it when the off-peak signal is received.
                                case "0":
                                    await _events.PublishAsync(new WaterHeaterStateReportedEventArgs(endpoint,
                                        OpenNettyModels.TemperatureControl.WaterHeaterState.Idle), cancellationToken);
                                    break;

                                case "32":
                                    await _events.PublishAsync(new WaterHeaterSetpointModeReportedEventArgs(endpoint,
                                        OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic), cancellationToken);

                                    await _events.PublishAsync(new WaterHeaterStateReportedEventArgs(endpoint,
                                        OpenNettyModels.TemperatureControl.WaterHeaterState.Idle), cancellationToken);
                                    break;

                                case "1" or "33":
                                    await _events.PublishAsync(new WaterHeaterSetpointModeReportedEventArgs(endpoint,
                                        OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic), cancellationToken);

                                    await _events.PublishAsync(new WaterHeaterStateReportedEventArgs(endpoint,
                                        OpenNettyModels.TemperatureControl.WaterHeaterState.Heating), cancellationToken);
                                    break;

                                case "17":
                                    await _events.PublishAsync(new WaterHeaterSetpointModeReportedEventArgs(endpoint,
                                        OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn), cancellationToken);

                                    await _events.PublishAsync(new WaterHeaterStateReportedEventArgs(endpoint,
                                        OpenNettyModels.TemperatureControl.WaterHeaterState.Heating), cancellationToken);
                                    break;
                            }
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageSent,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionSet,
                                         Address  : not null,
                                         Mode     : OpenNettyMode mode,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 } value, ..] })
                    when dimension == OpenNettyDimensions.TemperatureControl.WaterHeatingMode:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        List<Task> tasks = [];

                        if (endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                        {
                            tasks.Add(ReportSetpointModeAsync(endpoint, cancellationToken).AsTask());
                        }

                        if (endpoint is { Unit.Definition.AssociatedUnitId: byte unit })
                        {
                            tasks.Add(Task.Run(async () =>
                            {
                                var endpoint = await _manager.FindEndpointByAddressAsync(notification.Gateway,
                                    OpenNettyAddress.FromNitooAddress(
                                        OpenNettyAddress.ToNitooAddress(message.Address.Value).Identifier, unit));

                                if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                                {
                                    await ReportSetpointModeAsync(endpoint, cancellationToken);
                                }
                            }, cancellationToken));
                        }

                        if (endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Scenarios: [_, ..] scenarios } &&
                            message.Mode is OpenNettyMode.Broadcast or OpenNettyMode.Multicast)
                        {
                            var endpoints = scenarios.ToAsyncEnumerable()
                                .Where(static scenario => scenario.FunctionCode is 255)
                                .SelectAwait(scenario => _manager.FindEndpointByNameAsync(scenario.EndpointName))
                                .Where(static endpoint => endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit: OpenNettyUnit })
                                .OfType<OpenNettyEndpoint>()
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.WaterHeating));

                            tasks.Add(Parallel.ForEachAsync(endpoints, ReportSetpointModeAsync));
                        }

                        await Task.WhenAll(tasks);
                    });

                    async ValueTask ReportSetpointModeAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken) =>
                        await _events.PublishAsync(new WaterHeaterSetpointModeReportedEventArgs(endpoint, value switch
                        {
                            "0" => OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOff,
                            "1" => OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn,
                            "2" => OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic,

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                        }), cancellationToken);
                    break;
                }

                // The partial state of a pilot wire device can be inferred from 3 types of incoming messages:
                //
                //   - From a "UNIT DESCRIPTION" DIMENSION READ message.
                //   - From a parameterized "WIRE PILOT SETPOINT MODE" BUS COMMAND message.
                //   - From a parameterized "WIRE PILOT DEROGATION MODE" BUS COMMAND message.
                //   - From a "CANCEL WIRE PILOT DEROGATION" BUS COMMAND message.

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : not null,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : ["6" or "132", { Length: > 0 } value] })
                    when dimension == OpenNettyDimensions.Diagnostics.UnitDescription:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                        {
                            var configuration = OpenNettyModels.TemperatureControl.PilotWireConfiguration.CreateFromUnitDescription([value]);
                            if (configuration.IsDerogationActive)
                            {
                                await _events.PublishAsync(new PilotWireDerogationModeReportedEventArgs(endpoint, configuration.Mode, configuration.DerogationDuration), cancellationToken);
                            }

                            else
                            {
                                await _events.PublishAsync(new PilotWireSetpointModeReportedEventArgs(endpoint, configuration.Mode), cancellationToken);
                                await _events.PublishAsync(new PilotWireDerogationModeReportedEventArgs(endpoint, null, null), cancellationToken);
                            }
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived or OpenNettyNotifications.MessageSent,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.BusCommand,
                                         Command  : OpenNettyCommand command,
                                         Address  : not null,
                                         Mode     : OpenNettyMode mode })
                    when command.WithParameters([]) == OpenNettyCommands.TemperatureControl.WirePilotSetpointMode &&
                         command.Parameters is [{ Length: > 0 } value]:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        List<Task> tasks = [];

                        if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                        {
                            tasks.Add(ReportSetpointModeAsync(endpoint, cancellationToken).AsTask());
                        }

                        if (endpoint is { Unit.Definition.AssociatedUnitId: byte unit })
                        {
                            tasks.Add(Task.Run(async () =>
                            {
                                var endpoint = await _manager.FindEndpointByAddressAsync(notification.Gateway,
                                    OpenNettyAddress.FromNitooAddress(
                                        OpenNettyAddress.ToNitooAddress(message.Address.Value).Identifier, unit));

                                if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                                {
                                    await ReportSetpointModeAsync(endpoint, cancellationToken);
                                }
                            }, cancellationToken));
                        }

                        if (endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Scenarios: [_, ..] scenarios } &&
                            message.Mode is OpenNettyMode.Broadcast or OpenNettyMode.Multicast)
                        {
                            var endpoints = scenarios.ToAsyncEnumerable()
                                .Where(static scenario => scenario.FunctionCode is 255)
                                .SelectAwait(scenario => _manager.FindEndpointByNameAsync(scenario.EndpointName))
                                .Where(static endpoint => endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit: OpenNettyUnit })
                                .OfType<OpenNettyEndpoint>()
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating));

                            tasks.Add(Parallel.ForEachAsync(endpoints, ReportSetpointModeAsync));
                        }

                        await Task.WhenAll(tasks);
                    });

                    // Note: setting the setpoint mode may not have an immediate effect on the device (e.g if a
                    // derogation mode was set with a minimal duration during which setpoint commands are ignored).
                    //
                    // As such, the derogation mode cannot be reported here, as it may still be active on the device.
                    async ValueTask ReportSetpointModeAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken) =>
                        await _events.PublishAsync(new PilotWireSetpointModeReportedEventArgs(endpoint, value switch
                        {
                            "0" => OpenNettyModels.TemperatureControl.PilotWireMode.Comfort,
                            "1" => OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne,
                            "2" => OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo,
                            "3" => OpenNettyModels.TemperatureControl.PilotWireMode.Eco,
                            "4" => OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection,

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                        }), cancellationToken);
                    break;
                }

                case (OpenNettyNotifications.MessageReceived or OpenNettyNotifications.MessageSent,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.BusCommand,
                                         Command  : OpenNettyCommand command,
                                         Address  : not null,
                                         Mode     : OpenNettyMode mode })
                    when command.WithParameters([]) == OpenNettyCommands.TemperatureControl.WirePilotDerogationMode &&
                         command.Parameters is [{ Length: > 0 } value]:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        List<Task> tasks = [];

                        if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                        {
                            tasks.Add(ReportDerogationModeAsync(endpoint, cancellationToken).AsTask());
                        }

                        if (endpoint is { Unit.Definition.AssociatedUnitId: byte unit })
                        {
                            tasks.Add(Task.Run(async () =>
                            {
                                var endpoint = await _manager.FindEndpointByAddressAsync(notification.Gateway,
                                    OpenNettyAddress.FromNitooAddress(
                                        OpenNettyAddress.ToNitooAddress(message.Address.Value).Identifier, unit));

                                if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                                {
                                    await ReportDerogationModeAsync(endpoint, cancellationToken);
                                }
                            }, cancellationToken));
                        }

                        if (endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Scenarios: [_, ..] scenarios } &&
                            message.Mode is OpenNettyMode.Broadcast or OpenNettyMode.Multicast)
                        {
                            var endpoints = scenarios.ToAsyncEnumerable()
                                .Where(static scenario => scenario.FunctionCode is 255)
                                .SelectAwait(scenario => _manager.FindEndpointByNameAsync(scenario.EndpointName))
                                .Where(static endpoint => endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit: OpenNettyUnit })
                                .OfType<OpenNettyEndpoint>()
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating));

                            tasks.Add(Parallel.ForEachAsync(endpoints, ReportDerogationModeAsync));
                        }

                        await Task.WhenAll(tasks);
                    });

                    async ValueTask ReportDerogationModeAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken) =>
                        await _events.PublishAsync(new PilotWireDerogationModeReportedEventArgs(endpoint,
                            value switch
                            {
                                "0" or "32" or "128" => OpenNettyModels.TemperatureControl.PilotWireMode.Comfort,
                                "1" or "33" or "129" => OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne,
                                "2" or "34" or "130" => OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo,
                                "3" or "35" or "131" => OpenNettyModels.TemperatureControl.PilotWireMode.Eco,
                                "4" or "36" or "132" => OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection,

                                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                            },
                            byte.Parse(value, CultureInfo.InvariantCulture) switch
                            {
                                          <  32 => OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None,
                                >= 32 and < 128 => OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours,
                                >= 128          => OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours
                            }), cancellationToken);
                    break;
                }

                case (OpenNettyNotifications.MessageReceived or OpenNettyNotifications.MessageSent,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.BusCommand,
                                         Command  : OpenNettyCommand command,
                                         Address  : not null,
                                         Mode     : OpenNettyMode mode })
                    when command == OpenNettyCommands.TemperatureControl.CancelWirePilotDerogationMode:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        List<Task> tasks = [];

                        if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                        {
                            tasks.Add(_events.PublishAsync(new PilotWireDerogationModeReportedEventArgs(endpoint, null, null), cancellationToken).AsTask());
                        }

                        if (endpoint is { Unit.Definition.AssociatedUnitId: byte unit })
                        {
                            tasks.Add(Task.Run(async () =>
                            {
                                var endpoint = await _manager.FindEndpointByAddressAsync(notification.Gateway,
                                    OpenNettyAddress.FromNitooAddress(
                                        OpenNettyAddress.ToNitooAddress(message.Address.Value).Identifier, unit));

                                if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                                {
                                    await _events.PublishAsync(new PilotWireDerogationModeReportedEventArgs(endpoint, null, null));
                                }
                            }, cancellationToken));
                        }

                        if (endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Scenarios: [_, ..] scenarios } &&
                            message.Mode is OpenNettyMode.Broadcast or OpenNettyMode.Multicast)
                        {
                            var endpoints = scenarios.ToAsyncEnumerable()
                                .Where(static scenario => scenario.FunctionCode is 255)
                                .SelectAwait(scenario => _manager.FindEndpointByNameAsync(scenario.EndpointName))
                                .Where(static endpoint => endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit: OpenNettyUnit })
                                .OfType<OpenNettyEndpoint>()
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating));

                            tasks.Add(Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                            {
                                await _events.PublishAsync(new PilotWireDerogationModeReportedEventArgs(endpoint, null, null), cancellationToken);
                            }));
                        }

                        await Task.WhenAll(tasks);
                    });
                    break;
                }

                // Note: the battery level is received when pushing the NETWORK or LEARN buttons on a Zigbee device.
                // To receive the battery level during normal operation, the LEARN button on the wireless device
                // must be pressed and a CEN+ binding request must be sent by the gateway to bind the two devices.
                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Zigbee,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : not null,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 } value] })
                    when dimension == OpenNettyDimensions.Management.BatteryInformation:
                {
                    // Note: while the battery level is reported using a unit-specific address, it applies to the entire
                    // device: this task retrieves the device endpoint and, if available, report its battery level.
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway,
                        OpenNettyAddress.FromDecimalZigbeeAddress(
                            identifier: OpenNettyAddress.ToZigbeeAddress(message.Address.Value).Identifier,
                            unit      : 0));

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.BatteryLevel))
                        {
                            await _events.PublishAsync(new BatteryLevelReportedEventArgs(endpoint, value switch
                            {
                                "0" => 5,
                                "1" => 33,
                                "2" => 66,
                                "3" => 100,

                                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                            }), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Nitoo,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand command,
                                         Address : not null })
                    when command == OpenNettyCommands.Management.BatteryWeak:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.BatteryAlert))
                        {
                            await _events.PublishAsync(new BatteryAlertReportedEventArgs(endpoint), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo or OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 }, { Length: > 0 }, { Length: > 0 }] values })
                    when dimension == OpenNettyDimensions.Management.FirmwareVersion:
                {
                    // Note: firmware version DIMENSION READ messages may be sent by the gateway itself or by a remote device.
                    if (message.Address is not null)
                    {
                        var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                        await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                        {
                            if (endpoint.HasCapability(OpenNettyCapabilities.FirmwareVersion))
                            {
                                await ReportFirmwareVersionAsync(endpoint, cancellationToken);
                            }
                        });
                    }

                    else
                    {
                        // Resolve the endpoint associated with the gateway that received the DIMENSION READ message.
                        var endpoint = await _manager.FindEndpointAsync(endpoint =>
                            endpoint.Device == notification.Gateway.Device && endpoint.Unit is null);

                        if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.FirmwareVersion))
                        {
                            await ReportFirmwareVersionAsync(endpoint, CancellationToken.None);
                        }
                    }

                    async ValueTask ReportFirmwareVersionAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken) =>
                        await _events.PublishAsync(new FirmwareVersionReportedEventArgs(endpoint, new Version(
                            major: int.Parse(values[0], CultureInfo.InvariantCulture),
                            minor: int.Parse(values[1], CultureInfo.InvariantCulture),
                            build: int.Parse(values[2], CultureInfo.InvariantCulture))), cancellationToken);
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo or OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 }, { Length: > 0 }, { Length: > 0 }] values })
                    when dimension == OpenNettyDimensions.Management.HardwareVersion:
                {
                    // Note: hardware version DIMENSION READ messages may be sent by the gateway itself or by a remote device.
                    if (message.Address is not null)
                    {
                        var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                        await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                        {
                            if (endpoint.HasCapability(OpenNettyCapabilities.HardwareVersion))
                            {
                                await ReportHardwareVersionAsync(endpoint, cancellationToken);
                            }
                        });
                    }

                    else
                    {
                        // Resolve the endpoint associated with the gateway that received the DIMENSION READ message.
                        var endpoint = await _manager.FindEndpointAsync(endpoint =>
                            endpoint.Device == notification.Gateway.Device && endpoint.Unit is null);

                        if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.HardwareVersion))
                        {
                            await ReportHardwareVersionAsync(endpoint, CancellationToken.None);
                        }
                    }

                    async ValueTask ReportHardwareVersionAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken) =>
                        await _events.PublishAsync(new HardwareVersionReportedEventArgs(endpoint, new Version(
                            major: int.Parse(values[0], CultureInfo.InvariantCulture),
                            minor: int.Parse(values[1], CultureInfo.InvariantCulture),
                            build: int.Parse(values[2], CultureInfo.InvariantCulture))), cancellationToken);
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo or OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : { Length: >= 6 } values })
                    when dimension == OpenNettyDimensions.Management.MacAddress:
                {
                    // Note: MAC address DIMENSION READ messages may be sent by the gateway itself or by a remote device.
                    if (message.Address is not null)
                    {
                        var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                        await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                        {
                            if (endpoint.HasCapability(OpenNettyCapabilities.MacAddress))
                            {
                                await ReportMacAddressAsync(endpoint, cancellationToken);
                            }
                        });
                    }

                    else
                    {
                        // Resolve the endpoint associated with the gateway that received the DIMENSION READ message.
                        var endpoint = await _manager.FindEndpointAsync(endpoint =>
                            endpoint.Device == notification.Gateway.Device && endpoint.Unit is null);

                        if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.MacAddress))
                        {
                            await ReportMacAddressAsync(endpoint, CancellationToken.None);
                        }
                    }

                    async ValueTask ReportMacAddressAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken)
                        => await _events.PublishAsync(new MacAddressReportedEventArgs(endpoint, string.Join(":",
                            values.Select(static value => uint.Parse(value, CultureInfo.InvariantCulture)
                                .ToString("X2", CultureInfo.InvariantCulture)))), cancellationToken);
                    break;
                }

                // Note: uptime DIMENSION READ messages never include an address, as they are sent by the gateway itself.
                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo or OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : null,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 }, { Length: > 0 }, { Length: > 0 }, { Length: > 0 }] values })
                    when dimension == OpenNettyDimensions.Management.Uptime:
                {
                    // Resolve the endpoint associated with the gateway that received the DIMENSION READ message.
                    var endpoint = await _manager.FindEndpointAsync(endpoint =>
                        endpoint.Device == notification.Gateway.Device && endpoint.Unit is null);

                    if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.Uptime))
                    {
                        await _events.PublishAsync(new UptimeReportedEventArgs(endpoint, new TimeSpan(
                            days   : int.Parse(values[0], CultureInfo.InvariantCulture),
                            hours  : int.Parse(values[1], CultureInfo.InvariantCulture),
                            minutes: int.Parse(values[2], CultureInfo.InvariantCulture),
                            seconds: int.Parse(values[3], CultureInfo.InvariantCulture))));
                    }
                    break;
                }

                // Note: Zigbee channel DIMENSION READ messages never include an address, as they are sent by the gateway itself.
                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Zigbee,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : null,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 } value] })
                    when dimension == OpenNettyDimensions.Management.ZigbeeChannel:
                {
                    // Resolve the endpoint associated with the gateway that received the DIMENSION READ message.
                    var endpoint = await _manager.FindEndpointAsync(endpoint =>
                        endpoint.Device == notification.Gateway.Device && endpoint.Unit is null);

                    if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                    {
                        await _events.PublishAsync(new ZigbeeChannelReportedEventArgs(endpoint, byte.Parse(value, CultureInfo.InvariantCulture)));
                    }
                    break;
                }

                // Note: Zigbee devices count DIMENSION READ messages never include an address, as they are sent by the gateway itself.
                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Zigbee,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : null,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 } value] })
                    when dimension == OpenNettyDimensions.Management.NumberOfProducts:
                {
                    // Resolve the endpoint associated with the gateway that received the DIMENSION READ message.
                    var endpoint = await _manager.FindEndpointAsync(endpoint =>
                        endpoint.Device == notification.Gateway.Device && endpoint.Unit is null);

                    if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                    {
                        await _events.PublishAsync(new ZigbeeDevicesCountReportedEventArgs(endpoint, byte.Parse(value, CultureInfo.InvariantCulture)));
                    }
                    break;
                }

                case (OpenNettyNotifications.MessageReceived or OpenNettyNotifications.MessageSent,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Zigbee,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand command,
                                         Address : null or not null })
                    when command == OpenNettyCommands.Management.CreateZigbeeNetwork ||
                         command == OpenNettyCommands.Management.CloseZigbeeNetwork  ||
                         command == OpenNettyCommands.Management.OpenZigbeeNetwork   ||
                         command == OpenNettyCommands.Management.JoinZigbeeNetwork   ||
                         command == OpenNettyCommands.Management.LeaveZigbeeNetwork:
                {
                    // Resolve the endpoint associated with the gateway that received the BUS COMMAND message.
                    var endpoint = await _manager.FindEndpointAsync(endpoint =>
                        endpoint.Device == notification.Gateway.Device && endpoint.Unit is null);

                    if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                    {
                        await _events.PublishAsync(new ZigbeeNetworkEventReportedEventArgs(endpoint,
                            command == OpenNettyCommands.Management.CreateZigbeeNetwork ? OpenNettyModels.Management.ZigbeeNetworkEventType.Created :
                            command == OpenNettyCommands.Management.CloseZigbeeNetwork  ? OpenNettyModels.Management.ZigbeeNetworkEventType.Closed  :
                            command == OpenNettyCommands.Management.OpenZigbeeNetwork   ? OpenNettyModels.Management.ZigbeeNetworkEventType.Opened  :
                            command == OpenNettyCommands.Management.JoinZigbeeNetwork   ? OpenNettyModels.Management.ZigbeeNetworkEventType.Joined  :
                            command == OpenNettyCommands.Management.LeaveZigbeeNetwork  ? OpenNettyModels.Management.ZigbeeNetworkEventType.Left    :
                            throw new InvalidDataException(SR.GetResourceString(SR.ID0068))));
                    }
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Zigbee,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand command,
                                         Address : not null })
                    when command == OpenNettyCommands.ScenariosPlus.OpenBinding ||
                         command == OpenNettyCommands.ScenariosPlus.CloseBinding ||
                         command == OpenNettyCommands.ScenariosPlus.CancelBinding:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.ZigbeeBinding))
                        {
                            await _events.PublishAsync(new ZigbeeBindingEventReportedEventArgs(endpoint,
                                command == OpenNettyCommands.ScenariosPlus.OpenBinding   ? OpenNettyModels.ScenariosPlus.ZigbeeBindingEventType.Opened   :
                                command == OpenNettyCommands.ScenariosPlus.CloseBinding  ? OpenNettyModels.ScenariosPlus.ZigbeeBindingEventType.Closed   :
                                command == OpenNettyCommands.ScenariosPlus.CancelBinding ? OpenNettyModels.ScenariosPlus.ZigbeeBindingEventType.Canceled :
                                throw new InvalidDataException(SR.GetResourceString(SR.ID0068))));
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : not null,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 }, { Length: > 0 }, { Length: > 0 }, { Length: > 0 }] values })
                    when dimension == OpenNettyDimensions.Diagnostics.DeviceDescription:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        var description = OpenNettyModels.Diagnostics.DeviceDescription.CreateFromDeviceDescription(values);

                        await Task.WhenAll(
                            _events.PublishAsync(new DeviceDescriptionReportedEventArgs(endpoint, description), cancellationToken).AsTask(),
                            _events.PublishAsync(new FirmwareVersionReportedEventArgs(endpoint, description.Version), cancellationToken).AsTask());
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived or OpenNettyNotifications.MessageSent,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionSet,
                                         Address  : not null,
                                         Mode     : OpenNettyMode.Broadcast,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 } value, ..] })
                    when dimension == OpenNettyDimensions.Lighting.DimmerStep:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.DimmingScenarioEvent))
                        {
                            var step = int.Parse(value, CultureInfo.InvariantCulture);
                            if (step is >= 128)
                            {
                                step -= 256;
                            }

                            await _events.PublishAsync(new DimmingStepReportedEventArgs(endpoint, step), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Zigbee,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand command,
                                         Address : not null })
                    when command == OpenNettyCommands.Lighting.Toggle:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.ToggleScenarioEvent))
                        {
                            await _events.PublishAsync(new ToggleScenarioReportedEventArgs(endpoint), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Scs,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand command,
                                         Address : not null })
                    // Note: pressure BUS COMMAND frames use the button number as the command.
                    when command.Category == OpenNettyCategories.Scenarios &&
                         byte.TryParse(command.Value, CultureInfo.InvariantCulture, out byte button) && button is <= 31:
                {
                    var type =
                        command.Parameters is [   ] ? OpenNettyModels.Scenarios.PressureScenarioType.Pressure :
                        command.Parameters is ["1"] ? OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterShortPressure :
                        command.Parameters is ["2"] ? OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterExtendedPressure :
                        command.Parameters is ["3"] ? OpenNettyModels.Scenarios.PressureScenarioType.ExtendedPressure :
                        throw new InvalidDataException(SR.GetResourceString(SR.ID0068));

                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioEvent))
                        {
                            await _events.PublishAsync(new PressureScenarioReportedEventArgs(endpoint, type, button), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand command,
                                         Address : not null })
                    // Note: pressure BUS COMMAND frames can be parameterized.
                    when command.WithParameters([]) == OpenNettyCommands.ScenariosPlus.ShortPressure ||
                         command.WithParameters([]) == OpenNettyCommands.ScenariosPlus.StartOfExtendedPressure ||
                         command.WithParameters([]) == OpenNettyCommands.ScenariosPlus.ExtendedPressure ||
                         command.WithParameters([]) == OpenNettyCommands.ScenariosPlus.EndOfExtendedPressure:
                {
                    var type =
                        command.WithParameters([]) == OpenNettyCommands.ScenariosPlus.ShortPressure           ? OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure :
                        command.WithParameters([]) == OpenNettyCommands.ScenariosPlus.StartOfExtendedPressure ? OpenNettyModels.ScenariosPlus.PressureScenarioType.StartOfExtendedPressure :
                        command.WithParameters([]) == OpenNettyCommands.ScenariosPlus.ExtendedPressure        ? OpenNettyModels.ScenariosPlus.PressureScenarioType.ExtendedPressure :
                        command.WithParameters([]) == OpenNettyCommands.ScenariosPlus.EndOfExtendedPressure   ? OpenNettyModels.ScenariosPlus.PressureScenarioType.EndOfExtendedPressure :
                        throw new InvalidDataException(SR.GetResourceString(SR.ID0068));

                    byte? button = command.Parameters is [string value] ? byte.Parse(value, CultureInfo.InvariantCulture) : null;

                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioPlusEvent))
                        {
                            await _events.PublishAsync(new PressureScenarioPlusReportedEventArgs(endpoint, type, button), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived or OpenNettyNotifications.MessageSent,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Nitoo,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand command,
                                         Address : not null,
                                         Mode    : OpenNettyMode mode })
                    // Note: timed and progressive scenarios are parameterized.
                    when command == OpenNettyCommands.ScenariosPlus.Action ||
                         command.WithParameters([]) == OpenNettyCommands.ScenariosPlus.ActionForTime ||
                         command.WithParameters([]) == OpenNettyCommands.ScenariosPlus.ActionInTime:
                {
                    var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address.Value);

                    await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                    {
                        List<Task> tasks = [];

                        if (command == OpenNettyCommands.ScenariosPlus.Action)
                        {
                            if (!endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioEvent))
                            {
                                return;
                            }

                            if (mode is OpenNettyMode.Broadcast && notification is OpenNettyNotifications.MessageReceived)
                            {
                                tasks.Add(_events.PublishAsync(new BasicScenarioReportedEventArgs(endpoint), cancellationToken).AsTask());
                            }

                            // Note: since they are radiofrequency devices, the current state of Nitoo wireless burglar alarms
                            // cannot be retrieved using a unit description request. To inform devices associated using a
                            // PnL scenario of a state change, Nitoo alarms broadcast it using unit-specific ACTION scenarios.
                            tasks.Add(Task.Run(async () =>
                            {
                                var (identifier, unit) = OpenNettyAddress.ToNitooAddress(message.Address.Value);
                                if (unit is not (>= 4 and <= 9))
                                {
                                    return;
                                }

                                var endpoint = await _manager.FindEndpointByAddressAsync(notification.Gateway,
                                    OpenNettyAddress.FromNitooAddress(identifier, 0));

                                if (endpoint is null || !endpoint.HasCapability(OpenNettyCapabilities.WirelessBurglarAlarmState))
                                {
                                    return;
                                }

                                await _events.PublishAsync(new WirelessBurglarAlarmStateReportedEventArgs(endpoint, unit switch
                                {
                                    4 => OpenNettyModels.Alarm.WirelessBurglarAlarmState.Armed,
                                    5 => OpenNettyModels.Alarm.WirelessBurglarAlarmState.Disarmed,
                                    6 => OpenNettyModels.Alarm.WirelessBurglarAlarmState.PartiallyArmed,
                                    7 => OpenNettyModels.Alarm.WirelessBurglarAlarmState.Triggered,
                                    8 => OpenNettyModels.Alarm.WirelessBurglarAlarmState.ExitDelayElapsed,
                                    9 => OpenNettyModels.Alarm.WirelessBurglarAlarmState.EventDetected,

                                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                                }));
                            }, cancellationToken));
                        }

                        else if (command.Value == OpenNettyCommands.ScenariosPlus.ActionForTime.Value)
                        {
                            if (!endpoint.HasCapability(OpenNettyCapabilities.TimedScenarioEvent))
                            {
                                return;
                            }

                            if (notification is OpenNettyNotifications.MessageReceived && mode is OpenNettyMode.Broadcast)
                            {
                                var duration = Math.Round(double.Parse(command.Parameters[0], CultureInfo.InvariantCulture) / 5, MidpointRounding.AwayFromZero);

                                await _events.PublishAsync(new TimedScenarioReportedEventArgs(endpoint, TimeSpan.FromSeconds(duration)), cancellationToken);
                            }
                        }

                        else if (command.Value == OpenNettyCommands.ScenariosPlus.ActionInTime.Value)
                        {
                            if (!endpoint.HasCapability(OpenNettyCapabilities.ProgressiveScenarioEvent))
                            {
                                return;
                            }

                            if (notification is OpenNettyNotifications.MessageReceived && mode is OpenNettyMode.Broadcast)
                            {
                                var duration = Math.Round(double.Parse(command.Parameters[0], CultureInfo.InvariantCulture) / 5, MidpointRounding.AwayFromZero);

                                await _events.PublishAsync(new ProgressiveScenarioReportedEventArgs(endpoint, TimeSpan.FromSeconds(duration)), cancellationToken);
                            }
                        }

                        // Note: on endpoints that don't support dimming, a "SCENARIO ACTION", "SCENARIO ACTION IN TIME" or
                        // "SCENARIO ACTION FOR TIME" BUS COMMAND always results in the associated unit being switched on.
                        //
                        // For endpoints that support dimming, the actual brightness level is retrieved asynchronously
                        // by a dedicated event handler to ensure the exact brightness level is correctly reported.
                        if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState) &&
                           !endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) &&
                           !endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                        {
                            tasks.Add(ReportOnStateAsync(endpoint, cancellationToken).AsTask());
                        }

                        if (endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Definition.AssociatedUnitId: byte unit })
                        {
                            tasks.Add(Task.Run(async () =>
                            {
                                var endpoint = await _manager.FindEndpointByAddressAsync(notification.Gateway,
                                    OpenNettyAddress.FromNitooAddress(
                                        OpenNettyAddress.ToNitooAddress(message.Address.Value).Identifier, unit));

                                if (endpoint is null || !endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                                {
                                    return;
                                }

                                // Note: on endpoints that don't support dimming, a "SCENARIO ACTION", "SCENARIO ACTION IN TIME" or
                                // "SCENARIO ACTION FOR TIME" BUS COMMAND always results in the associated unit being switched on.
                                //
                                // For endpoints that support dimming, the actual brightness level is retrieved asynchronously
                                // by a dedicated event handler to ensure the exact brightness level is correctly reported.
                                if (!endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) &&
                                    !endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                                {
                                    await ReportOnStateAsync(endpoint, cancellationToken);
                                }
                            }, cancellationToken));
                        }

                        if (endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Scenarios: [_, ..] scenarios })
                        {
                            tasks.Add(Parallel.ForEachAsync(scenarios, async (scenario, cancellationToken) =>
                            {
                                var endpoint = await _manager.FindEndpointByNameAsync(scenario.EndpointName, cancellationToken);
                                if (endpoint is null)
                                {
                                    return;
                                }

                                if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                                {
                                    if (scenario.FunctionCode is 101 or 103 or (>= 0 and <= 100))
                                    {
                                        await ReportOnStateAsync(endpoint, cancellationToken);
                                    }

                                    else if (scenario.FunctionCode is 102 or 104)
                                    {
                                        await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint,
                                            OpenNettyModels.Lighting.SwitchState.Off), cancellationToken);
                                    }
                                }

                                if (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                                    endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                                {
                                    // Note: for Nitoo devices supporting dimming, an ON scenario always changes the brightness to 100%.
                                    if (scenario.FunctionCode is 101 or 103)
                                    {
                                        await _events.PublishAsync(new BrightnessReportedEventArgs(endpoint, 100), cancellationToken);
                                    }

                                    // Note: the special brightness level "0" always indicates that the output is switched off.
                                    // To avoid overriding the last known level (which is typically restored by SCS devices when
                                    // receiving an ON command), the brightness level is only reported if it's higher than zero.
                                    // 
                                    // Note: while Nitoo devices normally don't restore the last known brightness level when
                                    // receiving an ON command, the same rule applies for consistency with MyHome/SCS devices.
                                    else if (scenario.FunctionCode is >= 1 and <= 100)
                                    {
                                        await _events.PublishAsync(new BrightnessReportedEventArgs(endpoint, scenario.FunctionCode), cancellationToken);
                                    }
                                }
                            }));
                        }

                        await Task.WhenAll(tasks);
                    });

                    async ValueTask ReportOnStateAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken)
                    {
                        await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint,
                            OpenNettyModels.Lighting.SwitchState.On), cancellationToken);

                        // Note: if the endpoint was configured to use the push-button mode,
                        // dispatch an "OFF state" event immediately after switching it on.
                        if (endpoint.GetStringSetting(OpenNettySettings.SwitchMode) is OpenNettySettings.SwitchModes.PushButton)
                        {
                            await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint,
                                OpenNettyModels.Lighting.SwitchState.Off), cancellationToken);
                        }
                    }
                    break;
                }
            }
        })
        .Do(onError: exception => _logger.LogWarning(6018, exception, SR.GetResourceString(SR.ID6018)))
        .Retry()
        .SubscribeAsync(static message => ValueTask.CompletedTask),

        // Note: this event handler is responsible for retrieving the exact switch state and brightless level of
        // endpoints that are directly or indirectly affected by BUS COMMAND or DIMENSION SET frames that don't
        // provide accurate information about their state.
        await _pipeline.SelectMany(static notification => notification switch
        {
            // Note: brightness level changes reported via ON% BUS COMMAND frames are deliberately ignored for
            // SCS and Zigbee endpoints that support advanced dimming, as this method often gives very imprecise
            // results that are inconsistent with the brightness level retrieved using a "DIMMER LEVEL SPEED"
            // or "DIMMER STATUS" DIMENSION REQUEST frame (e.g when setting the brightness to 30%, a F418U2 SCS
            // dimmer correctly reports the "130" value when using a DIMENSION REQUEST but returns "5" (50%)
            // when using a STATUS REQUEST). To avoid that, this event handler monitors all the ON% BUS COMMAND
            // frames and retrieves the exact brightness level using a "DIMMER LEVEL SPEED" DIMENSION REQUEST.
            OpenNettyNotifications.MessageReceived {
                Session.Type: OpenNettySessionType.Event,
                Message: {
                    Protocol: OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee,
                    Type    : OpenNettyMessageType.BusCommand,
                    Command : OpenNettyCommand command,
                    Address : not null } message }
                when command == OpenNettyCommands.Lighting.On20 ||
                     command == OpenNettyCommands.Lighting.On30 ||
                     command == OpenNettyCommands.Lighting.On40 ||
                     command == OpenNettyCommands.Lighting.On50 ||
                     command == OpenNettyCommands.Lighting.On60 ||
                     command == OpenNettyCommands.Lighting.On70 ||
                     command == OpenNettyCommands.Lighting.On80 ||
                     command == OpenNettyCommands.Lighting.On90 ||
                     command == OpenNettyCommands.Lighting.On100
                    => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            // Note: some Nitoo devices (like the 67210, 67212 and 67214 dimmers) offer preset buttons that allow
            // setting the brightness to a fixed value configured by the user directly on the device. When pressed,
            // the dimmer moves to the specified level and emits a SCENARIO ACTION frame but doesn't specify the
            // actual value, that must be retrieved separately using a DIMENSION REQUEST to determine the exact level.
            OpenNettyNotifications.MessageReceived {
                Session.Type: OpenNettySessionType.Generic,
                Message: {
                    Protocol: OpenNettyProtocol.Nitoo,
                    Type    : OpenNettyMessageType.BusCommand,
                    Command : OpenNettyCommand command,
                    Address : not null,
                    Mode    : OpenNettyMode.Broadcast } message }
                when command == OpenNettyCommands.ScenariosPlus.Action
                    => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            OpenNettyNotifications.MessageReceived {
                Session.Type: OpenNettySessionType.Generic,
                Message: {
                    Protocol: OpenNettyProtocol.Nitoo,
                    Type    : OpenNettyMessageType.BusCommand,
                    Command : OpenNettyCommand command,
                    Address : not null,
                    Mode    : OpenNettyMode.Broadcast } message }
                // Note: timed scenarios are reported using parameterized BUS COMMAND frames.
                when command.WithParameters([]) == OpenNettyCommands.ScenariosPlus.ActionForTime
                    => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            OpenNettyNotifications.MessageReceived {
                Session.Type: OpenNettySessionType.Generic,
                Message: {
                    Protocol: OpenNettyProtocol.Nitoo,
                    Type    : OpenNettyMessageType.BusCommand,
                    Command : OpenNettyCommand command,
                    Address : not null,
                    Mode    : OpenNettyMode.Broadcast } message }
                // Note: progressive scenarios are reported using parameterized BUS COMMAND frames.
                when command.WithParameters([]) == OpenNettyCommands.ScenariosPlus.ActionInTime
                    => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            // Nitoo devices allow changing the brightness level of a local unit (and of associated
            // devices) by using a long pressure. For that, Nitoo devices broadcast "DIM STEP"
            // DIMENSION SET frames until the pressed button is released by the user. While the final
            // brightness level can be estimated using the number of "DIM STEP" frames received, this
            // method is sadly very imprecise. To avoid that, "DIM STEP" frames are monitored and the
            // exact brightness is retrieved from the Nitoo device itself using a DIMENSION REQUEST.
            OpenNettyNotifications.MessageReceived {
                Session.Type: OpenNettySessionType.Generic,
                Message: {
                    Protocol : OpenNettyProtocol.Nitoo,
                    Type     : OpenNettyMessageType.DimensionSet,
                    Address  : not null,
                    Mode     : OpenNettyMode.Broadcast,
                    Dimension: OpenNettyDimension dimension } message }
                when dimension == OpenNettyDimensions.Lighting.DimmerStep
                    => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            _ => AsyncObservable.Empty<(OpenNettyNotification Notification, OpenNettyMessage Message)>()
        })
        .GroupBy(static arguments => arguments.Message.Address!.Value)
        .SelectMany(static group => group.Throttle(TimeSpan.FromSeconds(group.Key.Type is OpenNettyAddressType.Nitoo ? 0.5 : 1)))
        .Do(onNext: async arguments =>
        {
            var (notification, message) = arguments;

            var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address!.Value);

            await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
            {
                switch (message)
                {
                    case { Type: OpenNettyMessageType.BusCommand, Command: OpenNettyCommand command }
                        when command == OpenNettyCommands.ScenariosPlus.Action && !endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioEvent):
                        return;

                    case { Type: OpenNettyMessageType.BusCommand, Command: OpenNettyCommand command }
                        when command == OpenNettyCommands.Lighting.On20 ||
                             command == OpenNettyCommands.Lighting.On30 ||
                             command == OpenNettyCommands.Lighting.On40 ||
                             command == OpenNettyCommands.Lighting.On50 ||
                             command == OpenNettyCommands.Lighting.On60 ||
                             command == OpenNettyCommands.Lighting.On70 ||
                             command == OpenNettyCommands.Lighting.On80 ||
                             command == OpenNettyCommands.Lighting.On90 ||
                             command == OpenNettyCommands.Lighting.On100:
                        // Note: retrieving the state of an endpoint that doesn't support advanced dimming
                        // isn't necessary, as the ON% frames are used by the main event handler to change
                        // the brightless level of endpoints that only support basic dimming.
                        if (!endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                            !endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                        {
                            return;
                        }
                        break;

                    case { Type: OpenNettyMessageType.BusCommand, Command: OpenNettyCommand command }
                        when command.WithParameters([]) == OpenNettyCommands.ScenariosPlus.ActionForTime &&
                            !endpoint.HasCapability(OpenNettyCapabilities.TimedScenarioEvent):
                        return;

                    case { Type: OpenNettyMessageType.BusCommand, Command: OpenNettyCommand command }
                        when command.WithParameters([]) == OpenNettyCommands.ScenariosPlus.ActionInTime &&
                            !endpoint.HasCapability(OpenNettyCapabilities.ProgressiveScenarioEvent):
                        return;

                    case { Type: OpenNettyMessageType.DimensionSet, Dimension: OpenNettyDimension dimension }
                        when dimension == OpenNettyDimensions.Lighting.DimmerStep &&
                            !endpoint.HasCapability(OpenNettyCapabilities.DimmingScenarioEvent):
                        return;
                }

                List<Task> tasks = [];

                if (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                    endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                {
                    tasks.Add(_controller.GetBrightnessAsync(endpoint, cancellationToken).AsTask());
                }

                if (endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Definition.AssociatedUnitId: byte unit })
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var endpoint = await _manager.FindEndpointByAddressAsync(notification.Gateway,
                            OpenNettyAddress.FromNitooAddress(
                                OpenNettyAddress.ToNitooAddress(message.Address!.Value).Identifier, unit));

                        if (endpoint is null)
                        {
                            return;
                        }

                        if (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                            endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                        {
                            _ = await _controller.GetBrightnessAsync(endpoint);
                        }

                        // Note: on endpoints that don't support dimming, retrieving their actual status isn't necessary as a
                        // "SCENARIO ACTION", "SCENARIO ACTION IN TIME" or "SCENARIO ACTION FOR TIME" BUS COMMAND always results
                        // in the associated unit being switched on, which is a case already handled by the main event handler.
                    }, cancellationToken));
                }

                if (message.Protocol  is OpenNettyProtocol.Nitoo                 &&
                    message.Type      is OpenNettyMessageType.DimensionSet       &&
                    message.Dimension == OpenNettyDimensions.Lighting.DimmerStep &&
                    endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Scenarios: [_, ..] scenarios })
                {
                    var endpoints = scenarios.ToAsyncEnumerable()
                        .Where(static scenario => scenario.FunctionCode is < 105)
                        .SelectAwait(scenario => _manager.FindEndpointByNameAsync(scenario.EndpointName))
                        .Where(static endpoint => endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit: OpenNettyUnit })
                        .OfType<OpenNettyEndpoint>()
                        // Note: "DIM STEP" BUS COMMANDS don't have any effect on endpoints that don't support dimming.
                        .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                                                  endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState));

                    tasks.Add(Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                        await _controller.GetBrightnessAsync(endpoint, cancellationToken)));
                }

                await Task.WhenAll(tasks);
            });
        })
        .Do(onError: exception => _logger.LogWarning(6018, exception, SR.GetResourceString(SR.ID6018)))
        .Retry()
        .SubscribeAsync(static notification => ValueTask.CompletedTask),

        // Note: this event handler is responsible for reporting state changes of endpoints that received - directly 
        // or indirectly via a Nitoo PnL scenario - a timed scenario command at the end of the specified duration.
        await _pipeline.SelectMany(static notification => notification switch
        {
            OpenNettyNotifications.MessageReceived {
                Session.Type: OpenNettySessionType.Generic,
                Message: {
                    Protocol: OpenNettyProtocol.Nitoo,
                    Type    : OpenNettyMessageType.BusCommand,
                    Command : OpenNettyCommand command,
                    Address : not null,
                    Mode    : OpenNettyMode.Broadcast } message }
                // Note: timed scenarios are reported using parameterized BUS COMMAND frames.
                when command.WithParameters([]) == OpenNettyCommands.ScenariosPlus.ActionForTime &&
                     Math.Round(double.Parse(command.Parameters[0], CultureInfo.InvariantCulture) / 5, MidpointRounding.AwayFromZero) is double duration
                     => AsyncObservable.Timer(TimeSpan.FromSeconds(duration)).Select(_ => (Notification: notification, Message: message)),

            _ => AsyncObservable.Empty<(OpenNettyNotification Notification, OpenNettyMessage Message)>()
        })
        .Do(onNext: async arguments =>
        {
            var (notification, message) = arguments;

            var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address!.Value);

            await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
            {
                if (!endpoint.HasCapability(OpenNettyCapabilities.TimedScenarioEvent))
                {
                    return;
                }

                List<Task> tasks = [];

                if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                {
                    tasks.Add(ReportOffStateAsync(endpoint, cancellationToken).AsTask());
                }

                if (endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Definition.AssociatedUnitId: byte unit })
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var endpoint = await _manager.FindEndpointByAddressAsync(notification.Gateway,
                            OpenNettyAddress.FromNitooAddress(
                                OpenNettyAddress.ToNitooAddress(message.Address!.Value).Identifier, unit));

                        if (endpoint is not null &&
                            endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                        {
                            await ReportOffStateAsync(endpoint, cancellationToken);
                        }
                    }, cancellationToken));
                }

                if (endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Scenarios: [_, ..] scenarios })
                {
                    var endpoints = scenarios.ToAsyncEnumerable()
                        .Where(static scenario => scenario.FunctionCode is < 105)
                        .SelectAwait(scenario => _manager.FindEndpointByNameAsync(scenario.EndpointName))
                        .Where(static endpoint => endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit: OpenNettyUnit })
                        .OfType<OpenNettyEndpoint>()
                        .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState));

                    tasks.Add(Parallel.ForEachAsync(endpoints, ReportOffStateAsync));
                }

                await Task.WhenAll(tasks);
            });

            ValueTask ReportOffStateAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken)
                => _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.Off), cancellationToken);
        })
        .Do(onError: exception => _logger.LogWarning(6018, exception, SR.GetResourceString(SR.ID6018)))
        .Retry()
        .SubscribeAsync(static notification => ValueTask.CompletedTask),

        // Note: this event handler is responsible for retrieving the pilot wire configuration of an endpoint
        // immediately after the setpoint mode or derogation mode was changed via an outgoing BUS COMMAND message.
        await _pipeline.SelectMany(static notification => notification switch
        {
            OpenNettyNotifications.MessageReceived {
                Session.Type: OpenNettySessionType.Generic,
                Message: {
                    Protocol: OpenNettyProtocol.Nitoo,
                    Type    : OpenNettyMessageType.BusCommand,
                    Command : OpenNettyCommand command,
                    Address : not null,
                    Mode    : OpenNettyMode.Broadcast } message }
                // Note: pilot wire mode changes are reported using parameterized BUS COMMAND frames.
                when command.WithParameters([]) == OpenNettyCommands.TemperatureControl.WirePilotSetpointMode
                    => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            OpenNettyNotifications.MessageReceived {
                Session.Type: OpenNettySessionType.Generic,
                Message: {
                    Protocol: OpenNettyProtocol.Nitoo,
                    Type    : OpenNettyMessageType.BusCommand,
                    Command : OpenNettyCommand command,
                    Address : not null,
                    Mode    : OpenNettyMode.Broadcast } message }
                when command == OpenNettyCommands.TemperatureControl.CancelWirePilotDerogationMode
                    => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            OpenNettyNotifications.MessageSent {
                Session.Type: OpenNettySessionType.Generic,
                Message: {
                    Protocol: OpenNettyProtocol.Nitoo,
                    Type    : OpenNettyMessageType.BusCommand,
                    Command : OpenNettyCommand command,
                    Address : not null } message }
                // Note: pilot wire mode changes are emitted using parameterized BUS COMMAND frames.
                when command.WithParameters([]) == OpenNettyCommands.TemperatureControl.WirePilotSetpointMode
                     => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            OpenNettyNotifications.MessageSent {
                Session.Type: OpenNettySessionType.Generic,
                Message: {
                    Protocol: OpenNettyProtocol.Nitoo,
                    Type    : OpenNettyMessageType.BusCommand,
                    Command : OpenNettyCommand command,
                    Address : not null } message }
                when command == OpenNettyCommands.TemperatureControl.CancelWirePilotDerogationMode
                    => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            _ => AsyncObservable.Empty<(OpenNettyNotification Notification, OpenNettyMessage Message)>()
        })
        .GroupBy(static arguments => arguments.Message.Address)
        .SelectMany(static group => group.Throttle(TimeSpan.FromSeconds(0.5)))
        .Do(onNext: async arguments =>
        {
            var (notification, message) = arguments;

            var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address!.Value);

            await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
            {
                if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                {
                    _ = await _controller.GetPilotWireConfigurationAsync(endpoint, cancellationToken);
                }
            });
        })
        .Do(onError: exception => _logger.LogWarning(6018, exception, SR.GetResourceString(SR.ID6018)))
        .Retry()
        .SubscribeAsync(static notification => ValueTask.CompletedTask),

        // Note: this event handler is responsible for reporting water heater setpoint mode changes
        // that can be inferred when receiving an "off-peak rate" notification from the smart meter.
        await _pipeline.SelectMany(static notification => notification switch
        {
            OpenNettyNotifications.MessageReceived {
                Session.Type: OpenNettySessionType.Generic,
                Message: {
                    Protocol : OpenNettyProtocol.Nitoo,
                    Type     : OpenNettyMessageType.DimensionRead,
                    Dimension: OpenNettyDimension dimension,
                    Address  : not null,
                    Mode     : OpenNettyMode.Broadcast,
                    Values   : [{ Length: > 0 }] } message }
                when dimension == OpenNettyDimensions.TemperatureControl.SmartMeterRateType
                    => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            _ => AsyncObservable.Empty<(OpenNettyNotification Notification, OpenNettyMessage Message)>()
        })
        .GroupBy(static arguments => arguments.Message.Address)
        .SelectMany(static group => group.Throttle(TimeSpan.FromSeconds(2.5)))
        .Do(onNext: async arguments =>
        {
            var (notification, message) = arguments;

            var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address!.Value);

            await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
            {
                if (!endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                {
                    return;
                }

                var endpoints = _manager.EnumerateEndpointsAsync(cancellationToken)
                    .Where(static endpoint => endpoint.Protocol is OpenNettyProtocol.Nitoo)
                    .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                    .Where(endpoint => endpoint.Address is OpenNettyAddress address &&
                        OpenNettyAddress.ToNitooAddress(address) is { Identifier: uint identifier } &&
                        identifier == OpenNettyAddress.ToNitooAddress(message.Address!.Value).Identifier);

                await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                {
                    switch (message.Values, await _controller.GetUnitDescriptionAsync(endpoint, cancellationToken))
                    {
                        // If the unit description indicates water is not heating when an "off-peak" signal
                        // is received, this means that production of hot water was turned off by the user.
                        case (["2"], { FunctionCode: 133, Values: ["0" or "32"] }):
                            await _events.PublishAsync(new WaterHeaterSetpointModeReportedEventArgs(endpoint,
                                OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOff), cancellationToken);
                            break;

                        case (["2"], { FunctionCode: 133, Values: ["1" or "33"] }):
                            await _events.PublishAsync(new WaterHeaterSetpointModeReportedEventArgs(endpoint,
                                OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic), cancellationToken);
                            break;

                        case (["2"], { FunctionCode: 133, Values: ["17"] }):
                            await _events.PublishAsync(new WaterHeaterSetpointModeReportedEventArgs(endpoint,
                                OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn), cancellationToken);
                            break;
                    }
                });
            });
        })
        .Do(onError: exception => _logger.LogWarning(6018, exception, SR.GetResourceString(SR.ID6018)))
        .Retry()
        .SubscribeAsync(static notification => ValueTask.CompletedTask),

        // Note: this event handler is responsible for retrieving the water heating state immediately
        // after the heating mode was modified by the user via an outgoing DIMENSION SET message.
        await _pipeline.SelectMany(static notification => notification switch
        {
            OpenNettyNotifications.MessageSent {
                Session.Type: OpenNettySessionType.Generic,
                Message: {
                    Protocol : OpenNettyProtocol.Nitoo,
                    Type     : OpenNettyMessageType.DimensionSet,
                    Dimension: OpenNettyDimension dimension,
                    Address  : not null,
                    Values   : [{ Length: > 0 }] } message }
                when dimension == OpenNettyDimensions.TemperatureControl.WaterHeatingMode
                    => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            _ => AsyncObservable.Empty<(OpenNettyNotification Notification, OpenNettyMessage Message)>()
        })
        .GroupBy(static arguments => arguments.Message.Address)
        .SelectMany(static group => group.Throttle(TimeSpan.FromSeconds(0.5)))
        .Do(onNext: async arguments =>
        {
            var (notification, message) = arguments;

            var endpoints = _manager.FindEndpointsByAddressAsync(notification.Gateway, message.Address!.Value);

            await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
            {
                if (endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                {
                    _ = await _controller.GetWaterHeaterStateAsync(endpoint, cancellationToken);
                }
            });
        })
        .Do(onError: exception => _logger.LogWarning(6018, exception, SR.GetResourceString(SR.ID6018)))
        .Retry()
        .SubscribeAsync(static notification => ValueTask.CompletedTask),

        // Note: this event is responsible for synchronizing the date/time of compatible OpenWebNet gateways at regular intervals.
        await AsyncObservable.Interval(TimeSpan.FromMinutes(10))
        .ObserveOn(TaskPoolAsyncScheduler.Default)
        .Do(onNext: async _ => await Parallel.ForEachAsync(_manager.EnumerateGatewaysAsync(), async (gateway, cancellationToken) =>
        {
            var endpoint = await _manager.FindEndpointAsync(endpoint => endpoint.Device == gateway.Device && endpoint.Unit is null, cancellationToken);
            if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.DateTime) &&
                endpoint.GetBooleanSetting(OpenNettySettings.ClockSynchronization) is not false)
            {
                await _controller.SetDateTimeAsync(endpoint, DateTimeOffset.Now, cancellationToken);
            }
        }))
        .Do(onError: exception => _logger.LogWarning(6018, exception, SR.GetResourceString(SR.ID6018)))
        .Retry()
        .SubscribeAsync(static message => ValueTask.CompletedTask)
    ]);
}
