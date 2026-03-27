/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Buffers.Text;
using System.Collections.Immutable;
using System.Globalization;
using System.IO.Hashing;
using System.Net.Mime;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;

namespace OpenNetty.Mqtt;

/// <summary>
/// Represents a worker responsible for processing incoming MQTT application messages.
/// </summary>
public sealed class OpenNettyMqttWorker : IOpenNettyMqttWorker
{
    private readonly OpenNettyController _controller;
    private readonly ILogger<OpenNettyMqttWorker> _logger;
    private readonly OpenNettyManager _manager;
    private readonly IOptionsMonitor<OpenNettyMqttOptions> _options;
    private readonly IOptionsMonitor<OpenNettyOptions> _openNettyOptions;
    private readonly IOpenNettyPipeline _pipeline;
    private readonly IOpenNettyService _service;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyMqttWorker"/> class.
    /// </summary>
    /// <param name="controller">The OpenNetty controller.</param>
    /// <param name="logger">The OpenNetty logger.</param>
    /// <param name="manager">The OpenNetty manager.</param>
    /// <param name="options">The OpenNetty MQTT options.</param>
    /// <param name="openNettyOptions">The OpenNetty options.</param>
    /// <param name="pipeline">The OpenNetty notification pipeline.</param>
    /// <param name="service">The OpenNetty service.</param>
    public OpenNettyMqttWorker(
        OpenNettyController controller,
        ILogger<OpenNettyMqttWorker> logger,
        OpenNettyManager manager,
        IOptionsMonitor<OpenNettyMqttOptions> options,
        IOptionsMonitor<OpenNettyOptions> openNettyOptions,
        IOpenNettyPipeline pipeline,
        IOpenNettyService service)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _openNettyOptions = openNettyOptions ?? throw new ArgumentNullException(nameof(openNettyOptions));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    /// <inheritdoc/>
    public async Task ProcessMessagesAsync(
        IManagedMqttClient client,
        ChannelReader<MqttApplicationMessage> reader,
        CancellationToken cancellationToken)
    {
        await using var subscription = await ObserveMessagesAsync(reader, cancellationToken)
            .Select(async message => ExtractParameters(message))
            .Where(static parameters => !string.IsNullOrEmpty(parameters.Name) &&
                !string.IsNullOrEmpty(parameters.Attribute) &&
                parameters.Operation is OpenNettyMqttOperation.Get or OpenNettyMqttOperation.Set)
            .GroupBy(static parameters => parameters.Name)
            .Do(async group => await group.ObserveOn(TaskPoolAsyncScheduler.Default).Do(async parameters =>
            {
                // Handle the system-level discovery scan topic
                // (e.g. opennetty/system/discovery_scan/set with payload "SCAN").
                if (string.Equals(parameters.Name, "system", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(parameters.Attribute, OpenNettyMqttAttributes.DiscoveryScan, StringComparison.OrdinalIgnoreCase) &&
                    parameters.Operation is OpenNettyMqttOperation.Set &&
                    string.Equals(parameters.Message.ConvertPayloadToString(), "SCAN", StringComparison.OrdinalIgnoreCase))
                {
                    await PerformDiscoveryScanAsync(client, cancellationToken);
                    return;
                }

                var endpoints = from endpoint in _manager.EnumerateEndpointsAsync(cancellationToken)
                                let topic = endpoint.GetStringSetting(OpenNettySettings.MqttTopic) ?? endpoint.Name.ToLowerInvariant()
                                where string.Equals(topic, parameters.Name, StringComparison.Ordinal)
                                select endpoint;

                await Parallel.ForEachAsync(endpoints, async (endpoint, cancellationToken) =>
                {
                    try
                    {
                        await ExecuteAsync(parameters.Message, endpoint, parameters.Attribute!,
                            parameters.Operation!.Value, cancellationToken);

                        if (!string.IsNullOrEmpty(parameters.Message.ResponseTopic))
                        {
                            await client.EnqueueAsync(new MqttApplicationMessageBuilder()
                                .WithCorrelationData(parameters.Message.CorrelationData)
                                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                                .WithTopic(parameters.Message.ResponseTopic)
                                .Build());
                        }
                    }

                    catch (OpenNettyException exception) when (!string.IsNullOrEmpty(parameters.Message.ResponseTopic))
                    {
                        await client.EnqueueAsync(new MqttApplicationMessageBuilder()
                            .WithContentType(MediaTypeNames.Application.Json)
                            .WithCorrelationData(parameters.Message.CorrelationData)
                            .WithPayload(new JsonObject { ["error"] = exception.Message }.ToJsonString())
                            .WithPayloadFormatIndicator(MqttPayloadFormatIndicator.CharacterData)
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                            .WithTopic(parameters.Message.ResponseTopic)
                            .Build());

                        throw;
                    }
                });
            })
            .Do((Exception exception) => _logger.LogWarning(6018, exception, SR.GetResourceString(SR.ID6018)))
            .Retry()
            .SubscribeAsync(static arguments => ValueTask.CompletedTask))
        .Retry()
        .SubscribeAsync(static arguments => ValueTask.CompletedTask);

        // Wait until the host signals the application is shutting down and then, for each endpoint, publish
        // an "offline" message to the corresponding availability topic before the MQTT client disconnects.
        try
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(static state => ((TaskCompletionSource) state!).SetResult(), source);
            await source.Task;
        }

        finally
        {
            // Note: the cancellation token provided as a parameter MUST NOT be used here as it is already in a
            // canceled state and would cause the MQTT client to skip the publication of the availability messages.
            await Parallel.ForEachAsync(_manager.EnumerateEndpointsAsync(CancellationToken.None), async (endpoint, cancellationToken) =>
            {
                var topic = endpoint.GetStringSetting(OpenNettySettings.MqttTopic) ?? endpoint.Name.ToLowerInvariant();

                await client.EnqueueAsync(new MqttApplicationMessageBuilder()
                    .WithPayload("offline")
                    .WithPayloadFormatIndicator(MqttPayloadFormatIndicator.CharacterData)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                    .WithTopic($"{_options.CurrentValue.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}")
                    .Build());
            });

            while (client.PendingApplicationMessagesCount is not 0)
            {
                await Task.Delay(100, CancellationToken.None);
            }
        }

        static IAsyncObservable<MqttApplicationMessage> ObserveMessagesAsync(
            ChannelReader<MqttApplicationMessage> reader, CancellationToken cancellationToken)
            => AsyncObservable.Create<MqttApplicationMessage>(observer =>
                TaskPoolAsyncScheduler.Default.ScheduleAsync(async cancellationToken =>
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            if (!await reader.WaitToReadAsync(cancellationToken))
                            {
                                await observer.OnCompletedAsync();
                                return;
                            }

                            while (reader.TryRead(out MqttApplicationMessage? message))
                            {
                                await observer.OnNextAsync(message);
                            }
                        }

                        catch (ChannelClosedException)
                        {
                            await observer.OnCompletedAsync();
                            return;
                        }

                        catch (Exception exception)
                        {
                            await observer.OnErrorAsync(exception);
                        }
                    }
                }));

        async ValueTask ExecuteAsync(MqttApplicationMessage message, OpenNettyEndpoint endpoint,
            string attribute, OpenNettyMqttOperation operation, CancellationToken cancellationToken)
        {
            switch (attribute)
            {
                case OpenNettyMqttAttributes.BatteryAlert when operation is OpenNettyMqttOperation.Set:
                {
                    switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                    {
                        case "off":
                            await client.EnqueueAsync(new MqttApplicationMessageBuilder()
                                .WithPayloadFormatIndicator(MqttPayloadFormatIndicator.CharacterData)
                                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                                .WithTopic(message.Topic[..^4])
                                .WithPayload("OFF")
                                .WithRetainFlag()
                                .Build());
                            break;
                    }
                    break;
                }

                case OpenNettyMqttAttributes.Brightness when operation is OpenNettyMqttOperation.Get:
                {
                    _ = await _controller.EnumerateBrightnessAsync(endpoint, cancellationToken).ToListAsync(cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.Brightness when operation is OpenNettyMqttOperation.Set:
                {
                    if (!byte.TryParse(message.PayloadSegment, CultureInfo.InvariantCulture, out var level))
                    {
                        throw new InvalidDataException(SR.GetResourceString(SR.ID0068));
                    }

                    await _controller.SetBrightnessAsync(endpoint, level, null, cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.FirmwareVersion when operation is OpenNettyMqttOperation.Get:
                {
                    _ = await _controller.GetFirmwareVersionAsync(endpoint, cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.HardwareVersion when operation is OpenNettyMqttOperation.Get:
                {
                    _ = await _controller.GetHardwareVersionAsync(endpoint, cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.MacAddress when operation is OpenNettyMqttOperation.Get:
                {
                    _ = await _controller.GetMacAddressAsync(endpoint, cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.OutgoingMessage when operation is OpenNettyMqttOperation.Set:
                {
                    var parameters = TryParseAsJsonObject(message.ConvertPayloadToString())
                        ?? throw new InvalidDataException(SR.GetResourceString(SR.ID0068));

                    await _controller.SendRawMessageAsync(endpoint, parameters["message"]?["raw"]?.GetValue<string>() switch
                    {
                        { Length: > 0 } frame => OpenNettyMessage.CreateFromFrame(endpoint.Protocol, frame),

                        _ => OpenNettyMessage.CreateFromJsonObject(parameters["message"]?["parsed"]?.AsObject()
                            ?? throw new InvalidDataException(SR.GetResourceString(SR.ID0068)))
                    }, cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.PilotWireDerogationMode when operation is OpenNettyMqttOperation.Get:
                case OpenNettyMqttAttributes.PilotWireSetpointMode when operation is OpenNettyMqttOperation.Get:
                case OpenNettyMqttAttributes.PilotWireShutdownMode when operation is OpenNettyMqttOperation.Get:
                {
                    _ = await _controller.GetPilotWireConfigurationAsync(endpoint, cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.PilotWireDerogationMode when operation is OpenNettyMqttOperation.Set:
                {
                    switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                    {
                        case "none":
                            await _controller.CancelPilotWireDerogationModeAsync(endpoint, null, cancellationToken);
                            break;

                        case "comfort":
                            await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.Comfort,
                                OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, null, cancellationToken);
                            break;

                        case "comfort:4h":
                            await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.Comfort,
                                OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, null, cancellationToken);
                            break;

                        case "comfort:8h":
                            await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.Comfort,
                                OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, null, cancellationToken);
                            break;

                        case "comfort-1":
                            await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne,
                                OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, null, cancellationToken);
                            break;

                        case "comfort-1:4h":
                            await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne,
                                OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, null, cancellationToken);
                            break;

                        case "comfort-1:8h":
                            await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne,
                                OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, null, cancellationToken);
                            break;

                        case "comfort-2":
                            await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo,
                                OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, null, cancellationToken);
                            break;

                        case "comfort-2:4h":
                            await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo,
                                OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, null, cancellationToken);
                            break;

                        case "comfort-2:8h":
                            await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo,
                                OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, null, cancellationToken);
                            break;

                        case "eco":
                            await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.Eco,
                                OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, null, cancellationToken);
                            break;

                        case "eco:4h":
                            await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.Eco,
                                OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, null, cancellationToken);
                            break;

                        case "eco:8h":
                            await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.Eco,
                                OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, null, cancellationToken);
                            break;

                        case "frost_protection":
                            await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection,
                                OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None, null, cancellationToken);
                            break;

                        case "frost_protection:4h":
                            await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection,
                                OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours, null, cancellationToken);
                            break;

                        case "frost_protection:8h":
                            await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection,
                                OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours, null, cancellationToken);
                            break;
                    }
                    break;
                }

                case OpenNettyMqttAttributes.PilotWireSetpointMode when operation is OpenNettyMqttOperation.Set:
                {
                    switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                    {
                        case "comfort":
                            await _controller.SetPilotWireSetpointModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.Comfort, null, cancellationToken);
                            break;

                        case "comfort-1":
                            await _controller.SetPilotWireSetpointModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne, null, cancellationToken);
                            break;

                        case "comfort-2":
                            await _controller.SetPilotWireSetpointModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo, null, cancellationToken);
                            break;

                        case "eco":
                            await _controller.SetPilotWireSetpointModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.Eco, null, cancellationToken);
                            break;

                        case "frost_protection":
                            await _controller.SetPilotWireSetpointModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection, null, cancellationToken);
                            break;
                    }
                    break;
                }

                case OpenNettyMqttAttributes.PilotWireShutdownMode when operation is OpenNettyMqttOperation.Set:
                {
                    switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                    {
                        case "on":
                            await _controller.ActivatePilotWireShutdownModeAsync(endpoint, null, cancellationToken);
                            break;

                        case "off":
                            await _controller.CancelPilotWireShutdownModeAsync(endpoint, null, cancellationToken);
                            break;
                    }
                    break;
                }

                case OpenNettyMqttAttributes.Scenario when operation is OpenNettyMqttOperation.Set:
                {
                    var parameters = TryParseAsJsonObject(message.ConvertPayloadToString());

                    switch ((string?) parameters?["event_type"] ?? message.ConvertPayloadToString()?.ToLowerInvariant())
                    {
                        case "action":
                            await _controller.DispatchActionScenarioAsync(endpoint,
                                OpenNettyModels.ScenariosPlus.ActionScenarioType.Action, cancellationToken);
                            break;

                        case "dimming":
                        {
                            await _controller.DispatchDimmingScenarioAsync(endpoint,
                                (short?) parameters?["dimming_step"] ?? throw new InvalidDataException(SR.GetResourceString(SR.ID0068)), cancellationToken);
                            break;
                        }

                        case "end_of_extended_pressure":
                            await _controller.DispatchPressureScenarioPlusAsync(endpoint,
                                OpenNettyModels.ScenariosPlus.PressureScenarioType.EndOfExtendedPressure,
                                (byte?) parameters?["button"], cancellationToken);
                            break;

                        case "extended_pressure" when ((string?) parameters?["scenario_type"]) is "evolved":
                            await _controller.DispatchPressureScenarioAsync(endpoint,
                                OpenNettyModels.Scenarios.PressureScenarioType.ExtendedPressure,
                                (byte?) parameters?["button"] ?? throw new InvalidDataException(SR.GetResourceString(SR.ID0068)), cancellationToken);
                            break;

                        case "extended_pressure" when ((string?) parameters?["scenario_type"]) is "plus":
                            await _controller.DispatchPressureScenarioPlusAsync(endpoint,
                                OpenNettyModels.ScenariosPlus.PressureScenarioType.ExtendedPressure,
                                (byte?) parameters?["button"], cancellationToken);
                            break;

                        case "pressure":
                            await _controller.DispatchPressureScenarioAsync(endpoint,
                                OpenNettyModels.Scenarios.PressureScenarioType.Pressure,
                                (byte?) parameters?["button"] ?? throw new InvalidDataException(SR.GetResourceString(SR.ID0068)), cancellationToken);
                            break;

                        case "progressive_action":
                        {
                            if (!TimeSpan.TryParse((string?) parameters?["duration"], CultureInfo.InvariantCulture, out var duration))
                            {
                                throw new InvalidDataException(SR.GetResourceString(SR.ID0068));
                            }

                            await _controller.DispatchProgressiveScenarioAsync(endpoint, duration, cancellationToken);
                            break;
                        }

                        case "release_after_short_pressure":
                            await _controller.DispatchPressureScenarioAsync(endpoint,
                                OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterShortPressure,
                                (byte?) parameters?["button"] ?? throw new InvalidDataException(SR.GetResourceString(SR.ID0068)), cancellationToken);
                            break;

                        case "release_after_extended_pressure":
                            await _controller.DispatchPressureScenarioAsync(endpoint,
                                OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterExtendedPressure,
                                (byte?) parameters?["button"] ?? throw new InvalidDataException(SR.GetResourceString(SR.ID0068)), cancellationToken);
                            break;

                        case "short_pressure":
                            await _controller.DispatchPressureScenarioPlusAsync(endpoint,
                                OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure,
                                (byte?) parameters?["button"] ?? throw new InvalidDataException(SR.GetResourceString(SR.ID0068)), cancellationToken);
                            break;

                        case "shutter_down":
                            await _controller.DispatchStopUpDownScenarioAsync(endpoint,
                                OpenNettyModels.Automation.StopUpDownScenarioType.Down, cancellationToken);
                            break;

                        case "shutter_stop":
                            await _controller.DispatchStopUpDownScenarioAsync(endpoint,
                                OpenNettyModels.Automation.StopUpDownScenarioType.Stop, cancellationToken);
                            break;

                        case "shutter_up":
                            await _controller.DispatchStopUpDownScenarioAsync(endpoint,
                                OpenNettyModels.Automation.StopUpDownScenarioType.Up, cancellationToken);
                            break;

                        case "start_of_extended_pressure":
                            await _controller.DispatchPressureScenarioPlusAsync(endpoint,
                                OpenNettyModels.ScenariosPlus.PressureScenarioType.StartOfExtendedPressure,
                                (byte?) parameters?["button"], cancellationToken);
                            break;

                        case "stop_action":
                            await _controller.DispatchActionScenarioAsync(endpoint,
                                OpenNettyModels.ScenariosPlus.ActionScenarioType.StopAction, cancellationToken);
                            break;

                        case "switch_on":
                            await _controller.DispatchOnOffScenarioAsync(endpoint,
                                OpenNettyModels.Lighting.OnOffScenarioType.On, cancellationToken);
                            break;

                        case "switch_off":
                            await _controller.DispatchOnOffScenarioAsync(endpoint,
                                OpenNettyModels.Lighting.OnOffScenarioType.Off, cancellationToken);
                            break;

                        case "timed_action":
                        {
                            if (!TimeSpan.TryParse((string?) parameters?["duration"], CultureInfo.InvariantCulture, out var duration))
                            {
                                throw new InvalidDataException(SR.GetResourceString(SR.ID0068));
                            }

                            await _controller.DispatchTimedScenarioAsync(endpoint, duration, cancellationToken);
                            break;
                        }
                    }
                    break;
                }

                case OpenNettyMqttAttributes.ShutterPosition when operation is OpenNettyMqttOperation.Get:
                {
                    _ = await _controller.EnumerateShutterPositionsAsync(endpoint, cancellationToken).ToListAsync(cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.ShutterPosition when operation is OpenNettyMqttOperation.Set:
                {
                    if (!byte.TryParse(message.PayloadSegment, CultureInfo.InvariantCulture, out var position))
                    {
                        throw new InvalidDataException(SR.GetResourceString(SR.ID0068));
                    }

                    await _controller.SetShutterPositionAsync(endpoint, position, cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.ShutterState when operation is OpenNettyMqttOperation.Get:
                {
                    _ = await _controller.EnumerateShutterStatesAsync(endpoint, cancellationToken).ToListAsync(cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.ShutterState when operation is OpenNettyMqttOperation.Set:
                {
                    switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                    {
                        case "close":
                            await _controller.MoveShutterDownAsync(endpoint, cancellationToken);
                            break;

                        case "open":
                            await _controller.MoveShutterUpAsync(endpoint, cancellationToken);
                            break;

                        case "stop":
                            await _controller.StopShutterAsync(endpoint, cancellationToken);
                            break;
                    }
                    break;
                }

                case OpenNettyMqttAttributes.SmartMeterBaseIndex        when operation is OpenNettyMqttOperation.Get:
                case OpenNettyMqttAttributes.SmartMeterBlueIndex        when operation is OpenNettyMqttOperation.Get:
                case OpenNettyMqttAttributes.SmartMeterPeakOffPeakIndex when operation is OpenNettyMqttOperation.Get:
                case OpenNettyMqttAttributes.SmartMeterRedIndex         when operation is OpenNettyMqttOperation.Get:
                case OpenNettyMqttAttributes.SmartMeterSubscriptionType when operation is OpenNettyMqttOperation.Get:
                case OpenNettyMqttAttributes.SmartMeterWhiteIndex       when operation is OpenNettyMqttOperation.Get:
                {
                    _ = await _controller.GetSmartMeterIndexesAsync(endpoint, cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.SmartMeterPowerCutMode or OpenNettyMqttAttributes.SmartMeterRateType
                    when operation is OpenNettyMqttOperation.Get:
                {
                    _ = await _controller.GetSmartMeterInformationAsync(endpoint, cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.StartupDate when operation is OpenNettyMqttOperation.Get:
                {
                    _ = await _controller.GetUptimeAsync(endpoint, cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.SwitchState when operation is OpenNettyMqttOperation.Get:
                {
                    _ = await _controller.EnumerateSwitchStatesAsync(endpoint, cancellationToken).ToListAsync(cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.SwitchState when operation is OpenNettyMqttOperation.Set:
                {
                    switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                    {
                        case "on":
                            await _controller.SwitchOnAsync(endpoint, cancellationToken);
                            break;

                        case "off":
                            await _controller.SwitchOffAsync(endpoint, cancellationToken);
                            break;

                        case "toggle":
                            await _controller.ToggleAsync(endpoint, cancellationToken);
                            break;
                    }
                    break;
                }

                case OpenNettyMqttAttributes.WaterHeaterSetpointMode when operation is OpenNettyMqttOperation.Set:
                {
                    switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                    {
                        case "forced_off":
                            await _controller.SetWaterHeaterSetpointModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOff, cancellationToken);
                            break;

                        case "forced_on":
                            await _controller.SetWaterHeaterSetpointModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn, cancellationToken);
                            break;

                        case "automatic":
                            await _controller.SetWaterHeaterSetpointModeAsync(endpoint,
                                OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic, cancellationToken);
                            break;
                    }
                    break;
                }

                case OpenNettyMqttAttributes.WaterHeaterState when operation is OpenNettyMqttOperation.Get:
                {
                    _ = await _controller.GetWaterHeaterStateAsync(endpoint, cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.ZigbeeBinding when operation is OpenNettyMqttOperation.Set:
                {
                    switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                    {
                        case "bind":
                            await _controller.BindAsync(endpoint, cancellationToken);
                            break;

                        case "unbind":
                            await _controller.UnbindAsync(endpoint, cancellationToken);
                            break;
                    }
                    break;
                }

                case OpenNettyMqttAttributes.ZigbeeChannel when operation is OpenNettyMqttOperation.Get:
                {
                    _ = await _controller.GetZigbeeChannelAsync(endpoint, cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.ZigbeeDevicesCount when operation is OpenNettyMqttOperation.Get:
                {
                    _ = await _controller.CountZigbeeDevicesAsync(endpoint, cancellationToken);
                    break;
                }

                case OpenNettyMqttAttributes.ZigbeeNetwork when operation is OpenNettyMqttOperation.Set:
                {
                    switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                    {
                        case "close":
                            await _controller.CloseZigbeeNetworkAsync(endpoint, cancellationToken);
                            break;

                        case "create":
                            await _controller.CreateZigbeeNetworkAsync(endpoint, cancellationToken);
                            break;

                        case "join":
                            await _controller.JoinZigbeeNetworkAsync(endpoint, cancellationToken);
                            break;

                        case "leave":
                            await _controller.LeaveZigbeeNetworkAsync(endpoint, cancellationToken);
                            break;

                        case "open":
                            await _controller.OpenZigbeeNetworkAsync(endpoint, cancellationToken);
                            break;
                    }
                    break;
                }

                case OpenNettyMqttAttributes.ZigbeeSupervision when operation is OpenNettyMqttOperation.Set:
                {
                    switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                    {
                        case "disable":
                            await _controller.DisableSupervisionAsync(endpoint, cancellationToken);
                            break;

                        case "enable":
                            await _controller.EnableSupervisionAsync(endpoint, cancellationToken);
                            break;
                    }
                    break;
                }

                case OpenNettyMqttAttributes.DeviceName when operation is OpenNettyMqttOperation.Set:
                {
                    var newName = message.ConvertPayloadToString();
                    if (!string.IsNullOrEmpty(newName) && endpoint.Device is OpenNettyDevice device)
                    {
                        RenameDevice(device, newName);

                        // Re-announce the endpoints to update Home Assistant.
                        await AnnounceEndpointsAsync(client, cancellationToken);
                    }
                    break;
                }
            }

            static JsonObject? TryParseAsJsonObject(string value)
            {
                try
                {
                    return JsonObject.Parse(value)?.AsObject();
                }

                catch (JsonException)
                {
                    return null;
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task AnnounceEndpointsAsync(IManagedMqttClient client, CancellationToken cancellationToken)
    {
        if (_options.CurrentValue is not { DisableHomeAssistantDiscovery: false } options)
        {
            return;
        }

        await foreach (var device in _manager.EnumerateDevicesAsync(cancellationToken))
        {
            if (device.GetBooleanSetting(OpenNettySettings.HomeAssistantDiscovery) is false)
            {
                continue;
            }

            var culture = device.GetStringSetting(OpenNettySettings.HomeAssistantDiscoveryUICulture) switch
            {
                { Length: > 0 } value => CultureInfo.GetCultureInfo(value),

                _ => options.HomeAssistantDiscoveryUICulture
            };

            var components = new JsonObject();

            var configuration = new JsonObject
            {
                ["origin"] = new JsonObject
                {
                    ["name"] = "OpenNetty",
                    ["sw_version"] = typeof(OpenNettyMqttWorker).Assembly
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                       ?.InformationalVersion,
                    ["support_url"] = "https://github.com/opennetty/opennetty-core"
                },
                ["device"] = CreateDeviceNode(device, culture),
                ["qos"] = 2,
                ["components"] = components
            };

            await foreach (var endpoint in _manager.FindEndpointsByDeviceAsync(device, cancellationToken))
            {
                var topic = endpoint.GetStringSetting(OpenNettySettings.MqttTopic) ?? endpoint.Name.ToLowerInvariant();

                if (SupportsLightOrSwitchEntity(endpoint))
                {
                    var platform = endpoint.GetStringSetting(OpenNettySettings.HomeAssistantEntityType) switch
                    {
                        { Length: > 0 } value => value,

                        // If the endpoint has a switch mode setting attached, represent it as a switch by default.
                        _ when !string.IsNullOrEmpty(endpoint.GetStringSetting(OpenNettySettings.SwitchMode))
                            => OpenNettySettings.HomeAssistantEntityTypes.Switch,

                        // Endpoints that support ON/OFF switching are always treated as light entities by default.
                        _ => OpenNettySettings.HomeAssistantEntityTypes.Light
                    };

                    if (platform is not (OpenNettySettings.HomeAssistantEntityTypes.Light or OpenNettySettings.HomeAssistantEntityTypes.Switch))
                    {
                        throw new InvalidOperationException(SR.FormatID0120(platform));
                    }

                    var name = platform switch
                    {
                        OpenNettySettings.HomeAssistantEntityTypes.Light  => endpoint.GetStringSetting(OpenNettySettings.HomeAssistantLightName),
                        OpenNettySettings.HomeAssistantEntityTypes.Switch => endpoint.GetStringSetting(OpenNettySettings.HomeAssistantSwitchName),

                        _ => throw new InvalidOperationException(SR.FormatID0120(platform))
                    };

                    var icon = platform switch
                    {
                        OpenNettySettings.HomeAssistantEntityTypes.Light  => endpoint.GetStringSetting(OpenNettySettings.HomeAssistantLightIcon),
                        OpenNettySettings.HomeAssistantEntityTypes.Switch => endpoint.GetStringSetting(OpenNettySettings.HomeAssistantSwitchIcon),

                        _ => throw new InvalidOperationException(SR.FormatID0120(platform))
                    };

                    var component = new JsonObject
                    {
                        ["platform"] = platform,
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "feb44223-4814-4652-933c-53dbbaabac3f"u8),
                        ["name"] = name ?? ComputeEntityName(
                            name    : platform is OpenNettySettings.HomeAssistantEntityTypes.Light ?
                                GetLocalizedString(SR.ID8001, culture) : GetLocalizedString(SR.ID8000, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(SupportsLightOrSwitchEntity)
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SwitchState}/set"
                    };

                    if (!string.IsNullOrEmpty(icon))
                    {
                        component["icon"] = icon;
                    }

                    // Note: endpoints that can't report their state (e.g radio Nitoo devices) can still be mapped to a light entity:
                    // in that case, Home Assistant will automatically use the optimistic mode to dynamically update the current state.
                    if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                    {
                        component["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SwitchState}";
                    }

                    if (platform is OpenNettySettings.HomeAssistantEntityTypes.Light)
                    {
                        if (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingControl) ||
                            endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingControl))
                        {
                            component["brightness_command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Brightness}/set";
                            component["brightness_scale"] = 100;

                            // Note: unlike MyHome devices, Nitoo devices do not store the last brightness level set and always set
                            // the brightness to 100% when receiving an ON command. To have a consistent behavior across all devices,
                            // Nitoo devices are, by default, configured to use the brightness command topic to turn on the light.
                            component["on_command_type"] = endpoint.GetStringSetting(OpenNettySettings.HomeAssistantLightOnCommandType) switch
                            {
                                { Length: > 0 } value => value,

                                _ when endpoint.Protocol is OpenNettyProtocol.Nitoo => "brightness",
                                _ => "last"
                            };
                        }

                        if (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                            endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                        {
                            component["brightness_state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Brightness}";
                        }
                    }

                    // Note: switch entities can specify a device class but cannot support brightness control.
                    else if (platform is OpenNettySettings.HomeAssistantEntityTypes.Switch)
                    {
                        component["device_class"] = endpoint.GetStringSetting(OpenNettySettings.HomeAssistantSwitchDeviceClass)
                            ?? OpenNettySettings.HomeAssistantDeviceClasses.Switch;
                    }

                    components.Add(CreateEntityNode(component));

                    if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                    {
                        components.Add(CreateEntityNode(new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "3a10d925-c599-41a9-8a7e-30a04aefec86"u8),
                            ["entity_category"] = "diagnostic",
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8002, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                    .Where(SupportsLightOrSwitchEntity)
                                    .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                                    .CountAsync(cancellationToken)),
                            ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SwitchState}/get",
                            ["payload_press"] = string.Empty
                        }));
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                        endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                    {
                        components.Add(CreateEntityNode(new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "f05ecfb8-70d5-4116-b0a8-2f6d9d02090f"u8),
                            ["entity_category"] = "diagnostic",
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8003, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                    .Where(SupportsLightOrSwitchEntity)
                                    .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                                                              endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                                    .CountAsync(cancellationToken)),
                            ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Brightness}/get",
                            ["payload_press"] = string.Empty
                        }));
                    }
                }

                if (SupportsCoverEntity(endpoint))
                {
                    var component = new JsonObject
                    {
                        ["platform"] = "cover",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "9b138d62-bb0d-49cb-8624-1d85f9e86a6e"u8),
                        ["device_class"] = endpoint.GetStringSetting(OpenNettySettings.HomeAssistantCoverDeviceClass)
                            ?? OpenNettySettings.HomeAssistantDeviceClasses.Shutter,
                        ["name"] = endpoint.GetStringSetting(OpenNettySettings.HomeAssistantCoverName) ??
                            ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8004, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                    .Where(SupportsCoverEntity)
                                    .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ShutterState}/set"
                    };

                    if (endpoint.HasCapability(OpenNettyCapabilities.BasicShutterState))
                    {
                        component["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ShutterState}";
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterControl))
                    {
                        component["set_position_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ShutterPosition}/set";
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
                    {
                        component["position_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ShutterPosition}";
                    }

                    components.Add(CreateEntityNode(component));

                    if (endpoint.HasCapability(OpenNettyCapabilities.BasicShutterState) ||
                        endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
                    {
                        components.Add(CreateEntityNode(new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "3a760f9d-ca89-4c9e-9ad4-8ba40ad36f59"u8),
                            ["entity_category"] = "diagnostic",
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8005, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                    .Where(SupportsCoverEntity)
                                    .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.BasicShutterState) ||
                                                              endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
                                    .CountAsync(cancellationToken)),
                            ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ShutterState}/get",
                            ["payload_press"] = string.Empty
                        }));
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
                    {
                        components.Add(CreateEntityNode(new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "5731274c-e498-4c7b-8671-6de8c448eb99"u8),
                            ["entity_category"] = "diagnostic",
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8006, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                    .Where(SupportsCoverEntity)
                                    .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
                                    .CountAsync(cancellationToken)),
                            ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ShutterPosition}/get",
                            ["payload_press"] = string.Empty
                        }));
                    }
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioEvent)       ||
                    endpoint.HasCapability(OpenNettyCapabilities.DimmingScenarioEvent)      ||
                    endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioEvent)        ||
                    endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioEvent)     ||
                    endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioPlusEvent) ||
                    endpoint.HasCapability(OpenNettyCapabilities.ProgressiveScenarioEvent)  ||
                    endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioEvent)   ||
                    endpoint.HasCapability(OpenNettyCapabilities.TimedScenarioEvent)        ||
                    endpoint.HasCapability(OpenNettyCapabilities.ToggleScenarioEvent))
                {
                    var types = new HashSet<string>(StringComparer.Ordinal);

                    if (endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioEvent))
                    {
                        types.Add("action");
                        types.Add("stop_action");
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.DimmingScenarioEvent))
                    {
                        types.Add("dimming");
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioEvent))
                    {
                        types.Add("switch_on");
                        types.Add("switch_off");
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioEvent))
                    {
                        types.Add("pressure");
                        types.Add("release_after_short_pressure");
                        types.Add("release_after_extended_pressure");
                        types.Add("extended_pressure");
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioPlusEvent))
                    {
                        types.Add("short_pressure");
                        types.Add("start_of_extended_pressure");
                        types.Add("extended_pressure");
                        types.Add("end_of_extended_pressure");
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.ProgressiveScenarioEvent))
                    {
                        types.Add("progressive_action");
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioEvent))
                    {
                        types.Add("shutter_up");
                        types.Add("shutter_down");
                        types.Add("shutter_stop");
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.TimedScenarioEvent))
                    {
                        types.Add("timed_action");
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.ToggleScenarioEvent))
                    {
                        types.Add("switch_toggle");
                    }

                    var component = new JsonObject
                    {
                        ["platform"] = "event",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "7faeaa9a-ae51-43f4-af82-65d2e24e14d0"u8),
                        ["icon"] = endpoint.GetStringSetting(OpenNettySettings.HomeAssistantScenarioIcon) ?? "mdi:lightning-bolt",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8007, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint =>
                                    endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioEvent)       ||
                                    endpoint.HasCapability(OpenNettyCapabilities.DimmingScenarioEvent)      ||
                                    endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioEvent)        ||
                                    endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioEvent)     ||
                                    endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioPlusEvent) ||
                                    endpoint.HasCapability(OpenNettyCapabilities.ProgressiveScenarioEvent)  ||
                                    endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioEvent)   ||
                                    endpoint.HasCapability(OpenNettyCapabilities.TimedScenarioEvent)        ||
                                    endpoint.HasCapability(OpenNettyCapabilities.ToggleScenarioEvent))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}",
                        ["json_attributes_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}",
                        ["event_types"] = new JsonArray([.. types]),
                        // Unlike Nitoo devices that emit observable scenarios without requiring any preliminary configuration,
                        // Zigbee devices must be explicitly bound to the OpenWebNet gateway via a push-and-learn binding for
                        // scenarios to be triggered. As such, scenario entities are not enabled by default for Zigbee devices.
                        ["enabled_by_default"] = endpoint.Protocol is not OpenNettyProtocol.Zigbee
                    };

                    if (endpoint.GetStringSetting(OpenNettySettings.HomeAssistantScenarioDeviceClass) is string type)
                    {
                        component["device_class"] = type;
                    }

                    components.Add(CreateEntityNode(component));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioActivation))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "63485e4c-a3bd-4fc9-831d-b96bacddade9"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8008, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "action"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "bca953c0-7598-4baa-91df-f14ddc30450f"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8025, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "stop_action",
                        ["enabled_by_default"] = false
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.DimmingScenarioActivation))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "f47365e3-86fa-449f-b84e-a06acc3484b1"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8111, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.DimmingScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = new JsonObject { ["event_type"] = "dimming", ["dimming_step"] = 5 }.ToJsonString(),
                        ["enabled_by_default"] = false
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "5c2db171-97b3-451e-a502-8800928b4335"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8112, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.DimmingScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = new JsonObject { ["event_type"] = "dimming", ["dimming_step"] = -5 }.ToJsonString(),
                        ["enabled_by_default"] = false
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioActivation))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "f053a594-66fa-42a6-9237-64d570b2bd57"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8009, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "switch_on"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "d292636e-00d3-442e-b1db-73d60b4085ec"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8010, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "switch_off"
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.OutgoingCommunication))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "9563390d-24c4-48f0-a0de-61fef1e58eca"u8),
                        ["entity_category"] = "diagnostic",
                        ["device_class"] = "timestamp",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8119, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.OutgoingCommunication))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.LastCommunicationDate}",
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation) &&
                    endpoint.GetStringSetting(OpenNettySettings.FunctionType) is OpenNettySettings.FunctionTypes.ScheduledScenario)
                {
                    if (endpoint.GetStringSetting(OpenNettySettings.PushButtonNumbers) is string value &&
                        value.Split([','], StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } values &&
                        values.Select(value => uint.Parse(value, CultureInfo.InvariantCulture)).ToList() is List<uint> buttons)
                    {
                        foreach (var button in buttons)
                        {
                            components.Add(CreateEntityNode(new JsonObject
                            {
                                ["platform"] = "button",
                                ["unique_id"] = ComputeEntityUniqueId(endpoint,
                                [
                                    .. "be2887c3-f4ae-4935-bad6-1ffb1227d28b"u8,
                                    .. Encoding.UTF8.GetBytes(button.ToString(CultureInfo.InvariantCulture))
                                ]),
                                ["name"] = ComputeEntityName(
                                    name    : string.Format(GetLocalizedString(SR.ID8011, culture), button.ToString(CultureInfo.InvariantCulture)),
                                    endpoint: endpoint,
                                    culture : culture,
                                    count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                        .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                        .CountAsync(cancellationToken)),
                                ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                                ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                                ["payload_press"] = new JsonObject { ["event_type"] = "pressure", ["scenario_type"] = "basic", ["button"] = button }.ToJsonString()
                            }));

                            components.Add(CreateEntityNode(new JsonObject
                            {
                                ["platform"] = "button",
                                ["unique_id"] = ComputeEntityUniqueId(endpoint,
                                [
                                    .. "81e3f75a-fab3-4842-bb5e-1531d20290dd"u8,
                                    .. Encoding.UTF8.GetBytes(button.ToString(CultureInfo.InvariantCulture))
                                ]),
                                ["name"] = ComputeEntityName(
                                    name    : string.Format(GetLocalizedString(SR.ID8012, culture), button.ToString(CultureInfo.InvariantCulture)),
                                    endpoint: endpoint,
                                    culture : culture,
                                    count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                        .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                        .CountAsync(cancellationToken)),
                                ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                                ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                                ["payload_press"] = new JsonObject { ["event_type"] = "release_after_short_pressure", ["scenario_type"] = "evolved", ["button"] = button }.ToJsonString()
                            }));

                            components.Add(CreateEntityNode(new JsonObject
                            {
                                ["platform"] = "button",
                                ["unique_id"] = ComputeEntityUniqueId(endpoint,
                                [
                                    .. "3f069e7f-7c8f-4730-b067-9a944f61700b"u8,
                                    .. Encoding.UTF8.GetBytes(button.ToString(CultureInfo.InvariantCulture))
                                ]),
                                ["name"] = ComputeEntityName(
                                    name    : string.Format(GetLocalizedString(SR.ID8013, culture), button.ToString(CultureInfo.InvariantCulture)),
                                    endpoint: endpoint,
                                    culture : culture,
                                    count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                        .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                        .CountAsync(cancellationToken)),
                                ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                                ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                                ["payload_press"] = new JsonObject { ["event_type"] = "release_after_extended_pressure", ["scenario_type"] = "evolved", ["button"] = button }.ToJsonString()
                            }));

                            components.Add(CreateEntityNode(new JsonObject
                            {
                                ["platform"] = "button",
                                ["unique_id"] = ComputeEntityUniqueId(endpoint,
                                [
                                    .. "23e04eeb-8b35-44c8-87da-aed3829ce07d"u8,
                                    .. Encoding.UTF8.GetBytes(button.ToString(CultureInfo.InvariantCulture))
                                ]),
                                ["name"] = ComputeEntityName(
                                    name    : string.Format(GetLocalizedString(SR.ID8014, culture), button.ToString(CultureInfo.InvariantCulture)),
                                    endpoint: endpoint,
                                    culture : culture,
                                    count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                        .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                        .CountAsync(cancellationToken)),
                                ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                                ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                                ["payload_press"] = new JsonObject { ["event_type"] = "extended_pressure", ["scenario_type"] = "evolved", ["button"] = button }.ToJsonString()
                            }));
                        }
                    }

                    else
                    {
                        components.Add(CreateEntityNode(new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "be2887c3-f4ae-4935-bad6-1ffb1227d28b"u8),
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8015, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                    .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                    .CountAsync(cancellationToken)),
                            ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                            ["payload_press"] = new JsonObject { ["event_type"] = "pressure", ["scenario_type"] = "basic" }.ToJsonString()
                        }));

                        components.Add(CreateEntityNode(new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "81e3f75a-fab3-4842-bb5e-1531d20290dd"u8),
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8016, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                    .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                    .CountAsync(cancellationToken)),
                            ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                            ["payload_press"] = new JsonObject { ["event_type"] = "release_after_short_pressure", ["scenario_type"] = "evolved" }.ToJsonString()
                        }));

                        components.Add(CreateEntityNode(new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "3f069e7f-7c8f-4730-b067-9a944f61700b"u8),
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8017, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                    .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                    .CountAsync(cancellationToken)),
                            ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                            ["payload_press"] = new JsonObject { ["event_type"] = "release_after_extended_pressure", ["scenario_type"] = "evolved" }.ToJsonString()
                        }));

                        components.Add(CreateEntityNode(new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "23e04eeb-8b35-44c8-87da-aed3829ce07d"u8),
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8018, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                    .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                    .CountAsync(cancellationToken)),
                            ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                            ["payload_press"] = new JsonObject { ["event_type"] = "extended_pressure", ["scenario_type"] = "evolved" }.ToJsonString()
                        }));
                    }
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioPlusActivation) &&
                    endpoint.GetStringSetting(OpenNettySettings.FunctionType) is OpenNettySettings.FunctionTypes.ScheduledScenarioPlus)
                {
                    if (endpoint.GetStringSetting(OpenNettySettings.PushButtonNumbers) is string value &&
                        value.Split([','], StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } values &&
                        values.Select(value => uint.Parse(value, CultureInfo.InvariantCulture)).ToList() is List<uint> buttons)
                    {
                        foreach (var button in buttons)
                        {
                            components.Add(CreateEntityNode(new JsonObject
                            {
                                ["platform"] = "button",
                                ["unique_id"] = ComputeEntityUniqueId(endpoint,
                                [
                                    .. "63a92ba4-bec3-453e-a44a-9219e4f4d478"u8,
                                    .. Encoding.UTF8.GetBytes(button.ToString(CultureInfo.InvariantCulture))
                                ]),
                                ["name"] = ComputeEntityName(
                                    name    : string.Format(GetLocalizedString(SR.ID8019, culture), button.ToString(CultureInfo.InvariantCulture)),
                                    endpoint: endpoint,
                                    culture : culture,
                                    count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                        .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                        .CountAsync(cancellationToken)),
                                ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                                ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                                ["payload_press"] = new JsonObject { ["event_type"] = "short_pressure", ["scenario_type"] = "plus", ["button"] = button }.ToJsonString()
                            }));

                            components.Add(CreateEntityNode(new JsonObject
                            {
                                ["platform"] = "button",
                                ["unique_id"] = ComputeEntityUniqueId(endpoint,
                                [
                                    .. "e524f94b-9862-4da4-8f57-82c220e4560c"u8,
                                    .. Encoding.UTF8.GetBytes(button.ToString(CultureInfo.InvariantCulture))
                                ]),
                                ["name"] = ComputeEntityName(
                                    name    : string.Format(GetLocalizedString(SR.ID8020, culture), button.ToString(CultureInfo.InvariantCulture)),
                                    endpoint: endpoint,
                                    culture : culture,
                                    count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                        .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                        .CountAsync(cancellationToken)),
                                ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                                ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                                ["payload_press"] = new JsonObject { ["event_type"] = "start_of_extended_pressure", ["scenario_type"] = "plus", ["button"] = button }.ToJsonString()
                            }));

                            components.Add(CreateEntityNode(new JsonObject
                            {
                                ["platform"] = "button",
                                ["unique_id"] = ComputeEntityUniqueId(endpoint,
                                [
                                    .. "bf90ede8-9078-4817-8ec4-ef762c3e2077"u8,
                                    .. Encoding.UTF8.GetBytes(button.ToString(CultureInfo.InvariantCulture))
                                ]),
                                ["name"] = ComputeEntityName(
                                    name    : string.Format(GetLocalizedString(SR.ID8014, culture), button.ToString(CultureInfo.InvariantCulture)),
                                    endpoint: endpoint,
                                    culture : culture,
                                    count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                        .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                        .CountAsync(cancellationToken)),
                                ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                                ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                                ["payload_press"] = new JsonObject { ["event_type"] = "extended_pressure", ["scenario_type"] = "plus", ["button"] = button }.ToJsonString()
                            }));

                            components.Add(CreateEntityNode(new JsonObject
                            {
                                ["platform"] = "button",
                                ["unique_id"] = ComputeEntityUniqueId(endpoint,
                                [
                                    .. "73ff2263-9962-443a-89a1-f209fba81948"u8,
                                    .. Encoding.UTF8.GetBytes(button.ToString(CultureInfo.InvariantCulture))
                                ]),
                                ["name"] = ComputeEntityName(
                                    name    : string.Format(GetLocalizedString(SR.ID8021, culture), button.ToString(CultureInfo.InvariantCulture)),
                                    endpoint: endpoint,
                                    culture : culture,
                                    count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                        .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                        .CountAsync(cancellationToken)),
                                ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                                ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                                ["payload_press"] = new JsonObject { ["event_type"] = "end_of_extended_pressure", ["scenario_type"] = "plus", ["button"] = button }.ToJsonString()
                            }));
                        }
                    }

                    else
                    {
                        components.Add(CreateEntityNode(new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "63a92ba4-bec3-453e-a44a-9219e4f4d478"u8),
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8022, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                    .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                    .CountAsync(cancellationToken)),
                            ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                            ["payload_press"] = new JsonObject { ["event_type"] = "short_pressure", ["scenario_type"] = "plus" }.ToJsonString()
                        }));

                        components.Add(CreateEntityNode(new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "e524f94b-9862-4da4-8f57-82c220e4560c"u8),
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8023, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                    .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                    .CountAsync(cancellationToken)),
                            ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                            ["payload_press"] = new JsonObject { ["event_type"] = "start_of_extended_pressure", ["scenario_type"] = "plus" }.ToJsonString()
                        }));

                        components.Add(CreateEntityNode(new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "bf90ede8-9078-4817-8ec4-ef762c3e2077"u8),
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8018, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                    .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                    .CountAsync(cancellationToken)),
                            ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                            ["payload_press"] = new JsonObject { ["event_type"] = "extended_pressure", ["scenario_type"] = "plus" }.ToJsonString()
                        }));

                        components.Add(CreateEntityNode(new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "73ff2263-9962-443a-89a1-f209fba81948"u8),
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8024, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                    .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                    .CountAsync(cancellationToken)),
                            ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                            ["payload_press"] = new JsonObject { ["event_type"] = "end_of_extended_pressure", ["scenario_type"] = "plus" }.ToJsonString()
                        }));
                    }
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioActivation))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "9065ccb4-d2c6-47f5-b118-e3d2ecbe20c3"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8026, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "shutter_up"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "4006cf9e-620c-49d4-81b3-ab060d376966"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8027, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "shutter_down"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "d13fdcd2-c974-490a-b544-436a91785ffa"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8028, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "shutter_stop"
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.BatteryAlert))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "binary_sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "b7dd3824-ddc3-4cf3-b79e-168faa710e43"u8),
                        ["entity_category"] = "diagnostic",
                        ["device_class"] = "battery",
                        ["off_delay"] = 3600,
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8029, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.BatteryAlert))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.BatteryAlert}"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "7f7625f0-2461-4804-9cae-829a1040cd93"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:battery-check",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8030, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.BatteryAlert))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.BatteryAlert}/set",
                        ["payload_press"] = "OFF"
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.BatteryLevel))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "6a8c7f1c-426a-47ab-97a1-a476acc603fd"u8),
                        ["entity_category"] = "diagnostic",
                        ["device_class"] = "battery",
                        ["unit_of_measurement"] = "%",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8031, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.BatteryLevel))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.BatteryLevel}"
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.FirmwareVersion))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "3a92e77f-3910-4a20-9d19-caa1961dc33d"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8032, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.FirmwareVersion))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.FirmwareVersion}"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "e4fa32b3-9e9f-43b5-810a-cdb75acf44e5"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:help",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8033, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.FirmwareVersion))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.FirmwareVersion}/get",
                        ["payload_press"] = string.Empty
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.HardwareVersion))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "1091f326-0c22-4c59-af04-d0a6ee429a0c"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8034, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.HardwareVersion))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.HardwareVersion}"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "0371ccbb-52fd-4288-a943-b2f04a7b1e8b"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:help",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8035, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.HardwareVersion))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.HardwareVersion}/get",
                        ["payload_press"] = string.Empty
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.MacAddress))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "a8de45b2-0bb5-4375-b33b-0869623e40a7"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8036, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.MacAddress))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.MacAddress}"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "aa1968e6-f232-4b96-a29f-0e64de093bb0"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:help",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8037, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.MacAddress))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.MacAddress}/get",
                        ["payload_press"] = string.Empty
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireControl))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "select",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "205a01a1-ba4c-4e9b-a19a-c1589c445cbb"u8),
                        ["icon"] = "mdi:radiator",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8039, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireControl))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireSetpointMode}/set",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireSetpointMode}",
                        ["options"] = new JsonArray(
                        [
                            GetLocalizedString(SR.ID8040, culture),
                            GetLocalizedString(SR.ID8041, culture),
                            GetLocalizedString(SR.ID8042, culture),
                            GetLocalizedString(SR.ID8043, culture),
                            GetLocalizedString(SR.ID8044, culture)
                        ]),
                        ["value_template"] = $$$"""
                            {% set map = {
                              'comfort': '{{{GetLocalizedString(SR.ID8040, culture).Replace("'", "\\'")}}}',
                              'comfort-1': '{{{GetLocalizedString(SR.ID8041, culture).Replace("'", "\\'")}}}',
                              'comfort-2': '{{{GetLocalizedString(SR.ID8042, culture).Replace("'", "\\'")}}}',
                              'eco': '{{{GetLocalizedString(SR.ID8043, culture).Replace("'", "\\'")}}}',
                              'frost_protection': '{{{GetLocalizedString(SR.ID8044, culture).Replace("'", "\\'")}}}'
                            } %}
                            {{ map[value] }}
                            """,
                        ["command_template"] = $$$"""
                            {% set map = {
                              '{{{GetLocalizedString(SR.ID8040, culture).Replace("'", "\\'")}}}': 'comfort',
                              '{{{GetLocalizedString(SR.ID8041, culture).Replace("'", "\\'")}}}': 'comfort-1',
                              '{{{GetLocalizedString(SR.ID8042, culture).Replace("'", "\\'")}}}': 'comfort-2',
                              '{{{GetLocalizedString(SR.ID8043, culture).Replace("'", "\\'")}}}': 'eco',
                              '{{{GetLocalizedString(SR.ID8044, culture).Replace("'", "\\'")}}}': 'frost_protection'
                            } %}
                            {{ map[value] }}
                            """
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "7a9130b9-675a-437d-b806-cbfe6f6e20a6"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8062, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireControl))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireSetpointMode}/get",
                        ["payload_press"] = string.Empty
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireDerogation))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "select",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "f5f57920-d758-4ca8-8161-2614d4abeef0"u8),
                        ["icon"] = "mdi:radiator",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8045, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireDerogation))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireDerogationMode}/set",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireDerogationMode}",
                        ["options"] = new JsonArray(
                        [
                            GetLocalizedString(SR.ID8046, culture),
                            GetLocalizedString(SR.ID8047, culture),
                            GetLocalizedString(SR.ID8048, culture),
                            GetLocalizedString(SR.ID8049, culture),
                            GetLocalizedString(SR.ID8050, culture),
                            GetLocalizedString(SR.ID8051, culture),
                            GetLocalizedString(SR.ID8052, culture),
                            GetLocalizedString(SR.ID8053, culture),
                            GetLocalizedString(SR.ID8054, culture),
                            GetLocalizedString(SR.ID8055, culture),
                            GetLocalizedString(SR.ID8056, culture),
                            GetLocalizedString(SR.ID8057, culture),
                            GetLocalizedString(SR.ID8058, culture),
                            GetLocalizedString(SR.ID8059, culture),
                            GetLocalizedString(SR.ID8060, culture),
                            GetLocalizedString(SR.ID8061, culture)
                        ]),
                        ["value_template"] = $$$"""
                            {% set map = {
                              'none': '{{{GetLocalizedString(SR.ID8046, culture).Replace("'", "\\'")}}}',
                              'comfort': '{{{GetLocalizedString(SR.ID8047, culture).Replace("'", "\\'")}}}',
                              'comfort:4h': '{{{GetLocalizedString(SR.ID8048, culture).Replace("'", "\\'")}}}',
                              'comfort:8h': '{{{GetLocalizedString(SR.ID8049, culture).Replace("'", "\\'")}}}',
                              'comfort-1': '{{{GetLocalizedString(SR.ID8050, culture).Replace("'", "\\'")}}}',
                              'comfort-1:4h': '{{{GetLocalizedString(SR.ID8051, culture).Replace("'", "\\'")}}}',
                              'comfort-1:8h': '{{{GetLocalizedString(SR.ID8052, culture).Replace("'", "\\'")}}}',
                              'comfort-2': '{{{GetLocalizedString(SR.ID8053, culture).Replace("'", "\\'")}}}',
                              'comfort-2:4h': '{{{GetLocalizedString(SR.ID8054, culture).Replace("'", "\\'")}}}',
                              'comfort-2:8h': '{{{GetLocalizedString(SR.ID8055, culture).Replace("'", "\\'")}}}',
                              'eco': '{{{GetLocalizedString(SR.ID8056, culture).Replace("'", "\\'")}}}',
                              'eco:4h': '{{{GetLocalizedString(SR.ID8057, culture).Replace("'", "\\'")}}}',
                              'eco:8h': '{{{GetLocalizedString(SR.ID8058, culture).Replace("'", "\\'")}}}',
                              'frost_protection': '{{{GetLocalizedString(SR.ID8059, culture).Replace("'", "\\'")}}}',
                              'frost_protection:4h': '{{{GetLocalizedString(SR.ID8060, culture).Replace("'", "\\'")}}}',
                              'frost_protection:8h': '{{{GetLocalizedString(SR.ID8061, culture).Replace("'", "\\'")}}}'
                            } %}
                            {{ map[value] }}
                            """,
                        ["command_template"] = $$$"""
                            {% set map = {
                              '{{{GetLocalizedString(SR.ID8046, culture).Replace("'", "\\'")}}}': 'none',
                              '{{{GetLocalizedString(SR.ID8047, culture).Replace("'", "\\'")}}}': 'comfort',
                              '{{{GetLocalizedString(SR.ID8048, culture).Replace("'", "\\'")}}}': 'comfort:4h',
                              '{{{GetLocalizedString(SR.ID8049, culture).Replace("'", "\\'")}}}': 'comfort:8h',
                              '{{{GetLocalizedString(SR.ID8050, culture).Replace("'", "\\'")}}}': 'comfort-1',
                              '{{{GetLocalizedString(SR.ID8051, culture).Replace("'", "\\'")}}}': 'comfort-1:4h',
                              '{{{GetLocalizedString(SR.ID8052, culture).Replace("'", "\\'")}}}': 'comfort-1:8h',
                              '{{{GetLocalizedString(SR.ID8053, culture).Replace("'", "\\'")}}}': 'comfort-2',
                              '{{{GetLocalizedString(SR.ID8054, culture).Replace("'", "\\'")}}}': 'comfort-2:4h',
                              '{{{GetLocalizedString(SR.ID8055, culture).Replace("'", "\\'")}}}': 'comfort-2:8h',
                              '{{{GetLocalizedString(SR.ID8056, culture).Replace("'", "\\'")}}}': 'eco',
                              '{{{GetLocalizedString(SR.ID8057, culture).Replace("'", "\\'")}}}': 'eco:4h',
                              '{{{GetLocalizedString(SR.ID8058, culture).Replace("'", "\\'")}}}': 'eco:8h',
                              '{{{GetLocalizedString(SR.ID8059, culture).Replace("'", "\\'")}}}': 'frost_protection',
                              '{{{GetLocalizedString(SR.ID8060, culture).Replace("'", "\\'")}}}': 'frost_protection:4h',
                              '{{{GetLocalizedString(SR.ID8061, culture).Replace("'", "\\'")}}}': 'frost_protection:8h'
                            } %}
                            {{ map[value] }}
                            """
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "787582e8-0c5f-4c97-9277-0ad23dab4024"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8063, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireDerogation))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireDerogationMode}/get",
                        ["payload_press"] = string.Empty
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireShutdown))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "switch",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "178d9f9b-e87a-4ebf-8db3-80e1e1a091df"u8),
                        ["icon"] = "mdi:radiator-off",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8113, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireShutdown))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireShutdownMode}/set",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireShutdownMode}"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "ecc822a6-57ab-4352-8d2c-d85dc73df5da"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8114, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireShutdown))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireShutdownMode}/get",
                        ["payload_press"] = string.Empty
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "6c83787a-3537-49fa-b409-dc15d5c37b43"u8),
                        ["device_class"] = "energy",
                        ["state_class"] = "total_increasing",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8064, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterBaseIndex}",
                        ["value_template"] = "{{ value_json.base_index }}"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "0a9d909b-8449-496e-b38c-4dc3e2653288"u8),
                        ["device_class"] = "energy",
                        ["state_class"] = "total_increasing",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8065, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterBlueIndex}",
                        ["value_template"] = "{{ value_json.base_index }}"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "ccf0ce01-7b31-4eb6-995f-94038c186d20"u8),
                        ["device_class"] = "energy",
                        ["state_class"] = "total_increasing",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8115, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterBlueIndex}",
                        ["value_template"] = "{{ value_json.off_peak_index }}"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "2661d8db-085a-41bb-bab6-a1627cbf91d0"u8),
                        ["device_class"] = "energy",
                        ["state_class"] = "total_increasing",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8066, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterPeakOffPeakIndex}",
                        ["value_template"] = "{{ value_json.base_index }}"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "b087f0f1-db08-4a51-897a-dd5427590ad8"u8),
                        ["device_class"] = "energy",
                        ["state_class"] = "total_increasing",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8116, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterPeakOffPeakIndex}",
                        ["value_template"] = "{{ value_json.off_peak_index }}"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "7dcf846c-fbdd-4457-9a17-9cbc0a7c072b"u8),
                        ["device_class"] = "energy",
                        ["state_class"] = "total_increasing",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8067, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterRedIndex}",
                        ["value_template"] = "{{ value_json.base_index }}"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "7efb6978-3112-4ef6-86f9-fe9127a4411d"u8),
                        ["device_class"] = "energy",
                        ["state_class"] = "total_increasing",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8117, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterRedIndex}",
                        ["value_template"] = "{{ value_json.off_peak_index }}"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "1de29bc5-70b6-4302-aa27-8ecbcce13ec9"u8),
                        ["device_class"] = "energy",
                        ["state_class"] = "total_increasing",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8068, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterWhiteIndex}",
                        ["value_template"] = "{{ value_json.base_index }}"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "89b5ff55-499a-45f0-948a-28e74e02bc6f"u8),
                        ["device_class"] = "energy",
                        ["state_class"] = "total_increasing",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8118, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterWhiteIndex}",
                        ["value_template"] = "{{ value_json.off_peak_index }}"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "e07f0687-6ca1-47d9-a5b7-b20c0e79775a"u8),
                        ["device_class"] = "enum",
                        ["icon"] = "mdi:receipt-text-outline",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8069, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterSubscriptionType}",
                        ["options"] = new JsonArray(
                        [
                            GetLocalizedString(SR.ID8070, culture),
                            GetLocalizedString(SR.ID8071, culture),
                            GetLocalizedString(SR.ID8072, culture)
                        ]),
                        ["value_template"] = $$$"""
                            {% set map = {
                              'base': '{{{GetLocalizedString(SR.ID8070, culture).Replace("'", "\\'")}}}',
                              'peak/off_peak': '{{{GetLocalizedString(SR.ID8071, culture).Replace("'", "\\'")}}}',
                              'tempo': '{{{GetLocalizedString(SR.ID8072, culture).Replace("'", "\\'")}}}'
                            } %}
                            {{ map[value] }}
                            """,
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "2eddee8f-7c81-47ea-a775-785e9dfb5c26"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8073, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterBaseIndex}/get",
                        ["payload_press"] = string.Empty
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "31eda1f3-343f-4cd7-9f56-ea792fcaec7f"u8),
                        ["device_class"] = "enum",
                        ["icon"] = "mdi:receipt-text-outline",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8074, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterRateType}",
                        ["options"] = new JsonArray([GetLocalizedString(SR.ID8075, culture), GetLocalizedString(SR.ID8076, culture)]),
                        ["value_template"] = $$$"""
                            {% set map = {
                              'peak': '{{{GetLocalizedString(SR.ID8075, culture).Replace("'", "\\'")}}}',
                              'off_peak': '{{{GetLocalizedString(SR.ID8076, culture).Replace("'", "\\'")}}}'
                            } %}
                            {{ map[value] }}
                            """,
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "binary_sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "280fd1d1-4220-42ec-bf70-69936b728eb2"u8),
                        ["device_class"] = "running",
                        ["icon"] = "mdi:transmission-tower-off",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8077, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterPowerCutMode}"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "91e32508-61fa-46e3-ba57-32166b2de116"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8078, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterRateType}/get",
                        ["payload_press"] = string.Empty
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "df24321d-01e8-4d1a-9cca-92ece18934b4"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8079, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterPowerCutMode}/get",
                        ["payload_press"] = string.Empty
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.Uptime))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "46c1f892-f9bf-46b6-8658-fed1d7eb177b"u8),
                        ["entity_category"] = "diagnostic",
                        ["device_class"] = "timestamp",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8080, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.Uptime))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.StartupDate}",
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "5e9b5094-b028-492c-8be4-5b73139e2c57"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name: GetLocalizedString(SR.ID8081, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.Uptime))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.StartupDate}/get",
                        ["payload_press"] = string.Empty
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "select",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "733d9bf6-fd89-4ce1-bd71-d12a1c6a846e"u8),
                        ["icon"] = "mdi:water-boiler",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8109, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.WaterHeaterSetpointMode}/set",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.WaterHeaterSetpointMode}",
                        ["options"] = new JsonArray(
                        [
                            GetLocalizedString(SR.ID8082, culture),
                            GetLocalizedString(SR.ID8083, culture),
                            GetLocalizedString(SR.ID8084, culture)
                        ]),
                        ["value_template"] = $$$"""
                            {% set map = {
                              'automatic': '{{{GetLocalizedString(SR.ID8082, culture).Replace("'", "\\'")}}}',
                              'forced_on': '{{{GetLocalizedString(SR.ID8083, culture).Replace("'", "\\'")}}}',
                              'forced_off': '{{{GetLocalizedString(SR.ID8084, culture).Replace("'", "\\'")}}}'
                            } %}
                            {{ map[value] }}
                            """,
                        ["command_template"] = $$$"""
                            {% set map = {
                              '{{{GetLocalizedString(SR.ID8082, culture).Replace("'", "\\'")}}}': 'automatic',
                              '{{{GetLocalizedString(SR.ID8083, culture).Replace("'", "\\'")}}}': 'forced_on',
                              '{{{GetLocalizedString(SR.ID8084, culture).Replace("'", "\\'")}}}': 'forced_off'
                            } %}
                            {{ map[value] }}
                            """
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "binary_sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "344b6548-196d-42de-b627-22530cc28f07"u8),
                        ["device_class"] = "running",
                        ["icon"] = "mdi:fire",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8085, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.WaterHeaterState}",
                        ["payload_on"] = "heating",
                        ["payload_off"] = "idle"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "bb186d7c-f49d-4aa2-8770-a0fdcf9de123"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8086, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.WaterHeaterState}/get",
                        ["payload_press"] = string.Empty
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.WirelessBurglarAlarmState))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "2dd476d5-a35a-442a-a3f2-4c2621dcf375"u8),
                        ["device_class"] = "enum",
                        ["icon"] = "mdi:shield-home-outline",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8087, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.WirelessBurglarAlarmState))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.WirelessBurglarAlarmState}",
                        ["options"] = new JsonArray(
                        [
                            GetLocalizedString(SR.ID8088, culture),
                            GetLocalizedString(SR.ID8089, culture),
                            GetLocalizedString(SR.ID8090, culture),
                            GetLocalizedString(SR.ID8091, culture),
                            GetLocalizedString(SR.ID8092, culture),
                            GetLocalizedString(SR.ID8093, culture)
                        ]),
                        ["value_template"] = $$$"""
                            {% set map = {
                              'disarmed': '{{{GetLocalizedString(SR.ID8088, culture)}}}',
                              'armed': '{{{GetLocalizedString(SR.ID8089, culture)}}}',
                              'partially_armed': '{{{GetLocalizedString(SR.ID8090, culture)}}}',
                              'exit_delay_elapsed': '{{{GetLocalizedString(SR.ID8091, culture)}}}',
                              'triggered': '{{{GetLocalizedString(SR.ID8092, culture)}}}',
                              'event_detected': '{{{GetLocalizedString(SR.ID8093, culture)}}}'
                            } %}
                            {{ map[value] }}
                            """
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.ZigbeeBinding))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "binary_sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "ee8cc336-01cb-4485-a377-dc5603c64a13"u8),
                        ["entity_category"] = "diagnostic",
                        ["device_class"] = "running",
                        ["icon"] = "mdi:link-box",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8107, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeBinding))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeBinding}",
                        ["value_template"] = """
                            {% set map = {
                              'canceled': 'OFF',
                              'closed': 'OFF',
                              'opened': 'ON'
                            } %}
                            {{ map[value] }}
                            """
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "d04dc07d-b614-4e3e-aeac-ccaead40d919"u8),
                        ["entity_category"] = "config",
                        ["icon"] = "mdi:link",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8094, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeBinding))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeBinding}/set",
                        ["payload_press"] = "bind"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "55a25c45-cb7a-4b74-baa4-7f9b87465d1a"u8),
                        ["entity_category"] = "config",
                        ["icon"] = "mdi:link-off",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8095, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeBinding))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeBinding}/set",
                        ["payload_press"] = "unbind"
                    }));
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                {
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "binary_sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "da843c15-49d4-4a14-a7cb-4806783cd8a0"u8),
                        ["entity_category"] = "diagnostic",
                        ["device_class"] = "opening",
                        ["icon"] = "mdi:wifi-strength-lock-open-outline",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8108, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeNetwork}",
                        ["value_template"] = """
                            {% set map = {
                              'closed': 'OFF',
                              'created': 'ON',
                              'joint': 'OFF',
                              'left': 'OFF',
                              'opened': 'ON'
                            } %}
                            {{ map[value] }}
                            """
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "5ea35f24-9a6c-4d62-b000-85e6a9ef5380"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:sine-wave",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8096, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeChannel}",
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "abda41d7-1b4c-41cc-99b9-81b7bc5801d3"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:counter",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8105, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeDevicesCount}",
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "b7355e3d-0137-41e7-90fa-8b81b9530466"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:sine-wave",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8097, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeChannel}/get",
                        ["payload_press"] = string.Empty
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "6c550d13-cbd5-4a7a-b445-1c30e9b83c65"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:counter",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8106, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeDevicesCount}/get",
                        ["payload_press"] = string.Empty
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "f366a06c-6d7f-4741-a99a-490fedeabf9f"u8),
                        ["icon"] = "mdi:new-box",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8098, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeNetwork}/set",
                        ["payload_press"] = "create"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "1c56979a-3ad2-4b9b-9116-cd7321416b6a"u8),
                        ["icon"] = "mdi:download-network",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8099, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeNetwork}/set",
                        ["payload_press"] = "join"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "63096c5c-3fba-4ea7-a30d-3568b0680e18"u8),
                        ["icon"] = "mdi:upload-network",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8100, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeNetwork}/set",
                        ["payload_press"] = "leave",
                        ["enabled_by_default"] = false
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "09953b7d-0e18-4fc9-9b8c-2a11b52a15c5"u8),
                        ["icon"] = "mdi:lock-open",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8101, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeNetwork}/set",
                        ["payload_press"] = "open"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "7e344b36-199e-4a43-987f-59fccacc859b"u8),
                        ["icon"] = "mdi:lock",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8102, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeNetwork}/set",
                        ["payload_press"] = "close"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "f6a3d89f-a3a4-4776-a6b0-a0e3328183ee"u8),
                        ["icon"] = "mdi:eye-check",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8103, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeSupervision}/set",
                        ["payload_press"] = "enable"
                    }));

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "35f91e70-674d-4977-9475-ba231553051d"u8),
                        ["icon"] = "mdi:eye-remove",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8104, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                                .Where(static endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeSupervision}/set",
                        ["payload_press"] = "disable"
                    }));

                    // Add a "Discover devices" button to the gateway device that triggers
                    // a Zigbee network scan and auto-registers any new devices found.
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "c8b3a1d0-5e7f-4c2a-9b6d-3f8e1a2c4d5b"u8),
                        ["icon"] = "mdi:radar",
                        ["name"] = "Discover devices",
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/system/{OpenNettyMqttAttributes.DiscoveryScan}/set",
                        ["payload_press"] = "SCAN"
                    }));

                    // Add a sensor to display the last discovery scan result.
                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "a7f2e4b1-3c8d-4a5e-b9d0-6f1c2e3a4b5c"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:radar",
                        ["name"] = "Discovery scan status",
                        ["availability_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Availability}",
                        ["state_topic"] = $"{options.RootTopic}/system/{OpenNettyMqttAttributes.DiscoveryScan}",
                        ["value_template"] = "{{ value_json.status if value_json is mapping else value }}"
                    }));
                }
            }

            // Add a "Device name" text entity to every device, allowing users
            // to rename devices directly from the Home Assistant UI.
            {
                // Use the first endpoint's topic as the base for the device name command topic.
                var firstEndpoint = await _manager.FindEndpointsByDeviceAsync(device, cancellationToken)
                    .FirstOrDefaultAsync(cancellationToken);

                if (firstEndpoint is not null)
                {
                    var deviceNameTopic = firstEndpoint.GetStringSetting(OpenNettySettings.MqttTopic)
                        ?? firstEndpoint.Name.ToLowerInvariant();

                    var currentName = device.GetStringSetting(OpenNettySettings.HomeAssistantDeviceName)
                        ?? $"{Enum.GetName(device.Identity.Brand)} {device.Identity.Model} ({device.Identifier})";

                    components.Add(CreateEntityNode(new JsonObject
                    {
                        ["platform"] = "text",
                        ["unique_id"] = ComputeEntityUniqueId(firstEndpoint, "d4e5f6a7-b8c9-4d0e-a1f2-3b4c5d6e7f8a"u8),
                        ["entity_category"] = "config",
                        ["icon"] = "mdi:rename",
                        ["name"] = "Device name",
                        ["availability_topic"] = $"{options.RootTopic}/{deviceNameTopic}/{OpenNettyMqttAttributes.Availability}",
                        ["command_topic"] = $"{options.RootTopic}/{deviceNameTopic}/{OpenNettyMqttAttributes.DeviceName}/set",
                        ["state_topic"] = $"{options.RootTopic}/{deviceNameTopic}/{OpenNettyMqttAttributes.DeviceName}",
                        ["min"] = 1,
                        ["max"] = 100
                    }));

                    // Publish the current device name so the text entity shows the current value.
                    await client.EnqueueAsync(new MqttApplicationMessageBuilder()
                        .WithPayload(currentName)
                        .WithPayloadFormatIndicator(MqttPayloadFormatIndicator.CharacterData)
                        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                        .WithRetainFlag()
                        .WithTopic($"{options.RootTopic}/{deviceNameTopic}/{OpenNettyMqttAttributes.DeviceName}")
                        .Build());
                }
            }

            if (components.Count is not 0)
            {
                await client.EnqueueAsync(new MqttApplicationMessageBuilder()
                    .WithContentType(MediaTypeNames.Application.Json)
                    .WithPayload(configuration.ToJsonString())
                    .WithPayloadFormatIndicator(MqttPayloadFormatIndicator.CharacterData)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                    .WithRetainFlag()
                    .WithTopic(new StringBuilder(options.HomeAssistantDiscoveryRootTopic)
                        .Append('/')
                        .Append("device")
                        .Append('/')
                        .Append("opennetty-").Append(Enum.GetName(device.Definition.Protocol)!.ToLowerInvariant())
                        .Append('/')
                        .Append([.. device.Identifier.ToString().Where(char.IsAsciiHexDigit)])
                        .Append('/')
                        .Append("config")
                        .ToString())
                    .Build());
            }
        }

        static KeyValuePair<string, JsonNode?> CreateEntityNode(JsonObject component)
        {
            var identifier = (string?) component["unique_id"]?.AsValue();
            if (string.IsNullOrEmpty(identifier))
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0119));
            }

            return KeyValuePair.Create<string, JsonNode?>($"entity_{identifier}", component);
        }

        static JsonObject CreateDeviceNode(OpenNettyDevice device, CultureInfo culture)
        {
            var node = new JsonObject
            {
                ["identifiers"] = new JsonArray([ComputeDeviceUniqueId(device)]),
                ["manufacturer"] = Enum.GetName(device.Identity.Brand),
                ["model_id"] = device.Identity.Model,
                ["serial_number"] = device.Identifier.ToString(),
                ["name"] = device.GetStringSetting(OpenNettySettings.HomeAssistantDeviceName) is { Length: > 0 } customName
                    ? customName
                    : $"{Enum.GetName(device.Identity.Brand)} {device.Identity.Model} ({device.Identifier})"
            };

            var description = device.Identity.GetDescription(culture);
            if (!string.IsNullOrEmpty(description))
            {
                node["model"] = description;
            }

            if (device.Identifier.Type is OpenNettyDeviceIdentifierType.MacAddress)
            {
                node["connections"] = new JsonArray([new JsonArray(["mac", OpenNettyDeviceIdentifier.ToMacAddress(device.Identifier)])]);
            }

            if (device.Gateway is OpenNettyGateway gateway)
            {
                node["via_device"] = ComputeDeviceUniqueId(gateway.Device);
            }

            if (device.GetStringSetting(OpenNettySettings.HomeAssistantSuggestedArea) is { Length: > 0 } area)
            {
                node["suggested_area"] = area;
            }

            return node;
        }

        static string ComputeDeviceUniqueId(OpenNettyDevice device)
        {
            var hash = new XxHash128();
            hash.Append(MemoryMarshal.AsBytes<char>(Enum.GetName(device.Definition.Protocol)));
            hash.Append(MemoryMarshal.AsBytes<char>(Enum.GetName(device.Identifier.Type)));
            hash.Append(MemoryMarshal.AsBytes<char>(device.Identifier.ToString()));

            return Base64Url.EncodeToString(hash.GetCurrentHash());
        }

        static string ComputeEntityUniqueId(OpenNettyEndpoint endpoint, ReadOnlySpan<byte> discriminator)
        {
            var hash = new XxHash128();
            hash.Append(MemoryMarshal.AsBytes<char>(endpoint.Name));
            hash.Append(discriminator);

            return Base64Url.EncodeToString(hash.GetCurrentHash());
        }

        static string ComputeEntityName(string name, OpenNettyEndpoint endpoint, CultureInfo culture, int count)
        {
            if (count is < 2)
            {
                return name;
            }

            if (endpoint.Unit is not null)
            {
                var description = endpoint.Unit.Definition.GetDescription(culture);
                if (!string.IsNullOrEmpty(description))
                {
                    var info = new StringInfo(description);

                    return culture.TextInfo.IsRightToLeft ?
                        $"{info.SubstringByTextElements(0, 1).ToLower(culture) + info.SubstringByTextElements(1)} {name}" :
                        $"{name} {info.SubstringByTextElements(0, 1).ToLower(culture) + info.SubstringByTextElements(1)}";
                }
            }

            return name;
        }

        static string GetLocalizedString(string name, CultureInfo culture) => SR.ResourceManager.GetString(name, culture)!;

        static bool SupportsCoverEntity(OpenNettyEndpoint endpoint)
        {
            // If the endpoint doesn't support basic or advanced shutter commands, a Home Assistant cover entity cannot be created.
            //
            // Note: endpoints that can't report their state (e.g radio Nitoo devices) can still be used with a cover entity:
            // in that case, Home Assistant will automatically use the optimistic mode to dynamically update the current state.
            if (!endpoint.HasCapability(OpenNettyCapabilities.BasicShutterControl) &&
                !endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterControl))
            {
                return false;
            }

            // If the endpoint also supports lighting commands, ensure the function type
            // associated with the endpoint is appropriate for the requested operation.
            if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchControl) ||
                endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingControl) ||
                endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingControl))
            {
                var type = endpoint.GetStringSetting(OpenNettySettings.FunctionType);
                if (string.IsNullOrEmpty(type))
                {
                    throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
                }

                return type is OpenNettySettings.FunctionTypes.AutomationActuator;
            }

            return true;
        }

        static bool SupportsLightOrSwitchEntity(OpenNettyEndpoint endpoint)
        {
            // If the endpoint doesn't support ON/OFF commands, a Home Assistant light entity cannot be created.
            //
            // Note: endpoints that can't report their state (e.g radio Nitoo devices) can still be used with a light entity:
            // in that case, Home Assistant will automatically use the optimistic mode to dynamically update the current state.
            if (!endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchControl))
            {
                return false;
            }

            // If the endpoint also supports automation commands, ensure the function
            // type associated with the endpoint is appropriate for a light entity.
            if (endpoint.HasCapability(OpenNettyCapabilities.BasicShutterControl) ||
                endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterControl))
            {
                var type = endpoint.GetStringSetting(OpenNettySettings.FunctionType);
                if (string.IsNullOrEmpty(type))
                {
                    throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
                }

                return type is OpenNettySettings.FunctionTypes.LightActuator;
            }

            return true;
        }
    }

    /// <summary>
    /// Performs a Zigbee network discovery scan by querying the gateway for all
    /// known device identifiers, creating any new devices found, and publishing
    /// Home Assistant MQTT Discovery payloads for them.
    /// </summary>
    private async Task PerformDiscoveryScanAsync(IManagedMqttClient client, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Zigbee discovery scan...");

        var options = _openNettyOptions.CurrentValue;

        await client.EnqueueAsync(new MqttApplicationMessageBuilder()
            .WithPayload("scanning")
            .WithPayloadFormatIndicator(MqttPayloadFormatIndicator.CharacterData)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
            .WithTopic($"{_options.CurrentValue.RootTopic}/system/{OpenNettyMqttAttributes.DiscoveryScan}")
            .Build());

        var discoveredCount = 0;

        try
        {
            OpenNettyGateway? zigbeeGateway = null;

            await foreach (var gateway in _manager.EnumerateGatewaysAsync(cancellationToken))
            {
                if (gateway.Protocol is OpenNettyProtocol.Zigbee)
                {
                    zigbeeGateway = gateway;
                    break;
                }
            }

            if (zigbeeGateway is null)
            {
                _logger.LogWarning("No Zigbee gateway found. Discovery scan aborted.");

                await client.EnqueueAsync(new MqttApplicationMessageBuilder()
                    .WithPayload("error:no_gateway")
                    .WithPayloadFormatIndicator(MqttPayloadFormatIndicator.CharacterData)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                    .WithTopic($"{_options.CurrentValue.RootTopic}/system/{OpenNettyMqttAttributes.DiscoveryScan}")
                    .Build());

                return;
            }

            OpenNettyEndpoint? gatewayEndpoint = null;

            await foreach (var ep in _manager.FindEndpointsByGatewayAsync(zigbeeGateway, cancellationToken))
            {
                if (ep.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                {
                    gatewayEndpoint = ep;
                    break;
                }
            }

            if (gatewayEndpoint is null)
            {
                _logger.LogWarning("No Zigbee network management endpoint found. Discovery scan aborted.");

                await client.EnqueueAsync(new MqttApplicationMessageBuilder()
                    .WithPayload("error:no_endpoint")
                    .WithPayloadFormatIndicator(MqttPayloadFormatIndicator.CharacterData)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                    .WithTopic($"{_options.CurrentValue.RootTopic}/system/{OpenNettyMqttAttributes.DiscoveryScan}")
                    .Build());

                return;
            }

            _logger.LogInformation("Sending Zigbee network scan command...");
            await _service.ExecuteCommandAsync(
                protocol: OpenNettyProtocol.Zigbee,
                command: OpenNettyCommands.Management.ScanZigbeeNetwork,
                address: gatewayEndpoint.Address,
                medium: OpenNettyMedium.Radio,
                gateway: zigbeeGateway,
                cancellationToken: cancellationToken);

            var deviceCount = await _controller.CountZigbeeDevicesAsync(gatewayEndpoint, cancellationToken);
            _logger.LogInformation("Gateway reports {Count} registered Zigbee device(s).", deviceCount);

            // Aggregate device types and units across all responses
            // Key: hexId, Value: (Type, MaxUnit)
            var discoveredDevices = new Dictionary<string, (string Type, byte MaxUnit)>();

            for (var index = 0; index < deviceCount; index++)
            {
                try
                {
                    var result = await QueryProductInfoAsync(zigbeeGateway, index, cancellationToken);
                    if (result is null)
                    {
                        _logger.LogDebug("No response for product index {Index}.", index);
                        continue;
                    }

                    var (address, values) = result.Value;

                    string? hexId = null;
                    byte unit = 0;

                    if (address is not null)
                    {
                        var zigbeeAddr = OpenNettyAddress.ToZigbeeAddress(address.Value);
                        if (zigbeeAddr.Identifier is not 0)
                        {
                            hexId = zigbeeAddr.Identifier.ToString("X8", CultureInfo.InvariantCulture);
                            unit = zigbeeAddr.Unit;
                        }
                    }

                    if (hexId is null && values.Length > 0 &&
                        uint.TryParse(values[0], CultureInfo.InvariantCulture, out var decimalId) && decimalId != 0)
                    {
                        hexId = decimalId.ToString("X8", CultureInfo.InvariantCulture);
                    }

                    if (hexId is not null)
                    {
                        // values[1] holds the device type marker (e.g. 513 for Shutter, 256 for Switch)
                        var type = values.Length > 1 ? values[1] : string.Empty;

                        if (discoveredDevices.TryGetValue(hexId, out var existing))
                        {
                            discoveredDevices[hexId] = (
                                string.IsNullOrEmpty(type) ? existing.Type : type, 
                                Math.Max(unit, existing.MaxUnit)
                            );
                        }
                        else
                        {
                            discoveredDevices[hexId] = (type, unit);
                        }

                        _logger.LogDebug("Product index {Index}: device identifier {Identifier}, unit {Unit}, type {Type}.", index, hexId, unit, type);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not retrieve product info for index {Index}, skipping.", index);
                }
            }

            _logger.LogInformation("Discovery scan found {Count} unique device identifier(s).", discoveredDevices.Count);

            foreach (var kvp in discoveredDevices)
            {
                var hexId = kvp.Key;
                var type = kvp.Value.Type;
                var maxUnit = kvp.Value.MaxUnit;

                var identifier = OpenNettyDeviceIdentifier.FromZigbeeSerialNumber(hexId);

                // Skip the gateway device itself.
                if (zigbeeGateway.Device.Identifier == identifier)
                {
                    continue;
                }

                // Default to 1-gang switch (67233)
                var model = "67233"; 

                // Type 513 corresponds to a Shutter actuator
                if (type == "513")
                {
                    model = "67277"; 
                }
                // Type 256 with multiple units corresponds to a 2-gang switch
                else if (type == "256" && maxUnit >= 2)
                {
                    model = "67234";
                }

                var definition = OpenNettyDevices.GetDeviceByModel(OpenNettyBrand.Legrand, model);
                if (definition is null)
                {
                    _logger.LogWarning("Definition not found for model {Model}. Skipping device {Identifier}.", model, hexId);
                    continue;
                }

                var identity = definition.Identities.FirstOrDefault();
                if (identity.Model is null)
                {
                    continue;
                }

                // Prepare decimal device ID for MQTT display name
                var decimalId = uint.Parse(hexId, NumberStyles.HexNumber, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                var defaultName = $"{identity.Brand} {identity.Model} ({decimalId})";

                var existingDevice = options.Devices.Find(device => device.Identifier == identifier);
                
                // Existing Device Update Logic
                if (existingDevice is not null)
                {
                    if (existingDevice.Identity.Model != model)
                    {
                        _logger.LogInformation("Updating existing device {Identifier} from model {OldModel} to {NewModel}.", hexId, existingDevice.Identity.Model, model);
                        
                        var updatedUnits = definition.Units.Select(unitDef => 
                            existingDevice.Units.FirstOrDefault(u => u.Definition.Id == unitDef.Id) ?? new OpenNettyUnit
                            {
                                Definition = unitDef,
                                Scenarios = [],
                                Settings = ImmutableDictionary.Create<OpenNettySetting, string>()
                            }
                        );

                        var updatedDevice = new OpenNettyDevice
                        {
                            Definition = definition,
                            Gateway = existingDevice.Gateway,
                            Identifier = existingDevice.Identifier,
                            Identity = identity,
                            Settings = existingDevice.Settings,
                            Units = [.. updatedUnits]
                        };

                        var deviceIndex = options.Devices.IndexOf(existingDevice);
                        if (deviceIndex >= 0)
                        {
                            options.Devices[deviceIndex] = updatedDevice;
                        }

                        // Relink existing endpoints to the updated device instance
                        for (var i = 0; i < options.Endpoints.Count; i++)
                        {
                            if (options.Endpoints[i].Device == existingDevice)
                            {
                                options.Endpoints[i] = new OpenNettyEndpoint
                                {
                                    Address = options.Endpoints[i].Address,
                                    Capabilities = options.Endpoints[i].Capabilities,
                                    Description = options.Endpoints[i].Description,
                                    Device = updatedDevice,
                                    Gateway = options.Endpoints[i].Gateway,
                                    Medium = options.Endpoints[i].Medium,
                                    Name = options.Endpoints[i].Name,
                                    Protocol = options.Endpoints[i].Protocol,
                                    Settings = options.Endpoints[i].Settings,
                                    Unit = options.Endpoints[i].Unit is not null 
                                        ? updatedDevice.Units.FirstOrDefault(u => u.Definition.Id == options.Endpoints[i].Unit!.Definition.Id) 
                                        : null
                                };
                            }
                        }

                        // Add any missing new endpoints (e.g. upgrading from 1-gang to 2-gang switch)
                        foreach (var unitDef in definition.Units)
                        {
                            if (!options.Endpoints.Exists(e => e.Device == updatedDevice && e.Unit?.Definition.Id == unitDef.Id))
                            {
                                options.Endpoints.Add(new OpenNettyEndpoint
                                {
                                    Address = OpenNettyAddress.FromZigbeeAddress(identifier, unit: unitDef.Id),
                                    Capabilities = ImmutableHashSet.Create<OpenNettyCapability>(),
                                    Device = updatedDevice,
                                    Gateway = zigbeeGateway,
                                    Medium = definition.Medium,
                                    Name = $"Zigbee/{hexId}/{unitDef.Id}",
                                    Protocol = OpenNettyProtocol.Zigbee,
                                    Settings = ImmutableDictionary.Create<OpenNettySetting, string>(),
                                    Unit = updatedDevice.Units.FirstOrDefault(u => u.Definition.Id == unitDef.Id)
                                });
                            }
                        }

                        UpdateDeviceModelInXml(identifier, model);
                        discoveredCount++;
                    }
                    continue;
                }

                // New Device Creation Logic
                var newDevice = new OpenNettyDevice
                {
                    Definition = definition,
                    Gateway = zigbeeGateway,
                    Identifier = identifier,
                    Identity = identity,
                    Settings = ImmutableDictionary.Create<OpenNettySetting, string>().Add(OpenNettySettings.HomeAssistantDeviceName, defaultName),
                    Units = [.. definition.Units.Select(static unitDef => new OpenNettyUnit
                    {
                        Definition = unitDef,
                        Scenarios = [],
                        Settings = ImmutableDictionary.Create<OpenNettySetting, string>()
                    })]
                };

                options.Devices.Add(newDevice);

                options.Endpoints.Add(new OpenNettyEndpoint
                {
                    Address = OpenNettyAddress.FromZigbeeAddress(identifier, unit: 0),
                    Capabilities = ImmutableHashSet.Create<OpenNettyCapability>(),
                    Device = newDevice,
                    Gateway = zigbeeGateway,
                    Medium = definition.Medium,
                    Name = $"Zigbee/{hexId}",
                    Protocol = OpenNettyProtocol.Zigbee,
                    Settings = ImmutableDictionary.Create<OpenNettySetting, string>()
                });

                foreach (var unitDef in definition.Units)
                {
                    var unit = newDevice.Units.SingleOrDefault(u => u.Definition == unitDef) ?? new OpenNettyUnit
                    {
                        Definition = unitDef,
                        Scenarios = [],
                        Settings = ImmutableDictionary.Create<OpenNettySetting, string>()
                    };

                    options.Endpoints.Add(new OpenNettyEndpoint
                    {
                        Address = OpenNettyAddress.FromZigbeeAddress(identifier, unit: unitDef.Id),
                        Capabilities = ImmutableHashSet.Create<OpenNettyCapability>(),
                        Device = newDevice,
                        Gateway = zigbeeGateway,
                        Medium = definition.Medium,
                        Name = $"Zigbee/{hexId}/{unitDef.Id}",
                        Protocol = OpenNettyProtocol.Zigbee,
                        Settings = ImmutableDictionary.Create<OpenNettySetting, string>(),
                        Unit = unit
                    });
                }

                PersistNewDeviceToXml(newDevice, defaultName);
                discoveredCount++;

                _logger.LogInformation("Discovered and registered new Zigbee device: {Brand} {Model} ({Identifier}) as {Name}.",
                    identity.Brand, identity.Model, hexId, defaultName);
            }

            if (discoveredCount > 0)
            {
                await AnnounceEndpointsAsync(client, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "An error occurred during the Zigbee discovery scan.");
        }

        await client.EnqueueAsync(new MqttApplicationMessageBuilder()
            .WithPayload(new JsonObject
            {
                ["status"] = "complete",
                ["discovered"] = discoveredCount
            }.ToJsonString())
            .WithPayloadFormatIndicator(MqttPayloadFormatIndicator.CharacterData)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
            .WithTopic($"{_options.CurrentValue.RootTopic}/system/{OpenNettyMqttAttributes.DiscoveryScan}")
            .Build());

        _logger.LogInformation("Zigbee discovery scan complete. Interacted with {Count} device(s).", discoveredCount);
    }
    /// <summary>
    /// Queries the gateway for product info at the specified index using the ProductInfo dimension (WHO=13, DIM=66).
    /// The request frame format is *#13**66*index## which is specific to Zigbee USB gateways.
    /// </summary>
    private async Task<(OpenNettyAddress? Address, ImmutableArray<string> Values)?> QueryProductInfoAsync(
        OpenNettyGateway gateway, int productIndex, CancellationToken cancellationToken)
    {
        // Build the raw ProductInfo request frame: *#13**66*<index>##
        // This uses the DimensionRead format with the product index as a value,
        // matching the frame format used by OpenWebNet USB Zigbee gateways.
        var message = OpenNettyMessage.CreateFromFrame(
            OpenNettyProtocol.Zigbee,
            $"*#13**66*{productIndex.ToString(CultureInfo.InvariantCulture)}##");

        // Subscribe to the notification pipeline for the DimensionRead response before sending,
        // to avoid missing the response due to a race condition.
        var notifications = _pipeline.Where(notification => notification.Gateway == gateway)
            .SelectMany(notification => notification switch
            {
                OpenNettyNotifications.MessageReceived {
                    Session: { Protocol: OpenNettyProtocol.Zigbee, Type: OpenNettySessionType.Generic },
                    Message: { Type     : OpenNettyMessageType.DimensionRead,
                               Dimension: { Value: "66" } } } received
                        => AsyncObservable.Return(received.Message),

                _ => AsyncObservable.Empty<OpenNettyMessage>()
            })
            .Replay();

        await using var connection = await notifications.ConnectAsync();

        // Send the ProductInfo request and wait for acknowledgement.
        await _service.SendMessageAsync(message, gateway, cancellationToken: cancellationToken);

        // Wait for the DimensionRead response with a timeout.
        var response = await notifications
            .FirstOrDefault()
            .Timeout(TimeSpan.FromSeconds(5), AsyncObservable.Return<OpenNettyMessage>(null!))
            .RunAsync(cancellationToken);

        if (response is null)
        {
            return null;
        }

        return (response.Address, response.Values);
    }

    /// <summary>
    /// Persists a newly discovered device to the OpenNettyConfiguration.xml file.
    /// </summary>
    private void PersistNewDeviceToXml(OpenNettyDevice device, string defaultName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "OpenNettyConfiguration.xml");
            if (!File.Exists(path))
            {
                return;
            }

            var document = XDocument.Load(path);
            if (document.Root is null)
            {
                return;
            }

            var serialNumber = device.Identifier.ToString();
            foreach (var existingElement in document.Root.Elements("Device"))
            {
                var existingSn = (string?) existingElement.Attribute("SerialNumber");
                var existingMac = (string?) existingElement.Attribute("MacAddress");

                if ((!string.IsNullOrEmpty(existingSn) && string.Equals(existingSn, serialNumber, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(existingMac) && string.Equals(existingMac, serialNumber, StringComparison.OrdinalIgnoreCase)))
                {
                    return; 
                }
            }

            var deviceElement = new XElement("Device",
                new XAttribute("Brand", Enum.GetName(device.Identity.Brand)!),
                new XAttribute("Model", device.Identity.Model),
                new XAttribute("SerialNumber", serialNumber),
                new XAttribute("Name", defaultName));

            document.Root.Add(deviceElement);
            document.Save(path);

            _logger.LogInformation("Persisted new device {Brand} {Model} ({SerialNumber}) to OpenNettyConfiguration.xml.",
                device.Identity.Brand, device.Identity.Model, serialNumber);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "An error occurred while persisting a new device to the XML configuration file.");
        }
    }

    /// <summary>
    /// Updates the model of an existing device in the OpenNettyConfiguration.xml file.
    /// </summary>
    private void UpdateDeviceModelInXml(OpenNettyDeviceIdentifier identifier, string newModel)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "OpenNettyConfiguration.xml");
            if (!File.Exists(path))
            {
                return;
            }

            var document = XDocument.Load(path);
            if (document.Root is null)
            {
                return;
            }

            var targetId = identifier.ToString();
            foreach (var element in document.Root.Elements("Device"))
            {
                var sn = (string?) element.Attribute("SerialNumber");
                var mac = (string?) element.Attribute("MacAddress");

                if ((!string.IsNullOrEmpty(sn) && string.Equals(sn, targetId, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(mac) && string.Equals(mac, targetId, StringComparison.OrdinalIgnoreCase)))
                {
                    element.SetAttributeValue("Model", newModel);
                    break;
                }
            }

            document.Save(path);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "An error occurred while updating the device model in the XML configuration file.");
        }
    }


    /// <summary>
    /// Renames a device by updating its in-memory settings and persisting
    /// the change to the XML configuration file if it exists.
    /// </summary>
    private void RenameDevice(OpenNettyDevice device, string name)
    {
        var options = _openNettyOptions.CurrentValue;

        // Create a new device instance with the updated name setting.
        var updatedDevice = new OpenNettyDevice
        {
            Definition = device.Definition,
            Gateway = device.Gateway,
            Identifier = device.Identifier,
            Identity = device.Identity,
            Settings = device.Settings.SetItem(OpenNettySettings.HomeAssistantDeviceName, name),
            Units = device.Units
        };

        // Replace the device in the options list.
        var deviceIndex = options.Devices.IndexOf(device);
        if (deviceIndex >= 0)
        {
            options.Devices[deviceIndex] = updatedDevice;
        }

        // Update all endpoints that reference this device.
        for (var i = 0; i < options.Endpoints.Count; i++)
        {
            if (options.Endpoints[i].Device == device)
            {
                options.Endpoints[i] = new OpenNettyEndpoint
                {
                    Address = options.Endpoints[i].Address,
                    Capabilities = options.Endpoints[i].Capabilities,
                    Description = options.Endpoints[i].Description,
                    Device = updatedDevice,
                    Gateway = options.Endpoints[i].Gateway,
                    Medium = options.Endpoints[i].Medium,
                    Name = options.Endpoints[i].Name,
                    Protocol = options.Endpoints[i].Protocol,
                    Settings = options.Endpoints[i].Settings,
                    Unit = options.Endpoints[i].Unit
                };
            }
        }

        PersistDeviceNameToXml(device.Identifier, name);
    }

    /// <summary>
    /// Persists a device name change to the OpenNettyConfiguration.xml file.
    /// </summary>
    private void PersistDeviceNameToXml(OpenNettyDeviceIdentifier identifier, string name)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "OpenNettyConfiguration.xml");
            if (!File.Exists(path))
            {
                return;
            }

            var document = XDocument.Load(path);
            if (document.Root is null)
            {
                return;
            }

            foreach (var element in document.Root.Elements("Device"))
            {
                var serialNumber = (string?) element.Attribute("SerialNumber");
                var macAddress = (string?) element.Attribute("MacAddress");

                if ((!string.IsNullOrEmpty(serialNumber) && string.Equals(serialNumber, identifier.ToString(), StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(macAddress) && string.Equals(macAddress, identifier.ToString(), StringComparison.OrdinalIgnoreCase)))
                {
                    element.SetAttributeValue("Name", name);
                    break;
                }
            }

            document.Save(path);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "An error occurred while persisting the device name to the XML configuration file.");
        }
    }

    static (MqttApplicationMessage Message, string? Name, string? Attribute, OpenNettyMqttOperation? Operation) ExtractParameters(MqttApplicationMessage message)
        => message.Topic.Split('/', StringSplitOptions.RemoveEmptyEntries) switch
        {
            [_, .. string[] topics, string attribute, "get"]
                => (message, string.Join('/', topics), attribute, OpenNettyMqttOperation.Get),

            [_, .. string[] topics, string attribute, "set"]
                => (message, string.Join('/', topics), attribute, OpenNettyMqttOperation.Set),

            _ => (message, null, null, null)
        };
}
