using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
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
    private readonly OpenNettyLogger<OpenNettyCoordinator> _logger;
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
        OpenNettyLogger<OpenNettyCoordinator> logger,
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
                              OpenNettyMessageType.DimensionSet,
                    Address : not null,
                    Mode    : OpenNettyMode.Broadcast } message }
                => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            OpenNettyNotifications.MessageReceived {
                Session.Type: OpenNettySessionType.Event,
                Message: {
                    Protocol: OpenNettyProtocol.Scs,
                    Type    : OpenNettyMessageType.BusCommand    or
                              OpenNettyMessageType.DimensionRead or
                              OpenNettyMessageType.DimensionSet,
                    Address : not null } message }
                => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            OpenNettyNotifications.MessageReceived {
                Session.Type: OpenNettySessionType.Generic,
                Message: {
                    Protocol: OpenNettyProtocol.Zigbee,
                    Type    : OpenNettyMessageType.BusCommand    or
                              OpenNettyMessageType.DimensionRead or
                              OpenNettyMessageType.DimensionSet,
                    Address : not null } message }
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
                    Type    : OpenNettyMessageType.BusCommand or OpenNettyMessageType.DimensionSet,
                    Address : not null } message }
                => AsyncObservable.Return<(OpenNettyNotification Notification, OpenNettyMessage Message)>((notification, message)),

            _ => AsyncObservable.Empty<(OpenNettyNotification Notification, OpenNettyMessage Message)>()
        })
        .Do(async arguments =>
        {
            // Important: to ensure the order of events is preserved, this RX event handler processes incoming and outgoing
            // messages sequentially. As such, it is critical that this asynchronous method completes as quickly as possible.

            switch (arguments)
            {
                // The switch state and the brightness level of an endpoint can be inferred from 6 types of messages:
                //
                //   - For Nitoo devices, from an incoming or outgoing "OFF" or "ON" BUS COMMAND message.
                //   - For SCS/Zigbee devices, from an incoming "OFF" or "ON" - parameterized or not - BUS COMMAND message.
                //   - For SCS/Zigbee devices, from an incoming "ON%" BUS COMMAND message.
                //   - For SCS/Zigbee devices, from an incoming "DIMMER SPEED LEVEL" or "DIMMER STATUS" DIMENSION READ message.
                //   - For Nitoo devices, from an incoming "UNIT DESCRIPTION" DIMENSION READ message.
                //   - For Nitoo devices, from an outgoing "DIMMER SPEED LEVEL" DIMENSION SET message.

                case (OpenNettyNotification notification,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Nitoo,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand command,
                                         Address : OpenNettyAddress address,
                                         Mode    : OpenNettyMode mode })
                    when command == OpenNettyCommands.Lighting.Off || command == OpenNettyCommands.Lighting.On:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    // If the message was received and was emitted by the source using
                    // a broadcast transmission, it is considered as an ON/OFF scenario.
                    if (notification is OpenNettyNotifications.MessageReceived && mode is OpenNettyMode.Broadcast &&
                        endpoint.HasCapability(OpenNettyCapabilities.OnOffScenario))
                    {
                        await _events.PublishAsync(new OnOffScenarioReportedEventArgs(endpoint, command == OpenNettyCommands.Lighting.On ?
                            OpenNettyModels.Lighting.SwitchState.On :
                            OpenNettyModels.Lighting.SwitchState.Off));
                    }

                    List<Task> tasks = [];

                    // Note: outgoing ON/OFF commands sent using broadcast or multicast transmission
                    // generally don't affect the local output of a Nitoo lighting device. As such,
                    // the switch state/brightness of the endpoint is only reported if the message was
                    // received (e.g by a different unit on the same device) or was sent in unicast.
                    if (mode is OpenNettyMode.Unicast || notification is OpenNettyNotifications.MessageReceived)
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState) ||
                            endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                            endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                        {
                            tasks.Add(ReportStateAsync(endpoint, CancellationToken.None).AsTask());
                        }

                        if (endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Definition.AssociatedUnitId: ushort unit })
                        {
                            tasks.Add(Task.Run(async () =>
                            {
                                var endpoint = await _manager.FindEndpointByAddressAsync(OpenNettyAddress.FromNitooAddress(
                                    OpenNettyAddress.ToNitooAddress(address).Identifier, unit));

                                if (endpoint is null)
                                {
                                    return;
                                }

                                if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState) ||
                                    endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                                    endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                                {
                                    await ReportStateAsync(endpoint, CancellationToken.None);
                                }
                            }));
                        }
                    }

                    if (mode is OpenNettyMode.Broadcast or OpenNettyMode.Multicast && endpoint is { Unit.Scenarios: [_, ..] scenarios })
                    {
                        var endpoints = scenarios.ToAsyncEnumerable()
                            .Where(static scenario => scenario.FunctionCode is < 105)
                            .SelectAwait(scenario => _manager.FindEndpointByNameAsync(scenario.EndpointName))
                            .Where(static endpoint => endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit: OpenNettyUnit })
                            .OfType<OpenNettyEndpoint>()
                            .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState) ||
                                                      endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                                                      endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState));

                        tasks.Add(Parallel.ForEachAsync(endpoints, ReportStateAsync));
                    }

                    await Task.WhenAll(tasks);

                    async ValueTask ReportStateAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken)
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                        {
                            if (command == OpenNettyCommands.Lighting.On)
                            {
                                await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint,
                                    OpenNettyModels.Lighting.SwitchState.On), cancellationToken);

                                // Note: if the endpoint was configured to use the push-button mode, dispatch an OFF state
                                // event immediately after switching it on (or receiving a notification indicating it was
                                // switched on), as Nitoo devices using this mode don't automatically report the OFF state.
                                if (string.Equals(endpoint.GetStringSetting(OpenNettySettings.SwitchMode), "Push button", StringComparison.OrdinalIgnoreCase))
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
                        }
                        
                        // Note: for Nitoo devices supporting dimming, an ON command always changes the brightness to 100%.
                        if (command == OpenNettyCommands.Lighting.On && (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                                                                         endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState)))
                        {
                            await _events.PublishAsync(new BrightnessReportedEventArgs(endpoint, 100), cancellationToken);
                        }
                    }
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand command,
                                         Address : OpenNettyAddress address })
                    // Note: ON/OFF BUS COMMAND frames can be parameterized.
                    when command.Category == OpenNettyCategories.Lighting &&
                        (command.Value    == OpenNettyCommands.Lighting.On.Value ||
                         command.Value    == OpenNettyCommands.Lighting.Off.Value):
                {
                    await Parallel.ForEachAsync(_manager.FindEndpointsByAddressAsync(address), async (endpoint, cancellationToken) =>
                    {
                        // Ignore the message if it was received by a different gateway than the one associated with the endpoint.
                        if (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway)
                        {
                            return;
                        }

                        // SCS devices configured to use the PUL mode never react to area and general commands.
                        if (address.Type is OpenNettyAddressType.ScsLightPointArea or OpenNettyAddressType.ScsLightPointGeneral &&
                            string.Equals(endpoint.GetStringSetting(OpenNettySettings.SwitchMode), "Push button", StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                        {
                            await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint,
                                command.Value == OpenNettyCommands.Lighting.On.Value ?
                                    OpenNettyModels.Lighting.SwitchState.On :
                                    OpenNettyModels.Lighting.SwitchState.Off), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand command,
                                         Address : OpenNettyAddress address })
                    when command == OpenNettyCommands.Lighting.On20 ||
                         command == OpenNettyCommands.Lighting.On30 ||
                         command == OpenNettyCommands.Lighting.On40 ||
                         command == OpenNettyCommands.Lighting.On50 ||
                         command == OpenNettyCommands.Lighting.On60 ||
                         command == OpenNettyCommands.Lighting.On70 ||
                         command == OpenNettyCommands.Lighting.On80 ||
                         command == OpenNettyCommands.Lighting.On90 ||
                         command == OpenNettyCommands.Lighting.On100:
                {
                    await Parallel.ForEachAsync(_manager.FindEndpointsByAddressAsync(address), async (endpoint, cancellationToken) =>
                    {
                        // Ignore the message if it was received by a different gateway than the one associated with the endpoint.
                        if (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway)
                        {
                            return;
                        }

                        // SCS devices configured to use the PUL mode never react to area and general commands.
                        if (address.Type is OpenNettyAddressType.ScsLightPointArea or OpenNettyAddressType.ScsLightPointGeneral &&
                            string.Equals(endpoint.GetStringSetting(OpenNettySettings.SwitchMode), "Push button", StringComparison.OrdinalIgnoreCase))
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
                        // a DIMENSION REQUEST but returns "5" (50%) when using a STATUS REQUEST. To avoid that,
                        // a specialized event handler is responsible for monitoring ON% BUS COMMAND frames and
                        // retrieving the exact brightness level using a "DIMMER LEVEL SPEED" DIMENSION REQUEST.
                        if (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) &&
                           !endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                        {
                            await _events.PublishAsync(new BrightnessReportedEventArgs(endpoint, (ushort)
                                (command == OpenNettyCommands.Lighting.On20 ? 20 :
                                 command == OpenNettyCommands.Lighting.On30 ? 30 :
                                 command == OpenNettyCommands.Lighting.On40 ? 40 :
                                 command == OpenNettyCommands.Lighting.On50 ? 50 :
                                 command == OpenNettyCommands.Lighting.On60 ? 60 :
                                 command == OpenNettyCommands.Lighting.On70 ? 70 :
                                 command == OpenNettyCommands.Lighting.On80 ? 80 :
                                 command == OpenNettyCommands.Lighting.On90 ? 90 : 100)), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : OpenNettyAddress address,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 } value, ..] })
                    when dimension == OpenNettyDimensions.Lighting.DimmerLevelSpeed ||
                         dimension == OpenNettyDimensions.Lighting.DimmerStatus:
                {
                    await Parallel.ForEachAsync(_manager.FindEndpointsByAddressAsync(address), async (endpoint, cancellationToken) =>
                    {
                        // Ignore the message if it was received by a different gateway than the one associated with the endpoint.
                        if (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway)
                        {
                            return;
                        }

                        // SCS devices configured to use the PUL mode never react to area and general commands.
                        if (address.Type is OpenNettyAddressType.ScsLightPointArea or OpenNettyAddressType.ScsLightPointGeneral &&
                            string.Equals(endpoint.GetStringSetting(OpenNettySettings.SwitchMode), "Push button", StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        var level = (ushort) (ushort.Parse(value, CultureInfo.InvariantCulture) - 100);

                        if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                        {
                            await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint, level is not 0 ?
                                OpenNettyModels.Lighting.SwitchState.On :
                                OpenNettyModels.Lighting.SwitchState.Off), cancellationToken);
                        }

                        // Note: the special brightness level "0" always indicates that the output is switched off.
                        // To avoid overriding the last known level (which is typically restored by SCS devices when
                        // receiving an ON command), the brightness level is only reported if it's higher than zero.
                        if (level is not 0 && (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                                               endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState)))
                        {
                            await _events.PublishAsync(new BrightnessReportedEventArgs(endpoint, level), cancellationToken);
                        }
                    });
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : OpenNettyAddress address,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : ["129", { Length: > 0 } value] })
                    when dimension == OpenNettyDimensions.Diagnostics.UnitDescription:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                    {
                        await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint, value is "128" or "129" or "130" ?
                            OpenNettyModels.Lighting.SwitchState.On :
                            OpenNettyModels.Lighting.SwitchState.Off));
                    }
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : OpenNettyAddress address,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : ["143", { Length: > 0 } value, ..] })
                    when dimension == OpenNettyDimensions.Diagnostics.UnitDescription:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    var level = ushort.Parse(value, CultureInfo.InvariantCulture);

                    if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                    {
                        await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint, level is not 0 ?
                            OpenNettyModels.Lighting.SwitchState.On :
                            OpenNettyModels.Lighting.SwitchState.Off));
                    }

                    // Note: the special brightness level "0" always indicates that the output is switched off.
                    // While Nitoo devices normally don't restore the last known brightness level, the brightness
                    // level is only reported if it's higher than zero for consistency with MyHome/SCS devices.
                    if (level is not 0 && (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                                           endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState)))
                    {
                        await _events.PublishAsync(new BrightnessReportedEventArgs(endpoint, level));
                    }
                    break;
                }

                // Note: outgoing dimmer level commands sent using broadcast or multicast transmission
                // generally don't affect the local output of a Nitoo lighting device. As such, the switch
                // state/brightness of the endpoint is only reported if the message was sent in unicast.
                case (OpenNettyNotifications.MessageReceived or OpenNettyNotifications.MessageSent,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionSet,
                                         Address  : OpenNettyAddress address,
                                         Mode     : OpenNettyMode.Unicast,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 } value, ..] })
                    when dimension == OpenNettyDimensions.Lighting.DimmerLevelSpeed:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    var level = (ushort) Math.Round(decimal.Parse(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);

                    if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                    {
                        await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint, level is not 0 ?
                            OpenNettyModels.Lighting.SwitchState.On :
                            OpenNettyModels.Lighting.SwitchState.Off));
                    }

                    // Note: the special brightness level "0" always indicates that the output is switched off.
                    // While Nitoo devices normally don't restore the last known brightness level, the brightness
                    // level is only reported if it's higher than zero for consistency with MyHome/SCS devices.
                    if (level is not 0 && (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                                           endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState)))
                    {
                        await _events.PublishAsync(new BrightnessReportedEventArgs(endpoint, level));
                    }
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Zigbee,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand command,
                                         Address : OpenNettyAddress address })
                    when command == OpenNettyCommands.Lighting.Toggle:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.ToggleScenario))
                    {
                        await _events.PublishAsync(new ToggleScenarioReportedEventArgs(endpoint));
                    }
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : OpenNettyAddress address,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 }, { Length: > 0 }, { Length: > 0 }] values })
                    when dimension == OpenNettyDimensions.TemperatureControl.SmartMeterIndexes:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                    {
                        await _events.PublishAsync(new SmartMeterIndexesReportedEventArgs(endpoint,
                            OpenNettyModels.TemperatureControl.SmartMeterIndexes.CreateFromDimensionValues(values)));
                    }
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : OpenNettyAddress address,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 } value] })
                    when dimension == OpenNettyDimensions.TemperatureControl.SmartMeterRateType:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                    {
                        await _events.PublishAsync(new SmartMeterRateTypeReportedEventArgs(endpoint, value switch
                        {
                            "2" => OpenNettyModels.TemperatureControl.SmartMeterRateType.OffPeak,
                            "3" => OpenNettyModels.TemperatureControl.SmartMeterRateType.Peak,

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                        }));
                    }
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : OpenNettyAddress address,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : ["7", { Length: > 0 } value] })
                    when dimension == OpenNettyDimensions.Diagnostics.UnitDescription:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                    {
                        await _events.PublishAsync(new SmartMeterRateTypeReportedEventArgs(endpoint, value switch
                        {
                            "32" or "33" => OpenNettyModels.TemperatureControl.SmartMeterRateType.OffPeak,
                            "48" or "49" => OpenNettyModels.TemperatureControl.SmartMeterRateType.Peak,

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                        }));

                        await _events.PublishAsync(new SmartMeterPowerCutModeReportedEventArgs(endpoint, value is "33" or "49"));
                    }
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : OpenNettyAddress address,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : ["133", { Length: > 0 } value] })
                    when dimension == OpenNettyDimensions.Diagnostics.UnitDescription:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

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
                                    OpenNettyModels.TemperatureControl.WaterHeaterState.Idle));
                                break;

                            case "32":
                                await _events.PublishAsync(new WaterHeaterSetpointModeReportedEventArgs(endpoint,
                                    OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic));

                                await _events.PublishAsync(new WaterHeaterStateReportedEventArgs(endpoint,
                                    OpenNettyModels.TemperatureControl.WaterHeaterState.Idle));
                                break;

                            case "1" or "33":
                                await _events.PublishAsync(new WaterHeaterSetpointModeReportedEventArgs(endpoint,
                                    OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic));

                                await _events.PublishAsync(new WaterHeaterStateReportedEventArgs(endpoint,
                                    OpenNettyModels.TemperatureControl.WaterHeaterState.Heating));
                                break;

                            case "17":
                                await _events.PublishAsync(new WaterHeaterSetpointModeReportedEventArgs(endpoint,
                                    OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn));

                                await _events.PublishAsync(new WaterHeaterStateReportedEventArgs(endpoint,
                                    OpenNettyModels.TemperatureControl.WaterHeaterState.Heating));
                                break;
                        }
                    }
                    break;
                }

                case (OpenNettyNotifications.MessageSent,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionSet,
                                         Address  : OpenNettyAddress address,
                                         Mode     : OpenNettyMode mode,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 } value, ..] })
                    when dimension == OpenNettyDimensions.TemperatureControl.WaterHeatingMode:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    List<Task> tasks = [];

                    if (endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                    {
                        tasks.Add(ReportSetpointModeAsync(endpoint, CancellationToken.None).AsTask());
                    }

                    if (endpoint is { Unit.Definition.AssociatedUnitId: ushort unit })
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            var endpoint = await _manager.FindEndpointByAddressAsync(OpenNettyAddress.FromNitooAddress(
                                OpenNettyAddress.ToNitooAddress(address).Identifier, unit));

                            if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                            {
                                await ReportSetpointModeAsync(endpoint, CancellationToken.None);
                            }
                        }));
                    }

                    if (mode is OpenNettyMode.Broadcast or OpenNettyMode.Multicast && endpoint is { Unit.Scenarios: [_, ..] scenarios })
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

                    async ValueTask ReportSetpointModeAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken) =>
                        await _events.PublishAsync(new WaterHeaterSetpointModeReportedEventArgs(endpoint, value switch
                        {
                            "0" => OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOff,
                            "1" => OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn,
                            "2" => OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic,

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
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
                                         Address  : OpenNettyAddress address,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : ["6", { Length: > 0 } value] })
                    when dimension == OpenNettyDimensions.Diagnostics.UnitDescription:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                    {
                        var configuration = OpenNettyModels.TemperatureControl.PilotWireConfiguration.CreateFromUnitDescription([value]);
                        if (configuration.IsDerogationActive)
                        {
                            await _events.PublishAsync(new PilotWireDerogationModeReportedEventArgs(endpoint, configuration.Mode, configuration.DerogationDuration));
                        }

                        else
                        {
                            await _events.PublishAsync(new PilotWireSetpointModeReportedEventArgs(endpoint, configuration.Mode));
                            await _events.PublishAsync(new PilotWireDerogationModeReportedEventArgs(endpoint, null, null));
                        }
                    }
                    break;
                }

                case (OpenNettyNotification notification,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.BusCommand,
                                         Command  : OpenNettyCommand command,
                                         Address  : OpenNettyAddress address,
                                         Mode     : OpenNettyMode mode })
                    when command.Category   == OpenNettyCategories.TemperatureControl                           &&
                         command.Value      == OpenNettyCommands.TemperatureControl.WirePilotSetpointMode.Value &&
                         command.Parameters is [{ Length: > 0 } value]:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    List<Task> tasks = [];

                    if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                    {
                        tasks.Add(ReportDerogationAndSetpointModesAsync(endpoint, CancellationToken.None).AsTask());
                    }

                    if (endpoint is { Unit.Definition.AssociatedUnitId: ushort unit })
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            var endpoint = await _manager.FindEndpointByAddressAsync(OpenNettyAddress.FromNitooAddress(
                                OpenNettyAddress.ToNitooAddress(address).Identifier, unit));

                            if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                            {
                                await ReportDerogationAndSetpointModesAsync(endpoint, CancellationToken.None);
                            }
                        }));
                    }

                    if (mode is OpenNettyMode.Broadcast or OpenNettyMode.Multicast && endpoint is { Unit.Scenarios: [_, ..] scenarios })
                    {
                        var endpoints = scenarios.ToAsyncEnumerable()
                            .Where(static scenario => scenario.FunctionCode is 255)
                            .SelectAwait(scenario => _manager.FindEndpointByNameAsync(scenario.EndpointName))
                            .Where(static endpoint => endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit: OpenNettyUnit })
                            .OfType<OpenNettyEndpoint>()
                            .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating));

                        tasks.Add(Parallel.ForEachAsync(endpoints, ReportDerogationAndSetpointModesAsync));
                    }

                    await Task.WhenAll(tasks);

                    async ValueTask ReportDerogationAndSetpointModesAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken)
                    {
                        await _events.PublishAsync(new PilotWireSetpointModeReportedEventArgs(endpoint, value switch
                        {
                            "0" => OpenNettyModels.TemperatureControl.PilotWireMode.Comfort,
                            "1" => OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne,
                            "2" => OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo,
                            "3" => OpenNettyModels.TemperatureControl.PilotWireMode.Eco,
                            "4" => OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection,

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                        }), cancellationToken);

                        // Note: setting the setpoint mode may not have an immediate effect on the device (e.g if a
                        // derogation mode was set with a minimal duration during which setpoint commands are ignored).
                        // As such, the derogation mode cannot be reported here, as it may still be active on the device.
                        if (notification is OpenNettyNotifications.MessageSent)
                        {
                            await _events.PublishAsync(new PilotWireDerogationModeReportedEventArgs(endpoint, null, null), cancellationToken);
                        }
                    }
                    break;
                }

                case (OpenNettyNotifications.MessageReceived or OpenNettyNotifications.MessageSent,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.BusCommand,
                                         Command  : OpenNettyCommand command,
                                         Address  : OpenNettyAddress address,
                                         Mode     : OpenNettyMode mode })
                    when command.Category   == OpenNettyCategories.TemperatureControl                             &&
                         command.Value      == OpenNettyCommands.TemperatureControl.WirePilotDerogationMode.Value &&
                         command.Parameters is [{ Length: > 0 } value]:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    List<Task> tasks = [];

                    if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                    {
                        tasks.Add(ReportDerogationModeAsync(endpoint, CancellationToken.None).AsTask());
                    }

                    if (endpoint is { Unit.Definition.AssociatedUnitId: ushort unit })
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            var endpoint = await _manager.FindEndpointByAddressAsync(OpenNettyAddress.FromNitooAddress(
                                OpenNettyAddress.ToNitooAddress(address).Identifier, unit));

                            if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                            {
                                await ReportDerogationModeAsync(endpoint, CancellationToken.None);
                            }
                        }));
                    }

                    if (mode is OpenNettyMode.Broadcast or OpenNettyMode.Multicast && endpoint is { Unit.Scenarios: [_, ..] scenarios })
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

                    async ValueTask ReportDerogationModeAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken) =>
                        await _events.PublishAsync(new PilotWireDerogationModeReportedEventArgs(endpoint,
                            value switch
                            {
                                "0" or "32" or "128" => OpenNettyModels.TemperatureControl.PilotWireMode.Comfort,
                                "1" or "33" or "129" => OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne,
                                "2" or "34" or "130" => OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo,
                                "3" or "35" or "131" => OpenNettyModels.TemperatureControl.PilotWireMode.Eco,
                                "4" or "36" or "132" => OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection,

                                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                            },
                            ushort.Parse(value, CultureInfo.InvariantCulture) switch
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
                                         Address  : OpenNettyAddress address,
                                         Mode     : OpenNettyMode mode })
                    when command == OpenNettyCommands.TemperatureControl.CancelWirePilotDerogationMode:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    List<Task> tasks = [];

                    if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                    {
                        tasks.Add(_events.PublishAsync(new PilotWireDerogationModeReportedEventArgs(endpoint, null, null)).AsTask());
                    }

                    if (endpoint is { Unit.Definition.AssociatedUnitId: ushort unit })
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            var endpoint = await _manager.FindEndpointByAddressAsync(OpenNettyAddress.FromNitooAddress(
                                OpenNettyAddress.ToNitooAddress(address).Identifier, unit));

                            if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                            {
                                await _events.PublishAsync(new PilotWireDerogationModeReportedEventArgs(endpoint, null, null));
                            }
                        }));
                    }

                    if (mode is OpenNettyMode.Broadcast or OpenNettyMode.Multicast && endpoint is { Unit.Scenarios: [_, ..] scenarios })
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
                    break;
                }

                // Note: the battery level is received when pushing the NETWORK or LEARN buttons on a Zigbee device.
                // To receive the battery level during normal operation, the LEARN button on the wireless device
                // must be pressed and a CEN+ binding request must be sent by the gateway to bind the two devices.
                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Zigbee,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : OpenNettyAddress address,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 } value] })
                    when dimension == OpenNettyDimensions.Management.BatteryInformation:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    List<Task> tasks = [];

                    if (endpoint.HasCapability(OpenNettyCapabilities.Battery))
                    {
                        tasks.Add(ReportBatteryLevelAsync(endpoint, CancellationToken.None).AsTask());
                    }

                    tasks.Add(Task.Run(async () =>
                    {
                        // Note: while the battery level is reported using a unit-specific address, it applies to the entire
                        // device: this task retrieves the device endpoint and, if available, report its battery level.
                        var endpoint = await _manager.FindEndpointByAddressAsync(OpenNettyAddress.FromDecimalZigbeeAddress(
                            OpenNettyAddress.ToZigbeeAddress(address).Identifier));

                        if (endpoint is not null && endpoint.HasCapability(OpenNettyCapabilities.Battery))
                        {
                            await ReportBatteryLevelAsync(endpoint, CancellationToken.None);
                        }
                    }));

                    async ValueTask ReportBatteryLevelAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken)
                        => await _events.PublishAsync(new BatteryLevelReportedEventArgs(endpoint, value switch
                        {
                            "0" => 0,
                            "1" => 33,
                            "2" => 66,
                            "3" => 100,

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                        }), cancellationToken);
                    break;
                }

                // Note: Nitoo radio devices don't directly expose the battery level but send a special frame
                // header converted to an OpenWebNet "BATTERY WEAK" BUS COMMAND when the battery is low.
                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Nitoo,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand command,
                                         Address : OpenNettyAddress address })
                    when command == OpenNettyCommands.Management.BatteryWeak:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.Battery))
                    {
                        await _events.PublishAsync(new BatteryLevelReportedEventArgs(endpoint, (ushort) 5));
                    }
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Zigbee,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand command,
                                         Address : OpenNettyAddress address })
                    when command == OpenNettyCommands.Scenario.OpenBinding ||
                         command == OpenNettyCommands.Scenario.CloseBinding:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.ZigbeeBinding))
                    {
                        if (command == OpenNettyCommands.Scenario.OpenBinding)
                        {
                            await _events.PublishAsync(new BindingOpenEventArgs(endpoint));
                        }

                        else
                        {
                            await _events.PublishAsync(new BindingClosedEventArgs(endpoint));
                        }
                    }
                    break;
                }

                case (OpenNettyNotifications.MessageReceived,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionRead,
                                         Address  : OpenNettyAddress address,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 }, { Length: > 0 }, { Length: > 0 }, { Length: > 0 }] values })
                    when dimension == OpenNettyDimensions.Diagnostics.DeviceDescription:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    await _events.PublishAsync(new DeviceDescriptionReportedEventArgs(endpoint,
                        OpenNettyModels.Diagnostics.DeviceDescription.CreateFromDeviceDescription(values)));
                    break;
                }

                case (OpenNettyNotifications.MessageReceived or OpenNettyNotifications.MessageSent,
                      OpenNettyMessage { Protocol : OpenNettyProtocol.Nitoo,
                                         Type     : OpenNettyMessageType.DimensionSet,
                                         Address  : OpenNettyAddress address,
                                         Mode     : OpenNettyMode.Broadcast,
                                         Dimension: OpenNettyDimension dimension,
                                         Values   : [{ Length: > 0 } value, ..] })
                    when dimension == OpenNettyDimensions.Lighting.DimmerStep:
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.DimmingScenario))
                    {
                        var step = int.Parse(value, CultureInfo.InvariantCulture);
                        if (step is >= 128)
                        {
                            step -= 256;
                        }

                        await _events.PublishAsync(new DimmingStepReportedEventArgs(endpoint, step));
                    }
                    break;
                }

                case (OpenNettyNotification notification,
                      OpenNettyMessage { Protocol: OpenNettyProtocol.Nitoo,
                                         Type    : OpenNettyMessageType.BusCommand,
                                         Command : OpenNettyCommand command,
                                         Address : OpenNettyAddress address,
                                         Mode    : OpenNettyMode mode })
                    // Note: timed and progressive scenarios are parameterized.
                    when command == OpenNettyCommands.Scenario.Action ||
                        (command.Category   == OpenNettyCategories.Scenarios                  &&
                         command.Value      == OpenNettyCommands.Scenario.ActionForTime.Value &&
                         command.Parameters is [{ Length: > 0 }]) ||
                        (command.Category   == OpenNettyCategories.Scenarios                 &&
                         command.Value      == OpenNettyCommands.Scenario.ActionInTime.Value &&
                         command.Parameters is [{ Length: > 0 }]):
                {
                    // Ignore the message if the corresponding endpoint couldn't be resolved or if it
                    // was received by a different gateway than the one associated with the endpoint.
                    var endpoint = await _manager.FindEndpointByAddressAsync(address);
                    if (endpoint is null || (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway))
                    {
                        return;
                    }

                    List<Task> tasks = [];

                    if (command == OpenNettyCommands.Scenario.Action)
                    {
                        if (!endpoint.HasCapability(OpenNettyCapabilities.BasicScenario))
                        {
                            break;
                        }

                        if (mode is OpenNettyMode.Broadcast && notification is OpenNettyNotifications.MessageReceived)
                        {
                            tasks.Add(_events.PublishAsync(new BasicScenarioReportedEventArgs(endpoint)).AsTask());
                        }

                        // Note: since they are radiofrequency devices, the current state of Nitoo wireless burglar alarms
                        // cannot be retrieved using a unit description request. To inform devices associated using a
                        // PnL scenario of a state change, Nitoo alarms broadcast it using unit-specific ACTION scenarios.
                        if (endpoint.HasCapability(OpenNettyCapabilities.WirelessBurglarAlarmScenario))
                        {
                            tasks.Add(Task.Run(async () =>
                            {
                                var (identifier, unit) = OpenNettyAddress.ToNitooAddress(address);

                                var endpoint = await _manager.FindEndpointByAddressAsync(OpenNettyAddress.FromNitooAddress(identifier));
                                if (endpoint is null || !endpoint.HasCapability(OpenNettyCapabilities.WirelessBurglarAlarmState))
                                {
                                    return;
                                }

                                var state = unit switch
                                {
                                    4 => OpenNettyModels.Alarm.WirelessBurglarAlarmState.Armed,
                                    5 => OpenNettyModels.Alarm.WirelessBurglarAlarmState.Disarmed,
                                    6 => OpenNettyModels.Alarm.WirelessBurglarAlarmState.PartiallyArmed,
                                    7 => OpenNettyModels.Alarm.WirelessBurglarAlarmState.Triggered,
                                    8 => OpenNettyModels.Alarm.WirelessBurglarAlarmState.ExitDelayElapsed,
                                    9 => OpenNettyModels.Alarm.WirelessBurglarAlarmState.EventDetected,
                                    _ => null as OpenNettyModels.Alarm.WirelessBurglarAlarmState?
                                };

                                if (state is not null)
                                {
                                    await _events.PublishAsync(new WirelessBurglarAlarmStateReportedEventArgs(endpoint, state.Value));
                                }
                            }));
                        }
                    }

                    else if (command.Value == OpenNettyCommands.Scenario.ActionForTime.Value)
                    {
                        if (!endpoint.HasCapability(OpenNettyCapabilities.TimedScenario))
                        {
                            break;
                        }

                        if (notification is OpenNettyNotifications.MessageReceived && mode is OpenNettyMode.Broadcast)
                        {
                            var duration = Math.Round(double.Parse(command.Parameters[0], CultureInfo.InvariantCulture) / 5, MidpointRounding.AwayFromZero);

                            await _events.PublishAsync(new TimedScenarioReportedEventArgs(endpoint, TimeSpan.FromSeconds(duration)));
                        }
                    }

                    else if (command.Value == OpenNettyCommands.Scenario.ActionInTime.Value)
                    {
                        if (!endpoint.HasCapability(OpenNettyCapabilities.ProgressiveScenario))
                        {
                            break;
                        }

                        if (notification is OpenNettyNotifications.MessageReceived && mode is OpenNettyMode.Broadcast)
                        {
                            var duration = Math.Round(double.Parse(command.Parameters[0], CultureInfo.InvariantCulture) / 5, MidpointRounding.AwayFromZero);

                            await _events.PublishAsync(new ProgressiveScenarioReportedEventArgs(endpoint, TimeSpan.FromSeconds(duration)));
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
                        tasks.Add(ReportOnStateAsync(endpoint, CancellationToken.None).AsTask());
                    }

                    if (endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Definition.AssociatedUnitId: ushort unit })
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            var endpoint = await _manager.FindEndpointByAddressAsync(OpenNettyAddress.FromNitooAddress(
                                OpenNettyAddress.ToNitooAddress(address).Identifier, unit));

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
                                await ReportOnStateAsync(endpoint, CancellationToken.None);
                            }
                        }));
                    }

                    if (endpoint is { Unit.Scenarios: [_, ..] scenarios })
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
                                // While Nitoo devices normally don't restore the last known brightness level, the brightness
                                // level is only reported if it's higher than zero for consistency with MyHome/SCS devices.
                                else if (scenario.FunctionCode is >= 1 and <= 100)
                                {
                                    await _events.PublishAsync(new BrightnessReportedEventArgs(endpoint, scenario.FunctionCode), cancellationToken);
                                }
                            }
                        }));
                    }

                    await Task.WhenAll(tasks);

                    async ValueTask ReportOnStateAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken)
                    {
                        await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint,
                            OpenNettyModels.Lighting.SwitchState.On), cancellationToken);

                        // Note: if the endpoint was configured to use the push-button mode,
                        // dispatch an "OFF state" event immediately after switching it on.
                        if (string.Equals(endpoint.GetStringSetting(OpenNettySettings.SwitchMode), "Push button", StringComparison.OrdinalIgnoreCase))
                        {
                            await _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint,
                                OpenNettyModels.Lighting.SwitchState.Off), cancellationToken);
                        }
                    }
                    break;
                }
            }
        })
        .Do(_logger.UnhandledEventHandlerException)
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
            // when using a STATUS REQUEST. To avoid that, this event handler monitors all the ON% BUS COMMAND
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

            // Note: some Nitoo devices (like the 067210, 067212 and 067214 dimmers) offer preset buttons that allow
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
                when command == OpenNettyCommands.Scenario.Action
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
                when command.Category   == OpenNettyCategories.Scenarios                  &&
                     command.Value      == OpenNettyCommands.Scenario.ActionForTime.Value &&
                     command.Parameters is [{ Length: > 0 }]
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
                when command.Category   == OpenNettyCategories.Scenarios                 &&
                     command.Value      == OpenNettyCommands.Scenario.ActionInTime.Value &&
                     command.Parameters is [{ Length: > 0 }]
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
        .SelectMany(static group => group.Throttle(group.Key.Type switch
        {
            OpenNettyAddressType.NitooUnit     => TimeSpan.FromSeconds(0.5),
            not OpenNettyAddressType.NitooUnit => TimeSpan.FromSeconds(1)
        }))
        .Do(async arguments =>
        {
            await Parallel.ForEachAsync(_manager.FindEndpointsByAddressAsync(arguments.Message.Address!.Value), async (endpoint, cancellationToken) =>
            {
                // Ignore the message if it was received by a different gateway than the one associated with the endpoint.
                if (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway)
                {
                    return;
                }

                switch (arguments.Message)
                {
                    case { Type: OpenNettyMessageType.BusCommand, Command: OpenNettyCommand command }
                        when command == OpenNettyCommands.Scenario.Action && !endpoint.HasCapability(OpenNettyCapabilities.BasicScenario):
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
                        when command.Category == OpenNettyCategories.Scenarios                  &&
                             command.Value    == OpenNettyCommands.Scenario.ActionForTime.Value &&
                            !endpoint.HasCapability(OpenNettyCapabilities.TimedScenario):
                        return;

                    case { Type: OpenNettyMessageType.BusCommand, Command: OpenNettyCommand command }
                        when command.Category == OpenNettyCategories.Scenarios                 &&
                             command.Value    == OpenNettyCommands.Scenario.ActionInTime.Value &&
                            !endpoint.HasCapability(OpenNettyCapabilities.ProgressiveScenario):
                        return;

                    case { Type: OpenNettyMessageType.DimensionSet, Dimension: OpenNettyDimension dimension }
                        when dimension == OpenNettyDimensions.Lighting.DimmerStep &&
                            !endpoint.HasCapability(OpenNettyCapabilities.DimmingScenario):
                        return;
                }

                List<Task> tasks = [];

                if (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                    endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                {
                    tasks.Add(_controller.GetBrightnessAsync(endpoint, cancellationToken).AsTask());
                }

                if (endpoint is { Protocol: OpenNettyProtocol.Nitoo, Unit.Definition.AssociatedUnitId: ushort unit })
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var endpoint = await _manager.FindEndpointByAddressAsync(OpenNettyAddress.FromNitooAddress(
                            OpenNettyAddress.ToNitooAddress(arguments.Message.Address!.Value).Identifier, unit));

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

                if (arguments.Message.Protocol  is OpenNettyProtocol.Nitoo                 &&
                    arguments.Message.Type      is OpenNettyMessageType.DimensionSet       &&
                    arguments.Message.Dimension == OpenNettyDimensions.Lighting.DimmerStep &&
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
        .Do(_logger.UnhandledEventHandlerException)
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
                when command.Category   == OpenNettyCategories.Scenarios                  &&
                     command.Value      == OpenNettyCommands.Scenario.ActionForTime.Value &&
                     command.Parameters is [{ Length: > 0 } value]                        &&
                     Math.Round(double.Parse(value, CultureInfo.InvariantCulture) / 5, MidpointRounding.AwayFromZero) is double duration
                     => AsyncObservable.Timer(TimeSpan.FromSeconds(duration)).Select(_ => (Notification: notification, Message: message)),

            _ => AsyncObservable.Empty<(OpenNettyNotification Notification, OpenNettyMessage Message)>()
        })
        .Do(async arguments =>
        {
            // Ignore the message if no endpoint matching the associated address could be resolved.
            var endpoint = await _manager.FindEndpointByAddressAsync(arguments.Message.Address!.Value);
            if (endpoint is null)
            {
                return;
            }

            // Ignore the message if it was received by a different gateway than the one associated with the endpoint.
            if (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway)
            {
                return;
            }

            if (!endpoint.HasCapability(OpenNettyCapabilities.TimedScenario))
            {
                return;
            }

            List<Task> tasks = [];

            if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
            {
                tasks.Add(ReportOffStateAsync(endpoint, CancellationToken.None).AsTask());
            }

            if (endpoint is { Unit.Definition.AssociatedUnitId: ushort unit })
            {
                tasks.Add(Task.Run(async () =>
                {
                    var endpoint = await _manager.FindEndpointByAddressAsync(OpenNettyAddress.FromNitooAddress(
                        OpenNettyAddress.ToNitooAddress(arguments.Message.Address!.Value).Identifier, unit));

                    if (endpoint is null)
                    {
                        return;
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                    {
                        await ReportOffStateAsync(endpoint, CancellationToken.None);
                    }
                }));
            }

            if (endpoint is { Unit.Scenarios: [_, ..] scenarios })
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

            ValueTask ReportOffStateAsync(OpenNettyEndpoint endpoint, CancellationToken cancellationToken)
                => _events.PublishAsync(new SwitchStateReportedEventArgs(endpoint, OpenNettyModels.Lighting.SwitchState.Off), cancellationToken);
        })
        .Do(_logger.UnhandledEventHandlerException)
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
                when command.Category   == OpenNettyCategories.TemperatureControl                           &&
                     command.Value      == OpenNettyCommands.TemperatureControl.WirePilotSetpointMode.Value &&
                     command.Parameters is [{ Length: > 0 }]
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
                when command.Category   == OpenNettyCategories.TemperatureControl                           &&
                     command.Value      == OpenNettyCommands.TemperatureControl.WirePilotSetpointMode.Value &&
                     command.Parameters is [{ Length: > 0 }]
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
        .Do(async arguments =>
        {
            // Ignore the message if no endpoint matching the associated address could be resolved.
            var endpoint = await _manager.FindEndpointByAddressAsync(arguments.Message.Address!.Value);
            if (endpoint is null)
            {
                return;
            }

            // Ignore the message if it was received by a different gateway than the one associated with the endpoint.
            if (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway)
            {
                return;
            }

            if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
            {
                _ = await _controller.GetPilotWireConfigurationAsync(endpoint);
            }
        })
        .Do(_logger.UnhandledEventHandlerException)
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
        .Do(async arguments =>
        {
            // Ignore the message if no endpoint matching the associated address could be resolved.
            var endpoint = await _manager.FindEndpointByAddressAsync(arguments.Message.Address!.Value);
            if (endpoint is null)
            {
                return;
            }

            // Ignore the message if it was received by a different gateway than the one associated with the endpoint.
            if (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway)
            {
                return;
            }

            if (!endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
            {
                return;
            }

            var endpoints = _manager.EnumerateEndpointsAsync()
                .Where(static endpoint => endpoint.Protocol is OpenNettyProtocol.Nitoo)
                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                .Where(endpoint => endpoint.Address is OpenNettyAddress address &&
                    OpenNettyAddress.ToNitooAddress(address) is { Identifier: uint identifier } &&
                    identifier == OpenNettyAddress.ToNitooAddress(arguments.Message.Address!.Value).Identifier);

            await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
            {
                switch (arguments.Message.Values, await _controller.GetUnitDescriptionAsync(endpoint, cancellationToken))
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
        })
        .Do(_logger.UnhandledEventHandlerException)
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
        .Do(async arguments =>
        {
            // Ignore the message if no endpoint matching the associated address could be resolved.
            var endpoint = await _manager.FindEndpointByAddressAsync(arguments.Message.Address!.Value);
            if (endpoint is null)
            {
                return;
            }

            // Ignore the message if it was received by a different gateway than the one associated with the endpoint.
            if (endpoint.Gateway is not null && arguments.Notification.Gateway != endpoint.Gateway)
            {
                return;
            }

            if (endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
            {
                _ = await _controller.GetWaterHeaterStateAsync(endpoint);
            }
        })
        .Do(_logger.UnhandledEventHandlerException)
        .Retry()
        .SubscribeAsync(static notification => ValueTask.CompletedTask)
    ]);
}
