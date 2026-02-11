/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Buffers.Text;
using System.Globalization;
using System.IO.Hashing;
using System.Net.Mime;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
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

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyMqttWorker"/> class.
    /// </summary>
    /// <param name="controller">The OpenNetty controller.</param>
    /// <param name="logger">The OpenNetty logger.</param>
    /// <param name="manager">The OpenNetty manager.</param>
    /// <param name="options">The OpenNetty MQTT options.</param>
    public OpenNettyMqttWorker(
        OpenNettyController controller,
        ILogger<OpenNettyMqttWorker> logger,
        OpenNettyManager manager,
        IOptionsMonitor<OpenNettyMqttOptions> options)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public async Task ProcessMessagesAsync(
        IManagedMqttClient client,
        ChannelReader<MqttApplicationMessage> reader,
        CancellationToken cancellationToken)
    {
        await using var subscription = await AsyncObservable.Create<MqttApplicationMessage>(observer =>
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
            }))
            .SelectMany(async message =>
            {
                var (name, attribute, operation) = ExtractParameters(message);

                if (string.IsNullOrEmpty(name) ||
                    string.IsNullOrEmpty(attribute) ||
                    operation is not (OpenNettyMqttOperation.Get or OpenNettyMqttOperation.Set) ||
                    await _manager.FindEndpointAsync(Matches) is not OpenNettyEndpoint endpoint)
                {
                    return AsyncObservable.Empty<(MqttApplicationMessage Message, OpenNettyEndpoint Endpoint, string Attribute, OpenNettyMqttOperation Operation)>();
                }

                return AsyncObservable.Return((Message: message, Endpoint: endpoint, Attribute: attribute, Operation: operation.Value));

                bool Matches(OpenNettyEndpoint endpoint) => string.Equals(
                    endpoint.GetStringSetting(OpenNettySettings.MqttTopic) ?? endpoint.Name.ToLowerInvariant(),
                    name, StringComparison.Ordinal);
            })
            .GroupBy(static arguments => arguments.Endpoint.Name)
            .Do(async group => await group
                .ObserveOn(TaskPoolAsyncScheduler.Default)
                .Do(async arguments =>
                {
                    var (message, endpoint, attribute, operation) = arguments;

                    try
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
                                _ = await _controller.EnumerateBrightnessAsync(endpoint).ToListAsync();
                                break;
                            }

                            case OpenNettyMqttAttributes.Brightness when operation is OpenNettyMqttOperation.Set:
                            {
                                if (!byte.TryParse(message.PayloadSegment, CultureInfo.InvariantCulture, out var level))
                                {
                                    throw new InvalidDataException(SR.GetResourceString(SR.ID0068));
                                }

                                await _controller.SetBrightnessAsync(endpoint, level);
                                break;
                            }

                            case OpenNettyMqttAttributes.FirmwareVersion when operation is OpenNettyMqttOperation.Get:
                            {
                                _ = await _controller.GetFirmwareVersionAsync(endpoint);
                                break;
                            }

                            case OpenNettyMqttAttributes.HardwareVersion when operation is OpenNettyMqttOperation.Get:
                            {
                                _ = await _controller.GetHardwareVersionAsync(endpoint);
                                break;
                            }

                            case OpenNettyMqttAttributes.MacAddress when operation is OpenNettyMqttOperation.Get:
                            {
                                _ = await _controller.GetMacAddressAsync(endpoint);
                                break;
                            }

                            case OpenNettyMqttAttributes.PilotWireDerogationMode when operation is OpenNettyMqttOperation.Get:
                            case OpenNettyMqttAttributes.PilotWireSetpointMode   when operation is OpenNettyMqttOperation.Get:
                            case OpenNettyMqttAttributes.PilotWireShutdownMode   when operation is OpenNettyMqttOperation.Get:
                            {
                                _ = await _controller.GetPilotWireConfigurationAsync(endpoint);
                                break;
                            }

                            case OpenNettyMqttAttributes.PilotWireDerogationMode when operation is OpenNettyMqttOperation.Set:
                            {
                                switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                                {
                                    case "none":
                                        await _controller.CancelPilotWireDerogationModeAsync(endpoint);
                                        break;

                                    case "comfort":
                                        await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.Comfort,
                                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None);
                                        break;

                                    case "comfort:4h":
                                        await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.Comfort,
                                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours);
                                        break;

                                    case "comfort:8h":
                                        await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.Comfort,
                                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours);
                                        break;

                                    case "comfort-1":
                                        await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne,
                                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None);
                                        break;

                                    case "comfort-1:4h":
                                        await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne,
                                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours);
                                        break;

                                    case "comfort-1:8h":
                                        await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne,
                                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours);
                                        break;

                                    case "comfort-2":
                                        await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo,
                                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None);
                                        break;

                                    case "comfort-2:4h":
                                        await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo,
                                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours);
                                        break;

                                    case "comfort-2:8h":
                                        await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo,
                                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours);
                                        break;

                                    case "eco":
                                        await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.Eco,
                                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None);
                                        break;

                                    case "eco:4h":
                                        await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.Eco,
                                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours);
                                        break;

                                    case "eco:8h":
                                        await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.Eco,
                                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours);
                                        break;

                                    case "frost_protection":
                                        await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection,
                                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None);
                                        break;

                                    case "frost_protection:4h":
                                        await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection,
                                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours);
                                        break;

                                    case "frost_protection:8h":
                                        await _controller.SetPilotWireDerogationModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection,
                                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours);
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
                                            OpenNettyModels.TemperatureControl.PilotWireMode.Comfort);
                                        break;

                                    case "comfort-1":
                                        await _controller.SetPilotWireSetpointModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne);
                                        break;

                                    case "comfort-2":
                                        await _controller.SetPilotWireSetpointModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo);
                                        break;

                                    case "eco":
                                        await _controller.SetPilotWireSetpointModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.Eco);
                                        break;

                                    case "frost_protection":
                                        await _controller.SetPilotWireSetpointModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection);
                                        break;
                                }
                                break;
                            }

                            case OpenNettyMqttAttributes.PilotWireShutdownMode when operation is OpenNettyMqttOperation.Set:
                            {
                                switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                                {
                                    case "on":
                                        await _controller.ActivatePilotWireShutdownModeAsync(endpoint);
                                        break;

                                    case "off":
                                        await _controller.CancelPilotWireShutdownModeAsync(endpoint);
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
                                        await _controller.DispatchActionScenarioAsync(endpoint, OpenNettyModels.ScenariosPlus.ActionScenarioType.Action);
                                        break;

                                    case "dimming":
                                    {
                                        await _controller.DispatchDimmingScenarioAsync(endpoint,
                                            (short?) parameters?["dimming_step"] ?? throw new InvalidDataException(SR.GetResourceString(SR.ID0068)));
                                        break;
                                    }

                                    case "end_of_extended_pressure":
                                        await _controller.DispatchPressureScenarioPlusAsync(endpoint,
                                            OpenNettyModels.ScenariosPlus.PressureScenarioType.EndOfExtendedPressure,
                                            (byte?) parameters?["button"]);
                                        break;

                                    case "extended_pressure" when ((string?) parameters?["scenario_type"]) is "evolved":
                                        await _controller.DispatchPressureScenarioAsync(endpoint,
                                            OpenNettyModels.Scenarios.PressureScenarioType.ExtendedPressure,
                                            (byte?) parameters?["button"] ?? throw new InvalidDataException(SR.GetResourceString(SR.ID0068)));
                                        break;

                                    case "extended_pressure" when ((string?) parameters?["scenario_type"]) is "plus":
                                        await _controller.DispatchPressureScenarioPlusAsync(endpoint,
                                            OpenNettyModels.ScenariosPlus.PressureScenarioType.ExtendedPressure,
                                            (byte?) parameters?["button"]);
                                        break;

                                    case "pressure":
                                        await _controller.DispatchPressureScenarioAsync(endpoint,
                                            OpenNettyModels.Scenarios.PressureScenarioType.Pressure,
                                            (byte?) parameters?["button"] ?? throw new InvalidDataException(SR.GetResourceString(SR.ID0068)));
                                        break;

                                    case "progressive_action":
                                    {
                                        if (!TimeSpan.TryParse((string?) parameters?["duration"], CultureInfo.InvariantCulture, out var duration))
                                        {
                                            throw new InvalidDataException(SR.GetResourceString(SR.ID0068));
                                        }

                                        await _controller.DispatchProgressiveScenarioAsync(endpoint, duration);
                                        break;
                                    }

                                    case "release_after_short_pressure":
                                        await _controller.DispatchPressureScenarioAsync(endpoint,
                                            OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterShortPressure,
                                            (byte?) parameters?["button"] ?? throw new InvalidDataException(SR.GetResourceString(SR.ID0068)));
                                        break;

                                    case "release_after_extended_pressure":
                                        await _controller.DispatchPressureScenarioAsync(endpoint,
                                            OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterExtendedPressure,
                                            (byte?) parameters?["button"] ?? throw new InvalidDataException(SR.GetResourceString(SR.ID0068)));
                                        break;

                                    case "short_pressure":
                                        await _controller.DispatchPressureScenarioPlusAsync(endpoint,
                                            OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure,
                                            (byte?) parameters?["button"] ?? throw new InvalidDataException(SR.GetResourceString(SR.ID0068)));
                                        break;

                                    case "shutter_down":
                                        await _controller.DispatchStopUpDownScenarioAsync(endpoint, OpenNettyModels.Automation.StopUpDownScenarioType.Down);
                                        break;

                                    case "shutter_stop":
                                        await _controller.DispatchStopUpDownScenarioAsync(endpoint, OpenNettyModels.Automation.StopUpDownScenarioType.Stop);
                                        break;

                                    case "shutter_up":
                                        await _controller.DispatchStopUpDownScenarioAsync(endpoint, OpenNettyModels.Automation.StopUpDownScenarioType.Up);
                                        break;

                                    case "start_of_extended_pressure":
                                        await _controller.DispatchPressureScenarioPlusAsync(endpoint,
                                            OpenNettyModels.ScenariosPlus.PressureScenarioType.StartOfExtendedPressure,
                                            (byte?) parameters?["button"]);
                                        break;

                                    case "stop_action":
                                        await _controller.DispatchActionScenarioAsync(endpoint, OpenNettyModels.ScenariosPlus.ActionScenarioType.StopAction);
                                        break;

                                    case "switch_on":
                                        await _controller.DispatchOnOffScenarioAsync(endpoint, OpenNettyModels.Lighting.OnOffScenarioType.On);
                                        break;

                                    case "switch_off":
                                        await _controller.DispatchOnOffScenarioAsync(endpoint, OpenNettyModels.Lighting.OnOffScenarioType.Off);
                                        break;

                                    case "timed_action":
                                    {
                                        if (!TimeSpan.TryParse((string?) parameters?["duration"], CultureInfo.InvariantCulture, out var duration))
                                        {
                                            throw new InvalidDataException(SR.GetResourceString(SR.ID0068));
                                        }

                                        await _controller.DispatchTimedScenarioAsync(endpoint, duration);
                                        break;
                                    }
                                }
                                break;
                            }

                            case OpenNettyMqttAttributes.ShutterPosition when operation is OpenNettyMqttOperation.Get:
                            {
                                _ = await _controller.EnumerateShutterPositionsAsync(endpoint).ToListAsync();
                                break;
                            }

                            case OpenNettyMqttAttributes.ShutterPosition when operation is OpenNettyMqttOperation.Set:
                            {
                                if (!byte.TryParse(message.PayloadSegment, CultureInfo.InvariantCulture, out var position))
                                {
                                    throw new InvalidDataException(SR.GetResourceString(SR.ID0068));
                                }

                                await _controller.SetShutterPositionAsync(endpoint, position);
                                break;
                            }

                            case OpenNettyMqttAttributes.ShutterState when operation is OpenNettyMqttOperation.Get:
                            {
                                _ = await _controller.EnumerateShutterStatesAsync(endpoint).ToListAsync();
                                break;
                            }

                            case OpenNettyMqttAttributes.ShutterState when operation is OpenNettyMqttOperation.Set:
                            {
                                switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                                {
                                    case "close":
                                        await _controller.MoveShutterDownAsync(endpoint);
                                        break;

                                    case "open":
                                        await _controller.MoveShutterUpAsync(endpoint);
                                        break;

                                    case "stop":
                                        await _controller.StopShutterAsync(endpoint);
                                        break;
                                }
                                break;
                            }

                            case OpenNettyMqttAttributes.SmartMeterIndexes when operation is OpenNettyMqttOperation.Get:
                            {
                                _ = await _controller.GetSmartMeterIndexesAsync(endpoint);
                                break;
                            }

                            case OpenNettyMqttAttributes.SmartMeterPowerCutMode or OpenNettyMqttAttributes.SmartMeterRateType
                                when operation is OpenNettyMqttOperation.Get:
                            {
                                _ = await _controller.GetSmartMeterInformationAsync(endpoint);
                                break;
                            }

                            case OpenNettyMqttAttributes.StartupDate when operation is OpenNettyMqttOperation.Get:
                            {
                                _ = await _controller.GetUptimeAsync(endpoint);
                                break;
                            }

                            case OpenNettyMqttAttributes.SwitchState when operation is OpenNettyMqttOperation.Get:
                            {
                                _ = await _controller.EnumerateSwitchStatesAsync(endpoint).ToListAsync();
                                break;
                            }

                            case OpenNettyMqttAttributes.SwitchState when operation is OpenNettyMqttOperation.Set:
                            {
                                switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                                {
                                    case "on":
                                        await _controller.SwitchOnAsync(endpoint);
                                        break;

                                    case "off":
                                        await _controller.SwitchOffAsync(endpoint);
                                        break;

                                    case "toggle":
                                        await _controller.ToggleAsync(endpoint);
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
                                            OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOff);
                                        break;

                                    case "forced_on":
                                        await _controller.SetWaterHeaterSetpointModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn);
                                        break;

                                    case "automatic":
                                        await _controller.SetWaterHeaterSetpointModeAsync(endpoint,
                                            OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic);
                                        break;
                                }
                                break;
                            }

                            case OpenNettyMqttAttributes.WaterHeaterState when operation is OpenNettyMqttOperation.Get:
                            {
                                _ = await _controller.GetWaterHeaterStateAsync(endpoint);
                                break;
                            }

                            case OpenNettyMqttAttributes.ZigbeeBinding when operation is OpenNettyMqttOperation.Set:
                            {
                                switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                                {
                                    case "bind":
                                        await _controller.BindAsync(endpoint);
                                        break;

                                    case "unbind":
                                        await _controller.UnbindAsync(endpoint);
                                        break;
                                }
                                break;
                            }

                            case OpenNettyMqttAttributes.ZigbeeChannel when operation is OpenNettyMqttOperation.Get:
                            {
                                _ = await _controller.GetZigbeeChannelAsync(endpoint);
                                break;
                            }

                            case OpenNettyMqttAttributes.ZigbeeDevicesCount when operation is OpenNettyMqttOperation.Get:
                            {
                                _ = await _controller.CountZigbeeDevicesAsync(endpoint);
                                break;
                            }

                            case OpenNettyMqttAttributes.ZigbeeNetwork when operation is OpenNettyMqttOperation.Set:
                            {
                                switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                                {
                                    case "close":
                                        await _controller.CloseZigbeeNetworkAsync(endpoint);
                                        break;

                                    case "create":
                                        await _controller.CreateZigbeeNetworkAsync(endpoint);
                                        break;

                                    case "join":
                                        await _controller.JoinZigbeeNetworkAsync(endpoint);
                                        break;

                                    case "leave":
                                        await _controller.LeaveZigbeeNetworkAsync(endpoint);
                                        break;

                                    case "open":
                                        await _controller.OpenZigbeeNetworkAsync(endpoint);
                                        break;
                                }
                                break;
                            }

                            case OpenNettyMqttAttributes.ZigbeeSupervision when operation is OpenNettyMqttOperation.Set:
                            {
                                switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                                {
                                    case "disable":
                                        await _controller.DisableSupervisionAsync(endpoint);
                                        break;

                                    case "enable":
                                        await _controller.EnableSupervisionAsync(endpoint);
                                        break;
                                }
                                break;
                            }
                        }

                        if (!string.IsNullOrEmpty(message.ResponseTopic))
                        {
                            await client.EnqueueAsync(new MqttApplicationMessageBuilder()
                                .WithCorrelationData(message.CorrelationData)
                                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                                .WithTopic(message.ResponseTopic)
                                .Build());
                        }
                    }

                    catch (OpenNettyException exception) when (!string.IsNullOrEmpty(message.ResponseTopic))
                    {
                        await client.EnqueueAsync(new MqttApplicationMessageBuilder()
                            .WithContentType(MediaTypeNames.Application.Json)
                            .WithCorrelationData(message.CorrelationData)
                            .WithPayload(new JsonObject { ["error"] = exception.Message }.ToJsonString())
                            .WithPayloadFormatIndicator(MqttPayloadFormatIndicator.CharacterData)
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                            .WithTopic(message.ResponseTopic)
                            .Build());

                        throw;
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
                })
                .Do((Exception exception) => _logger.LogWarning(6018, exception, SR.GetResourceString(SR.ID6018)))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask))
            .Retry()
            .SubscribeAsync(static arguments => ValueTask.CompletedTask);

        await WaitCancellationAsync(cancellationToken);
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
                ["components"] = components,
                ["qos"] = 2
            };

            await foreach (var endpoint in from endpoint in _manager.EnumerateEndpointsAsync(cancellationToken)
                                           where endpoint.Device == device
                                           select endpoint)
            {
                var topic = endpoint.GetStringSetting(OpenNettySettings.MqttTopic) ?? endpoint.Name.ToLowerInvariant();

                if (SupportsLightOrSwitchEntity(endpoint))
                {
                    // Note: by default, endpoints that support ON/OFF switching are always treated as light entities.
                    var platform = endpoint.GetStringSetting(OpenNettySettings.HomeAssistantEntityType)
                        ?? OpenNettySettings.HomeAssistantEntityTypes.Light;

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
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(SupportsLightOrSwitchEntity)
                                .CountAsync(cancellationToken)),
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
                            component["on_command_type"] = "brightness";
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

                    AddComponent(components, component);

                    if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                    {
                        AddComponent(components, new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "3a10d925-c599-41a9-8a7e-30a04aefec86"u8),
                            ["entity_category"] = "diagnostic",
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8002, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                    .Where(endpoint => endpoint.Device == device)
                                    .Where(SupportsLightOrSwitchEntity)
                                    .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                                    .CountAsync(cancellationToken)),
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SwitchState}/get",
                            ["payload_press"] = string.Empty
                        });
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                        endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                    {
                        AddComponent(components, new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "f05ecfb8-70d5-4116-b0a8-2f6d9d02090f"u8),
                            ["entity_category"] = "diagnostic",
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8003, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                    .Where(endpoint => endpoint.Device == device)
                                    .Where(SupportsLightOrSwitchEntity)
                                    .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                                                       endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                                    .CountAsync(cancellationToken)),
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Brightness}/get",
                            ["payload_press"] = string.Empty
                        });
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
                                count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                    .Where(endpoint => endpoint.Device == device)
                                    .Where(SupportsCoverEntity)
                                    .CountAsync(cancellationToken)),
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

                    AddComponent(components, component);

                    if (endpoint.HasCapability(OpenNettyCapabilities.BasicShutterState) ||
                        endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
                    {
                        AddComponent(components, new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "3a760f9d-ca89-4c9e-9ad4-8ba40ad36f59"u8),
                            ["entity_category"] = "diagnostic",
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8005, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                    .Where(endpoint => endpoint.Device == device)
                                    .Where(SupportsCoverEntity)
                                    .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.BasicShutterState) ||
                                                       endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
                                    .CountAsync(cancellationToken)),
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ShutterState}/get",
                            ["payload_press"] = string.Empty
                        });
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
                    {
                        AddComponent(components, new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "5731274c-e498-4c7b-8671-6de8c448eb99"u8),
                            ["entity_category"] = "diagnostic",
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8006, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                    .Where(endpoint => endpoint.Device == device)
                                    .Where(SupportsCoverEntity)
                                    .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
                                    .CountAsync(cancellationToken)),
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ShutterPosition}/get",
                            ["payload_press"] = string.Empty
                        });
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
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioEvent)       ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.DimmingScenarioEvent)      ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioEvent)        ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioEvent)     ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioPlusEvent) ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.ProgressiveScenarioEvent)  ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioEvent)   ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.TimedScenarioEvent)        ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.ToggleScenarioEvent))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}",
                        ["json_attributes_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}",
                        ["event_types"] = new JsonArray([.. types])
                    };

                    if (endpoint.GetStringSetting(OpenNettySettings.HomeAssistantScenarioDeviceClass) is string type)
                    {
                        component["device_class"] = type;
                    }

                    AddComponent(components, component);
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioActivation))
                {
                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "63485e4c-a3bd-4fc9-831d-b96bacddade9"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8008, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "action"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "bca953c0-7598-4baa-91df-f14ddc30450f"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8025, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "stop_action",
                        ["enabled_by_default"] = false
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.DimmingScenarioActivation))
                {
                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "f47365e3-86fa-449f-b84e-a06acc3484b1"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8111, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.DimmingScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = new JsonObject { ["event_type"] = "dimming", ["dimming_step"] = 5 }.ToJsonString(),
                        ["enabled_by_default"] = false
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "5c2db171-97b3-451e-a502-8800928b4335"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8112, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.DimmingScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = new JsonObject { ["event_type"] = "dimming", ["dimming_step"] = -5 }.ToJsonString(),
                        ["enabled_by_default"] = false
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioActivation))
                {
                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "f053a594-66fa-42a6-9237-64d570b2bd57"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8009, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "switch_on"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "d292636e-00d3-442e-b1db-73d60b4085ec"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8010, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "switch_off"
                    });
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
                            AddComponent(components, new JsonObject
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
                                    count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                        .Where(endpoint => endpoint.Device == device)
                                        .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                        .CountAsync(cancellationToken)),
                                ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                                ["payload_press"] = new JsonObject { ["event_type"] = "pressure", ["scenario_type"] = "basic", ["button"] = button }.ToJsonString()
                            });

                            AddComponent(components, new JsonObject
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
                                    count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                        .Where(endpoint => endpoint.Device == device)
                                        .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                        .CountAsync(cancellationToken)),
                                ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                                ["payload_press"] = new JsonObject { ["event_type"] = "release_after_short_pressure", ["scenario_type"] = "evolved", ["button"] = button }.ToJsonString()
                            });

                            AddComponent(components, new JsonObject
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
                                    count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                        .Where(endpoint => endpoint.Device == device)
                                        .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                        .CountAsync(cancellationToken)),
                                ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                                ["payload_press"] = new JsonObject { ["event_type"] = "release_after_extended_pressure", ["scenario_type"] = "evolved", ["button"] = button }.ToJsonString()
                            });

                            AddComponent(components, new JsonObject
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
                                    count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                        .Where(endpoint => endpoint.Device == device)
                                        .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                        .CountAsync(cancellationToken)),
                                ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                                ["payload_press"] = new JsonObject { ["event_type"] = "extended_pressure", ["scenario_type"] = "evolved", ["button"] = button }.ToJsonString()
                            });
                        }
                    }

                    else
                    {
                        AddComponent(components, new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "be2887c3-f4ae-4935-bad6-1ffb1227d28b"u8),
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8015, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                    .Where(endpoint => endpoint.Device == device)
                                    .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                    .CountAsync(cancellationToken)),
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                            ["payload_press"] = new JsonObject { ["event_type"] = "pressure", ["scenario_type"] = "basic" }.ToJsonString()
                        });

                        AddComponent(components, new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "81e3f75a-fab3-4842-bb5e-1531d20290dd"u8),
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8016, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                    .Where(endpoint => endpoint.Device == device)
                                    .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                    .CountAsync(cancellationToken)),
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                            ["payload_press"] = new JsonObject { ["event_type"] = "release_after_short_pressure", ["scenario_type"] = "evolved" }.ToJsonString()
                        });

                        AddComponent(components, new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "3f069e7f-7c8f-4730-b067-9a944f61700b"u8),
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8017, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                    .Where(endpoint => endpoint.Device == device)
                                    .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                    .CountAsync(cancellationToken)),
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                            ["payload_press"] = new JsonObject { ["event_type"] = "release_after_extended_pressure", ["scenario_type"] = "evolved" }.ToJsonString()
                        });

                        AddComponent(components, new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "23e04eeb-8b35-44c8-87da-aed3829ce07d"u8),
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8018, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                    .Where(endpoint => endpoint.Device == device)
                                    .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                    .CountAsync(cancellationToken)),
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                            ["payload_press"] = new JsonObject { ["event_type"] = "extended_pressure", ["scenario_type"] = "evolved" }.ToJsonString()
                        });
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
                            AddComponent(components, new JsonObject
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
                                    count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                        .Where(endpoint => endpoint.Device == device)
                                        .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                        .CountAsync(cancellationToken)),
                                ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                                ["payload_press"] = new JsonObject { ["event_type"] = "short_pressure", ["scenario_type"] = "plus", ["button"] = button }.ToJsonString()
                            });

                            AddComponent(components, new JsonObject
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
                                    count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                        .Where(endpoint => endpoint.Device == device)
                                        .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                        .CountAsync(cancellationToken)),
                                ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                                ["payload_press"] = new JsonObject { ["event_type"] = "start_of_extended_pressure", ["scenario_type"] = "plus", ["button"] = button }.ToJsonString()
                            });

                            AddComponent(components, new JsonObject
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
                                    count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                        .Where(endpoint => endpoint.Device == device)
                                        .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                        .CountAsync(cancellationToken)),
                                ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                                ["payload_press"] = new JsonObject { ["event_type"] = "extended_pressure", ["scenario_type"] = "plus", ["button"] = button }.ToJsonString()
                            });

                            AddComponent(components, new JsonObject
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
                                    count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                        .Where(endpoint => endpoint.Device == device)
                                        .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                        .CountAsync(cancellationToken)),
                                ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                                ["payload_press"] = new JsonObject { ["event_type"] = "end_of_extended_pressure", ["scenario_type"] = "plus", ["button"] = button }.ToJsonString()
                            });
                        }
                    }

                    else
                    {
                        AddComponent(components, new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "63a92ba4-bec3-453e-a44a-9219e4f4d478"u8),
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8022, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                    .Where(endpoint => endpoint.Device == device)
                                    .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                    .CountAsync(cancellationToken)),
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                            ["payload_press"] = new JsonObject { ["event_type"] = "short_pressure", ["scenario_type"] = "plus" }.ToJsonString()
                        });

                        AddComponent(components, new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "e524f94b-9862-4da4-8f57-82c220e4560c"u8),
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8023, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                    .Where(endpoint => endpoint.Device == device)
                                    .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                    .CountAsync(cancellationToken)),
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                            ["payload_press"] = new JsonObject { ["event_type"] = "start_of_extended_pressure", ["scenario_type"] = "plus" }.ToJsonString()
                        });

                        AddComponent(components, new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "bf90ede8-9078-4817-8ec4-ef762c3e2077"u8),
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8018, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                    .Where(endpoint => endpoint.Device == device)
                                    .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                    .CountAsync(cancellationToken)),
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                            ["payload_press"] = new JsonObject { ["event_type"] = "extended_pressure", ["scenario_type"] = "plus" }.ToJsonString()
                        });

                        AddComponent(components, new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "73ff2263-9962-443a-89a1-f209fba81948"u8),
                            ["name"] = ComputeEntityName(
                                name    : GetLocalizedString(SR.ID8024, culture),
                                endpoint: endpoint,
                                culture : culture,
                                count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                    .Where(endpoint => endpoint.Device == device)
                                    .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
                                    .CountAsync(cancellationToken)),
                            ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                            ["payload_press"] = new JsonObject { ["event_type"] = "end_of_extended_pressure", ["scenario_type"] = "plus" }.ToJsonString()
                        });
                    }
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioActivation))
                {
                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "9065ccb4-d2c6-47f5-b118-e3d2ecbe20c3"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8026, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "shutter_up"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "4006cf9e-620c-49d4-81b3-ab060d376966"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8027, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "shutter_down"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "d13fdcd2-c974-490a-b544-436a91785ffa"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8028, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioActivation))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "shutter_stop"
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.BatteryAlert))
                {
                    AddComponent(components, new JsonObject
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
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.BatteryAlert))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.BatteryAlert}"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "7f7625f0-2461-4804-9cae-829a1040cd93"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:battery-check",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8030, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.BatteryAlert))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.BatteryAlert}/set",
                        ["payload_press"] = "OFF"
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.BatteryLevel))
                {
                    AddComponent(components, new JsonObject
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
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.BatteryLevel))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.BatteryLevel}"
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.FirmwareVersion))
                {
                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "3a92e77f-3910-4a20-9d19-caa1961dc33d"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8032, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.FirmwareVersion))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.FirmwareVersion}"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "e4fa32b3-9e9f-43b5-810a-cdb75acf44e5"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:help",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8033, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.FirmwareVersion))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.FirmwareVersion}/get",
                        ["payload_press"] = string.Empty
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.HardwareVersion))
                {
                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "1091f326-0c22-4c59-af04-d0a6ee429a0c"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8034, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.HardwareVersion))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.HardwareVersion}"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "0371ccbb-52fd-4288-a943-b2f04a7b1e8b"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:help",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8035, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.HardwareVersion))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.HardwareVersion}/get",
                        ["payload_press"] = string.Empty
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.MacAddress))
                {
                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "a8de45b2-0bb5-4375-b33b-0869623e40a7"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8036, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.MacAddress))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.MacAddress}"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "aa1968e6-f232-4b96-a29f-0e64de093bb0"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:help",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8037, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.MacAddress))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.MacAddress}/get",
                        ["payload_press"] = string.Empty
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchControl) &&
                    endpoint.GetStringSetting(OpenNettySettings.SwitchMode) is OpenNettySettings.SwitchModes.PushButton)
                {
                    // Note: endpoints that use the "push button" mode are always represented as buttons instead of light entities.
                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "5c207503-bbc7-47dc-a4a7-8833c5bf058f"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8038, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchControl))
                                .Where(endpoint => endpoint.GetStringSetting(OpenNettySettings.SwitchMode) is OpenNettySettings.SwitchModes.PushButton)
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SwitchState}/set",
                        ["payload_press"] = "ON"
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireControl))
                {
                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "select",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "205a01a1-ba4c-4e9b-a19a-c1589c445cbb"u8),
                        ["icon"] = "mdi:radiator",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8039, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireControl))
                                .CountAsync(cancellationToken)),
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
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "7a9130b9-675a-437d-b806-cbfe6f6e20a6"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8062, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireControl))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireSetpointMode}/get",
                        ["payload_press"] = string.Empty
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireDerogation))
                {
                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "select",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "f5f57920-d758-4ca8-8161-2614d4abeef0"u8),
                        ["icon"] = "mdi:radiator",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8045, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireDerogation))
                                .CountAsync(cancellationToken)),
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
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "787582e8-0c5f-4c97-9277-0ad23dab4024"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8063, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireDerogation))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireDerogationMode}/get",
                        ["payload_press"] = string.Empty
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireShutdown))
                {
                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "switch",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "178d9f9b-e87a-4ebf-8db3-80e1e1a091df"u8),
                        ["icon"] = "mdi:radiator-off",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8113, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireShutdown))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireShutdownMode}/set",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireShutdownMode}"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "ecc822a6-57ab-4352-8d2c-d85dc73df5da"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8114, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireShutdown))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireShutdownMode}/get",
                        ["payload_press"] = string.Empty
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                {
                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "6c83787a-3537-49fa-b409-dc15d5c37b43"u8),
                        ["device_class"] = "energy",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8064, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterIndexes}",
                        ["value_template"] = "{{ value_json.base_index }}"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "0a9d909b-8449-496e-b38c-4dc3e2653288"u8),
                        ["device_class"] = "energy",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8065, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterIndexes}",
                        ["value_template"] = "{{ value_json.blue_index }}"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "2661d8db-085a-41bb-bab6-a1627cbf91d0"u8),
                        ["device_class"] = "energy",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8066, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterIndexes}",
                        ["value_template"] = "{{ value_json.off_peak_index }}"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "7dcf846c-fbdd-4457-9a17-9cbc0a7c072b"u8),
                        ["device_class"] = "energy",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8067, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterIndexes}",
                        ["value_template"] = "{{ value_json.red_index }}"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "1de29bc5-70b6-4302-aa27-8ecbcce13ec9"u8),
                        ["device_class"] = "energy",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8068, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterIndexes}",
                        ["value_template"] = "{{ value_json.white_index }}"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "e07f0687-6ca1-47d9-a5b7-b20c0e79775a"u8),
                        ["device_class"] = "enum",
                        ["icon"] = "mdi:receipt-text-outline",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8069, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterIndexes}",
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
                            {{ map[value_json.subscription_type] }}
                            """,
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "2eddee8f-7c81-47ea-a775-785e9dfb5c26"u8),
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8073, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterIndexes}/get",
                        ["payload_press"] = string.Empty
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                {
                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "31eda1f3-343f-4cd7-9f56-ea792fcaec7f"u8),
                        ["device_class"] = "enum",
                        ["icon"] = "mdi:receipt-text-outline",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8074, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterRateType}",
                        ["options"] = new JsonArray([GetLocalizedString(SR.ID8075, culture), GetLocalizedString(SR.ID8076, culture)]),
                        ["value_template"] = $$$"""
                            {% set map = {
                              'peak': '{{{GetLocalizedString(SR.ID8075, culture).Replace("'", "\\'")}}}',
                              'off_peak': '{{{GetLocalizedString(SR.ID8076, culture).Replace("'", "\\'")}}}'
                            } %}
                            {{ map[value] }}
                            """,
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "binary_sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "280fd1d1-4220-42ec-bf70-69936b728eb2"u8),
                        ["icon"] = "mdi:transmission-tower-off",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8077, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterPowerCutMode}"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "91e32508-61fa-46e3-ba57-32166b2de116"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8078, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterRateType}/get",
                        ["payload_press"] = string.Empty
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "df24321d-01e8-4d1a-9cca-92ece18934b4"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8079, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterPowerCutMode}/get",
                        ["payload_press"] = string.Empty
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.Uptime))
                {
                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "46c1f892-f9bf-46b6-8658-fed1d7eb177b"u8),
                        ["entity_category"] = "diagnostic",
                        ["device_class"] = "date",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8080, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.Uptime))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.StartupDate}",
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "5e9b5094-b028-492c-8be4-5b73139e2c57"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name: GetLocalizedString(SR.ID8081, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.Uptime))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.StartupDate}/get",
                        ["payload_press"] = string.Empty
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                {
                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "select",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "733d9bf6-fd89-4ce1-bd71-d12a1c6a846e"u8),
                        ["icon"] = "mdi:water-boiler",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8109, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                                .CountAsync(cancellationToken)),
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
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "binary_sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "344b6548-196d-42de-b627-22530cc28f07"u8),
                        ["icon"] = "mdi:fire",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8085, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.WaterHeaterState}",
                        ["payload_on"] = "heating",
                        ["payload_off"] = "idle"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "9aa4633b-c253-4623-a80c-54ba9d681777"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8110, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.WaterHeaterSetpointMode}/get",
                        ["payload_press"] = string.Empty
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "bb186d7c-f49d-4aa2-8770-a0fdcf9de123"u8),
                        ["entity_category"] = "diagnostic",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8086, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.WaterHeaterState}/get",
                        ["payload_press"] = string.Empty
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.WirelessBurglarAlarmState))
                {
                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "2dd476d5-a35a-442a-a3f2-4c2621dcf375"u8),
                        ["device_class"] = "enum",
                        ["icon"] = "mdi:shield-home-outline",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8087, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.WirelessBurglarAlarmState))
                                .CountAsync(cancellationToken)),
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
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.ZigbeeBinding))
                {
                    AddComponent(components, new JsonObject
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
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeBinding))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeBinding}",
                        ["value_template"] = """
                            {% set map = {
                              'canceled': 'OFF',
                              'closed': 'OFF',
                              'opened': 'ON'
                            } %}
                            {{ map[value] }}
                            """
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "d04dc07d-b614-4e3e-aeac-ccaead40d919"u8),
                        ["entity_category"] = "config",
                        ["icon"] = "mdi:link",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8094, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeBinding))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeBinding}/set",
                        ["payload_press"] = "bind"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "55a25c45-cb7a-4b74-baa4-7f9b87465d1a"u8),
                        ["entity_category"] = "config",
                        ["icon"] = "mdi:link-off",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8095, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeBinding))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeBinding}/set",
                        ["payload_press"] = "unbind"
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                {
                    AddComponent(components, new JsonObject
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
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
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
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "5ea35f24-9a6c-4d62-b000-85e6a9ef5380"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:sine-wave",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8096, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeChannel}",
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "abda41d7-1b4c-41cc-99b9-81b7bc5801d3"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:counter",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8105, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeDevicesCount}",
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "b7355e3d-0137-41e7-90fa-8b81b9530466"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:sine-wave",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8097, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeChannel}/get",
                        ["payload_press"] = string.Empty
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "6c550d13-cbd5-4a7a-b445-1c30e9b83c65"u8),
                        ["entity_category"] = "diagnostic",
                        ["icon"] = "mdi:counter",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8106, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeDevicesCount}/get",
                        ["payload_press"] = string.Empty
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "f366a06c-6d7f-4741-a99a-490fedeabf9f"u8),
                        ["icon"] = "mdi:new-box",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8098, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeNetwork}/set",
                        ["payload_press"] = "create"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "1c56979a-3ad2-4b9b-9116-cd7321416b6a"u8),
                        ["icon"] = "mdi:download-network",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8099, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeNetwork}/set",
                        ["payload_press"] = "join"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "63096c5c-3fba-4ea7-a30d-3568b0680e18"u8),
                        ["icon"] = "mdi:upload-network",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8100, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeNetwork}/set",
                        ["payload_press"] = "leave",
                        ["enabled_by_default"] = false
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "09953b7d-0e18-4fc9-9b8c-2a11b52a15c5"u8),
                        ["icon"] = "mdi:lock-open",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8101, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeNetwork}/set",
                        ["payload_press"] = "open"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "7e344b36-199e-4a43-987f-59fccacc859b"u8),
                        ["icon"] = "mdi:lock",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8102, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeNetwork}/set",
                        ["payload_press"] = "close"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "f6a3d89f-a3a4-4776-a6b0-a0e3328183ee"u8),
                        ["icon"] = "mdi:eye-check",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8103, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeSupervision}/set",
                        ["payload_press"] = "enable"
                    });

                    AddComponent(components, new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "35f91e70-674d-4977-9475-ba231553051d"u8),
                        ["icon"] = "mdi:eye-remove",
                        ["name"] = ComputeEntityName(
                            name    : GetLocalizedString(SR.ID8104, culture),
                            endpoint: endpoint,
                            culture : culture,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeSupervision}/set",
                        ["payload_press"] = "disable"
                    });
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

        static void AddComponent(JsonObject components, JsonObject component)
        {
            var identifier = (string?) component["unique_id"]?.AsValue();
            if (string.IsNullOrEmpty(identifier))
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0119));
            }

            components.Add($"entity_{identifier}", component);
        }

        static JsonObject CreateDeviceNode(OpenNettyDevice device, CultureInfo culture)
        {
            var node = new JsonObject
            {
                ["identifiers"] = new JsonArray([ComputeDeviceUniqueId(device)]),
                ["manufacturer"] = Enum.GetName(device.Identity.Brand),
                ["model_id"] = device.Identity.Model,
                ["serial_number"] = device.Identifier.ToString(),
                ["name"] = $"{Enum.GetName(device.Identity.Brand)} {device.Identity.Model} ({device.Identifier})"
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

            if (device.Gateway is not null)
            {
                node["via_device"] = ComputeDeviceUniqueId(device.Gateway.Device);
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

            // If the endpoint is configured to use the special "push button" mode, do not consider
            // it suitable for a light entity. Instead, it will be represented as a dedicated button.
            if (endpoint.GetStringSetting(OpenNettySettings.SwitchMode) is OpenNettySettings.SwitchModes.PushButton)
            {
                return false;
            }

            return true;
        }
    }

    static (string? FriendlyName, string? attribute, OpenNettyMqttOperation? Operation) ExtractParameters(MqttApplicationMessage message)
        => message.Topic.Split('/', StringSplitOptions.RemoveEmptyEntries) switch
        {
            [_, .. string[] topics, string attribute, "get"]
                => (string.Join('/', topics), attribute, OpenNettyMqttOperation.Get),

            [_, .. string[] topics, string attribute, "set"]
                => (string.Join('/', topics), attribute, OpenNettyMqttOperation.Set),

            _ => (null, null, null)
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    static async Task WaitCancellationAsync(CancellationToken cancellationToken)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(static state => ((TaskCompletionSource) state!).SetResult(), source);
        await source.Task;
    }
}
