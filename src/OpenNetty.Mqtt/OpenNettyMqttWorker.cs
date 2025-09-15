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

                            case OpenNettyMqttAttributes.Brightness when operation is OpenNettyMqttOperation.Get:
                                _ = await _controller.EnumerateBrightnessAsync(endpoint).ToListAsync();
                                break;

                            case OpenNettyMqttAttributes.Brightness when operation is OpenNettyMqttOperation.Set:
                                if (!byte.TryParse(message.PayloadSegment, CultureInfo.InvariantCulture, out var level))
                                {
                                    throw new InvalidDataException(SR.GetResourceString(SR.ID0068));
                                }

                                await _controller.SetBrightnessAsync(endpoint, level);
                                break;

                            case OpenNettyMqttAttributes.FirmwareVersion when operation is OpenNettyMqttOperation.Get:
                                _ = await _controller.GetFirmwareVersionAsync(endpoint);
                                break;

                            case OpenNettyMqttAttributes.HardwareVersion when operation is OpenNettyMqttOperation.Get:
                                _ = await _controller.GetHardwareVersionAsync(endpoint);
                                break;

                            case OpenNettyMqttAttributes.MacAddress when operation is OpenNettyMqttOperation.Get:
                                _ = await _controller.GetMacAddressAsync(endpoint);
                                break;

                            case OpenNettyMqttAttributes.PilotWireDerogationMode when operation is OpenNettyMqttOperation.Get:
                            case OpenNettyMqttAttributes.PilotWireSetpointMode   when operation is OpenNettyMqttOperation.Get:
                                _ = await _controller.GetPilotWireConfigurationAsync(endpoint);
                                break;

                            case OpenNettyMqttAttributes.PilotWireDerogationMode when operation is OpenNettyMqttOperation.Set:
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

                            case OpenNettyMqttAttributes.PilotWireSetpointMode when operation is OpenNettyMqttOperation.Set:
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

                            case OpenNettyMqttAttributes.Scenario when operation is OpenNettyMqttOperation.Set:
                                switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                                {
                                    case "action":
                                        await _controller.DispatchActionScenarioAsync(endpoint);
                                        break;

                                    case "shutter_down":
                                        await _controller.DispatchShutterDownScenarioAsync(endpoint);
                                        break;

                                    case "shutter_stop":
                                        await _controller.DispatchShutterStopScenarioAsync(endpoint);
                                        break;

                                    case "shutter_up":
                                        await _controller.DispatchShutterUpScenarioAsync(endpoint);
                                        break;

                                    case "stop_action":
                                        await _controller.DispatchStopActionScenarioAsync(endpoint);
                                        break;

                                    case "switch_on":
                                        await _controller.DispatchOnScenarioAsync(endpoint);
                                        break;

                                    case "switch_off":
                                        await _controller.DispatchOffScenarioAsync(endpoint);
                                        break;
                                }
                                break;

                            case OpenNettyMqttAttributes.ShutterPosition when operation is OpenNettyMqttOperation.Get:
                                _ = await _controller.EnumerateShutterPositionsAsync(endpoint).ToListAsync();
                                break;

                            case OpenNettyMqttAttributes.ShutterPosition when operation is OpenNettyMqttOperation.Set:
                                if (!byte.TryParse(message.PayloadSegment, CultureInfo.InvariantCulture, out var position))
                                {
                                    throw new InvalidDataException(SR.GetResourceString(SR.ID0068));
                                }

                                await _controller.SetShutterPositionAsync(endpoint, position);
                                break;

                            case OpenNettyMqttAttributes.ShutterState when operation is OpenNettyMqttOperation.Get:
                                _ = await _controller.EnumerateShutterStatesAsync(endpoint).ToListAsync();
                                break;

                            case OpenNettyMqttAttributes.ShutterState when operation is OpenNettyMqttOperation.Set:
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

                            case OpenNettyMqttAttributes.SmartMeterIndexes when operation is OpenNettyMqttOperation.Get:
                                _ = await _controller.GetSmartMeterIndexesAsync(endpoint);
                                break;

                            case OpenNettyMqttAttributes.SmartMeterPowerCutMode or OpenNettyMqttAttributes.SmartMeterRateType
                                when operation is OpenNettyMqttOperation.Get:
                                _ = await _controller.GetSmartMeterInformationAsync(endpoint);
                                break;

                            case OpenNettyMqttAttributes.StartupDate when operation is OpenNettyMqttOperation.Get:
                                _ = await _controller.GetUptimeAsync(endpoint);
                                break;

                            case OpenNettyMqttAttributes.SwitchState when operation is OpenNettyMqttOperation.Get:
                                _ = await _controller.EnumerateSwitchStatesAsync(endpoint).ToListAsync();
                                break;

                            case OpenNettyMqttAttributes.SwitchState when operation is OpenNettyMqttOperation.Set:
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

                            case OpenNettyMqttAttributes.WaterHeaterSetpointMode when operation is OpenNettyMqttOperation.Set:
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

                            case OpenNettyMqttAttributes.WaterHeaterState when operation is OpenNettyMqttOperation.Get:
                                _ = await _controller.GetWaterHeaterStateAsync(endpoint);
                                break;

                            case OpenNettyMqttAttributes.ZigbeeBinding when operation is OpenNettyMqttOperation.Set:
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

                            case OpenNettyMqttAttributes.ZigbeeNetwork when operation is OpenNettyMqttOperation.Set:
                                switch (message.ConvertPayloadToString()?.ToLowerInvariant())
                                {
                                    case "close":
                                        await _controller.CloseNetworkAsync(endpoint);
                                        break;

                                    case "create":
                                        await _controller.CreateNetworkAsync(endpoint);
                                        break;

                                    case "join":
                                        await _controller.JoinNetworkAsync(endpoint);
                                        break;

                                    case "leave":
                                        await _controller.LeaveNetworkAsync(endpoint);
                                        break;

                                    case "open":
                                        await _controller.OpenNetworkAsync(endpoint);
                                        break;
                                }
                                break;

                            case OpenNettyMqttAttributes.ZigbeeSupervision when operation is OpenNettyMqttOperation.Set:
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
                ["device"] = CreateDeviceNode(device),
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

                    var component = new JsonObject
                    {
                        ["platform"] = platform,
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "feb44223-4814-4652-933c-53dbbaabac3f"u8),
                        ["name"] = ComputeEntityName(
                            name    : platform is OpenNettySettings.HomeAssistantEntityTypes.Switch ? "Switch" : "Light",
                            endpoint: endpoint,
                            setting : OpenNettySettings.HomeAssistantLightSwitchName,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(SupportsLightOrSwitchEntity)
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SwitchState}/set"
                    };

                    if (endpoint.GetStringSetting(OpenNettySettings.HomeAssistantLightSwitchIcon) is string icon)
                    {
                        component["icon"] = icon;
                    }

                    // Note: endpoints that can't report their state (e.g radio Nitoo devices) can still be mapped to a light entity:
                    // in that case, Home Assistant will automatically use the optimistic mode to dynamically update the current state.
                    if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                    {
                        component["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SwitchState}";
                    }

                    // Unlike light entities, switch entities can specify a device class but cannot support brightness control.
                    if (platform is OpenNettySettings.HomeAssistantEntityTypes.Switch)
                    {
                        component["device_class"] = endpoint.GetStringSetting(OpenNettySettings.HomeAssistantLightSwitchDeviceClass)
                            ?? OpenNettySettings.HomeAssistantDeviceClasses.Switch;
                    }

                    else
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

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", component);

                    if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                    {
                        components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "3a10d925-c599-41a9-8a7e-30a04aefec86"u8),
                            ["name"] = ComputeEntityName(
                                name    : "Get switch state",
                                endpoint: endpoint,
                                setting : OpenNettySettings.HomeAssistantLightSwitchName,
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
                        components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "f05ecfb8-70d5-4116-b0a8-2f6d9d02090f"u8),
                            ["name"] = ComputeEntityName(
                                name    : "Get brightness",
                                endpoint: endpoint,
                                setting : OpenNettySettings.HomeAssistantLightSwitchName,
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
                        ["name"] = ComputeEntityName(
                            name    : "Cover",
                            endpoint: endpoint,
                            setting : OpenNettySettings.HomeAssistantCoverName,
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

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", component);

                    if (endpoint.HasCapability(OpenNettyCapabilities.BasicShutterState) ||
                        endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
                    {
                        components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "3a760f9d-ca89-4c9e-9ad4-8ba40ad36f59"u8),
                            ["name"] = ComputeEntityName(
                                name    : "Get cover state",
                                endpoint: endpoint,
                                setting : OpenNettySettings.HomeAssistantCoverName,
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
                        components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                        {
                            ["platform"] = "button",
                            ["unique_id"] = ComputeEntityUniqueId(endpoint, "5731274c-e498-4c7b-8671-6de8c448eb99"u8),
                            ["name"] = ComputeEntityName(
                                name    : "Get cover position",
                                endpoint: endpoint,
                                setting : OpenNettySettings.HomeAssistantCoverName,
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

                if (endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioState) ||
                    endpoint.HasCapability(OpenNettyCapabilities.DimmingScenarioState) ||
                    endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioState) ||
                    endpoint.HasCapability(OpenNettyCapabilities.ProgressiveScenarioState) ||
                    endpoint.HasCapability(OpenNettyCapabilities.ShortPressureScenarioState) ||
                    endpoint.HasCapability(OpenNettyCapabilities.StopActionScenarioState) ||
                    endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioState) ||
                    endpoint.HasCapability(OpenNettyCapabilities.TimedScenarioState) ||
                    endpoint.HasCapability(OpenNettyCapabilities.ToggleScenarioState))
                {
                    var types = new HashSet<string>(StringComparer.Ordinal);

                    if (endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioState))
                    {
                        types.Add("action");
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.DimmingScenarioState))
                    {
                        types.Add("dimming_step_up");
                        types.Add("dimming_step_down");
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioState))
                    {
                        types.Add("switch_on");
                        types.Add("switch_off");
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.ProgressiveScenarioState))
                    {
                        types.Add("progressive_action");
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.ShortPressureScenarioState))
                    {
                        types.Add("short_pressure");
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.StopActionScenarioState))
                    {
                        types.Add("stop_action");
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioState))
                    {
                        types.Add("shutter_up");
                        types.Add("shutter_down");
                        types.Add("shutter_stop");
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.TimedScenarioState))
                    {
                        types.Add("timed_action");
                    }

                    if (endpoint.HasCapability(OpenNettyCapabilities.ToggleScenarioState))
                    {
                        types.Add("switch_toggle");
                    }

                    var component = new JsonObject
                    {
                        ["platform"] = "event",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "7faeaa9a-ae51-43f4-af82-65d2e24e14d0"u8),
                        ["icon"] = endpoint.GetStringSetting(OpenNettySettings.HomeAssistantScenarioIcon) ?? "mdi:lightning-bolt",
                        ["name"] = ComputeEntityName(
                            name    : "Scenario",
                            endpoint: endpoint,
                            setting : OpenNettySettings.HomeAssistantScenarioName,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioState) ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.DimmingScenarioState) ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioState) ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.ProgressiveScenarioState) ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.ShortPressureScenarioState) ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.StopActionScenarioState) ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioState) ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.TimedScenarioState) ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.ToggleScenarioState))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}",
                        ["event_types"] = new JsonArray([.. types])
                    };

                    if (endpoint.GetStringSetting(OpenNettySettings.HomeAssistantScenarioDeviceClass) is string type)
                    {
                        component["device_class"] = type;
                    }

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", component);
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioControl))
                {
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "63485e4c-a3bd-4fc9-831d-b96bacddade9"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Dispatch action scenario",
                            endpoint: endpoint,
                            setting : OpenNettySettings.HomeAssistantScenarioName,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioControl))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "action"
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioControl))
                {
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "f053a594-66fa-42a6-9237-64d570b2bd57"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Dispatch ON scenario",
                            endpoint: endpoint,
                            setting : OpenNettySettings.HomeAssistantScenarioName,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioControl))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "switch_on"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "d292636e-00d3-442e-b1db-73d60b4085ec"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Dispatch OFF scenario",
                            endpoint: endpoint,
                            setting : OpenNettySettings.HomeAssistantScenarioName,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioControl))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "switch_off"
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.StopActionScenarioControl))
                {
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "bca953c0-7598-4baa-91df-f14ddc30450f"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Dispatch stop action scenario",
                            endpoint: endpoint,
                            setting : OpenNettySettings.HomeAssistantScenarioName,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.StopActionScenarioControl))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "stop_action",
                        ["enabled_by_default"] = false
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioControl))
                {
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "9065ccb4-d2c6-47f5-b118-e3d2ecbe20c3"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Dispatch UP scenario",
                            endpoint: endpoint,
                            setting : OpenNettySettings.HomeAssistantScenarioName,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioControl))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "shutter_up"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "4006cf9e-620c-49d4-81b3-ab060d376966"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Dispatch DOWN scenario",
                            endpoint: endpoint,
                            setting : OpenNettySettings.HomeAssistantScenarioName,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioControl))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "shutter_down"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "d13fdcd2-c974-490a-b544-436a91785ffa"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Dispatch STOP scenario",
                            endpoint: endpoint,
                            setting : OpenNettySettings.HomeAssistantScenarioName,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioControl))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.Scenario}/set",
                        ["payload_press"] = "shutter_stop"
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.BatteryAlert))
                {
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "binary_sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "b7dd3824-ddc3-4cf3-b79e-168faa710e43"u8),
                        ["device_class"] = "battery",
                        ["off_delay"] = 3600,
                        ["name"] = ComputeEntityName(
                            name    : "Battery",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.BatteryAlert))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.BatteryAlert}"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "7f7625f0-2461-4804-9cae-829a1040cd93"u8),
                        ["icon"] = "mdi:battery-check",
                        ["name"] = ComputeEntityName(
                            name    : "Reset battery state",
                            endpoint: endpoint,
                            setting : null,
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
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "6a8c7f1c-426a-47ab-97a1-a476acc603fd"u8),
                        ["device_class"] = "battery",
                        ["unit_of_measurement"] = "%",
                        ["name"] = ComputeEntityName(
                            name    : "Battery",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.BatteryLevel))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.BatteryLevel}"
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.FirmwareVersion) ||
                    endpoint.HasCapability(OpenNettyCapabilities.DeviceDescription))
                {
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "3a92e77f-3910-4a20-9d19-caa1961dc33d"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Firmware version",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.FirmwareVersion) ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.DeviceDescription))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.FirmwareVersion}"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "e4fa32b3-9e9f-43b5-810a-cdb75acf44e5"u8),
                        ["icon"] = "mdi:help",
                        ["name"] = ComputeEntityName(
                            name    : "Get firmware version",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.FirmwareVersion) ||
                                                   endpoint.HasCapability(OpenNettyCapabilities.DeviceDescription))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.FirmwareVersion}/get",
                        ["payload_press"] = string.Empty
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.HardwareVersion))
                {
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "1091f326-0c22-4c59-af04-d0a6ee429a0c"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Hardware version",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.HardwareVersion))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.HardwareVersion}"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "0371ccbb-52fd-4288-a943-b2f04a7b1e8b"u8),
                        ["icon"] = "mdi:help",
                        ["name"] = ComputeEntityName(
                            name    : "Get hardware version",
                            endpoint: endpoint,
                            setting : null,
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
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "a8de45b2-0bb5-4375-b33b-0869623e40a7"u8),
                        ["name"] = ComputeEntityName(
                            name    : "MAC address",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.MacAddress))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.MacAddress}"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "aa1968e6-f232-4b96-a29f-0e64de093bb0"u8),
                        ["icon"] = "mdi:help",
                        ["name"] = ComputeEntityName(
                            name    : "Get MAC address",
                            endpoint: endpoint,
                            setting : null,
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
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "5c207503-bbc7-47dc-a4a7-8833c5bf058f"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Push button",
                            endpoint: endpoint,
                            setting : OpenNettySettings.HomeAssistantLightSwitchName,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchControl))
                                .Where(endpoint => endpoint.GetStringSetting(OpenNettySettings.SwitchMode) is OpenNettySettings.SwitchModes.PushButton)
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SwitchState}/set",
                        ["payload_press"] = "ON"
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                {
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "select",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "205a01a1-ba4c-4e9b-a19a-c1589c445cbb"u8),
                        ["icon"] = "mdi:radiator",
                        ["name"] = ComputeEntityName(
                            name    : "Setpoint mode",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireSetpointMode}/set",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireSetpointMode}",
                        ["options"] = new JsonArray(["Comfort", "Comfort -1°C", "Comfort -2°C", "Eco", "Frost protection"]),
                        ["value_template"] = """
                            {% set map = {
                              'comfort': 'Comfort',
                              'comfort-1': 'Comfort -1°C',
                              'comfort-2': 'Comfort -2°C',
                              'eco': 'Eco',
                              'frost_protection': 'Frost protection'
                            } %}
                            {{ map[value] }}
                            """,
                        ["command_template"] = """
                            {% set map = {
                              'Comfort': 'comfort',
                              'Comfort -1°C': 'comfort-1',
                              'Comfort -2°C': 'comfort-2',
                              'Eco': 'eco',
                              'Frost protection': 'frost_protection'
                            } %}
                            {{ map[value] }}
                            """
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "select",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "f5f57920-d758-4ca8-8161-2614d4abeef0"u8),
                        ["icon"] = "mdi:radiator",
                        ["name"] = ComputeEntityName(
                            name    : "Derogation mode",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireDerogationMode}/set",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireDerogationMode}",
                        ["options"] = new JsonArray(
                        [
                            "No derogation",
                            "Comfort, until the next setpoint change",
                            "Comfort, for at least 4 hours",
                            "Comfort, for at least 8 hours",
                            "Comfort -1°C, until the next setpoint change",
                            "Comfort -1°C, for at least 4 hours",
                            "Comfort -1°C, for at least 8 hours",
                            "Comfort -2°C, until the next setpoint change",
                            "Comfort -2°C, for at least 4 hours",
                            "Comfort -2°C, for at least 8 hours",
                            "Eco, until the next setpoint change",
                            "Eco, for at least 4 hours",
                            "Eco, for at least 8 hours",
                            "Frost protection (permanent)",
                            "Frost protection, for at least 4 hours",
                            "Frost protection, for at least 8 hours"
                        ]),
                        ["value_template"] = """
                            {% set map = {
                              'none': 'No derogation',
                              'comfort': 'Comfort, until the next setpoint change',
                              'comfort:4h': 'Comfort, for at least 4 hours',
                              'comfort:8h': 'Comfort, for at least 8 hours',
                              'comfort-1': 'Comfort -1°C, until the next setpoint change',
                              'comfort-1:4h': 'Comfort -1°C, for at least 4 hours',
                              'comfort-1:8h': 'Comfort -1°C, for at least 8 hours',
                              'comfort-2': 'Comfort -2°C, until the next setpoint change',
                              'comfort-2:4h': 'Comfort -2°C, for at least 4 hours',
                              'comfort-2:8h': 'Comfort -2°C, for at least 8 hours',
                              'eco': 'Eco, until the next setpoint change',
                              'eco:4h': 'Eco, for at least 4 hours',
                              'eco:8h': 'Eco, for at least 8 hours',
                              'frost_protection': 'Frost protection (permanent)',
                              'frost_protection:4h': 'Frost protection, for at least 4 hours',
                              'frost_protection:8h': 'Frost protection, for at least 8 hours'
                            } %}
                            {{ map[value] }}
                            """,
                        ["command_template"] = """
                            {% set map = {
                              'No derogation': 'none',
                              'Comfort, until the next setpoint change': 'comfort',
                              'Comfort, for at least 4 hours': 'comfort:4h',
                              'Comfort, for at least 8 hours': 'comfort:8h',
                              'Comfort -1°C, until the next setpoint change': 'comfort-1',
                              'Comfort -1°C, for at least 4 hours': 'comfort-1:4h',
                              'Comfort -1°C, for at least 8 hours': 'comfort-1:8h',
                              'Comfort -2°C, until the next setpoint change': 'comfort-2',
                              'Comfort -2°C, for at least 4 hours': 'comfort-2:4h',
                              'Comfort -2°C, for at least 8 hours': 'comfort-2:8h',
                              'Eco, until the next setpoint change': 'eco',
                              'Eco, for at least 4 hours': 'eco:4h',
                              'Eco, for at least 8 hours': 'eco:8h',
                              'Frost protection (permanent)': 'frost_protection',
                              'Frost protection, for at least 4 hours': 'frost_protection:4h',
                              'Frost protection, for at least 8 hours': 'frost_protection:8h'
                            } %}
                            {{ map[value] }}
                            """
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "7a9130b9-675a-437d-b806-cbfe6f6e20a6"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Get setpoint mode",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireSetpointMode}/get",
                        ["payload_press"] = string.Empty
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "787582e8-0c5f-4c97-9277-0ad23dab4024"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Get derogation mode",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.PilotWireDerogationMode}/get",
                        ["payload_press"] = string.Empty
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                {
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "6c83787a-3537-49fa-b409-dc15d5c37b43"u8),
                        ["device_class"] = "energy",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : "Base index",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterIndexes}",
                        ["value_template"] = "{{ value_json.base_index }}"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "0a9d909b-8449-496e-b38c-4dc3e2653288"u8),
                        ["device_class"] = "energy",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : "Blue index",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterIndexes}",
                        ["value_template"] = "{{ value_json.blue_index }}"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "2661d8db-085a-41bb-bab6-a1627cbf91d0"u8),
                        ["device_class"] = "energy",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : "Off-peak index",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterIndexes}",
                        ["value_template"] = "{{ value_json.off_peak_index }}"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "7dcf846c-fbdd-4457-9a17-9cbc0a7c072b"u8),
                        ["device_class"] = "energy",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : "Red index",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterIndexes}",
                        ["value_template"] = "{{ value_json.red_index }}"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "1de29bc5-70b6-4302-aa27-8ecbcce13ec9"u8),
                        ["device_class"] = "energy",
                        ["unit_of_measurement"] = "kWh",
                        ["suggested_display_precision"] = 0,
                        ["name"] = ComputeEntityName(
                            name    : "White index",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterIndexes}",
                        ["value_template"] = "{{ value_json.white_index }}"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "e07f0687-6ca1-47d9-a5b7-b20c0e79775a"u8),
                        ["device_class"] = "enum",
                        ["icon"] = "mdi:receipt-text-outline",
                        ["name"] = ComputeEntityName(
                            name    : "Subscription type",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterIndexes}",
                        ["options"] = new JsonArray(
                        [
                            "Base",
                            "Off-peak",
                            "Tempo"
                        ]),
                        ["value_template"] = """
                            {% set map = {
                              'base': 'Base',
                              'off_peak': 'Off-peak',
                              'tempo': 'Tempo'
                            } %}
                            {{ map[value_json.subscription_type] }}
                            """,
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "2eddee8f-7c81-47ea-a775-785e9dfb5c26"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Get indexes",
                            endpoint: endpoint,
                            setting : null,
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
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "31eda1f3-343f-4cd7-9f56-ea792fcaec7f"u8),
                        ["device_class"] = "enum",
                        ["icon"] = "mdi:receipt-text-outline",
                        ["name"] = ComputeEntityName(
                            name    : "Rate type",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterRateType}",
                        ["options"] = new JsonArray(
                        [
                            "Off-peak",
                            "Peak"
                        ]),
                        ["value_template"] = """
                            {% set map = {
                              'off_peak': 'Off-peak',
                              'peak': 'Peak'
                            } %}
                            {{ map[value] }}
                            """,
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "binary_sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "280fd1d1-4220-42ec-bf70-69936b728eb2"u8),
                        ["icon"] = "mdi:transmission-tower-off",
                        ["name"] = ComputeEntityName(
                            name    : "Power cut mode active",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterPowerCutMode}",
                        ["payload_on"] = "1",
                        ["payload_off"] = "0"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "91e32508-61fa-46e3-ba57-32166b2de116"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Get rate type",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.SmartMeterRateType}/get",
                        ["payload_press"] = string.Empty
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "df24321d-01e8-4d1a-9cca-92ece18934b4"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Get power cut mode",
                            endpoint: endpoint,
                            setting : null,
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
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "46c1f892-f9bf-46b6-8658-fed1d7eb177b"u8),
                        ["device_class"] = "date",
                        ["name"] = ComputeEntityName(
                            name    : "Startup date",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.Uptime))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.StartupDate}",
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "5e9b5094-b028-492c-8be4-5b73139e2c57"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Get startup date",
                            endpoint: endpoint,
                            setting : null,
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
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "select",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "733d9bf6-fd89-4ce1-bd71-d12a1c6a846e"u8),
                        ["icon"] = "mdi:water-boiler",
                        ["name"] = ComputeEntityName(
                            name    : "Setpoint mode",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.WaterHeaterSetpointMode}/set",
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.WaterHeaterSetpointMode}",
                        ["options"] = new JsonArray(["Automatic", "Forced on", "Forced off"]),
                        ["value_template"] = """
                            {% set map = {
                              'automatic': 'Automatic',
                              'forced_on': 'Forced on',
                              'forced_off': 'Forced off'
                            } %}
                            {{ map[value] }}
                            """,
                        ["command_template"] = """
                            {% set map = {
                              'Automatic': 'automatic',
                              'Forced on': 'forced_on',
                              'Forced off': 'forced_off'
                            } %}
                            {{ map[value] }}
                            """
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "binary_sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "344b6548-196d-42de-b627-22530cc28f07"u8),
                        ["icon"] = "mdi:fire",
                        ["name"] = ComputeEntityName(
                            name    : "Heater active",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.WaterHeaterState}",
                        ["payload_on"] = "heating",
                        ["payload_off"] = "idle"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "9aa4633b-c253-4623-a80c-54ba9d681777"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Get setpoint mode",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.WaterHeaterSetpointMode}/get",
                        ["payload_press"] = string.Empty
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "bb186d7c-f49d-4aa2-8770-a0fdcf9de123"u8),
                        ["name"] = ComputeEntityName(
                            name    : "Get heater state",
                            endpoint: endpoint,
                            setting : null,
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
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "sensor",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "2dd476d5-a35a-442a-a3f2-4c2621dcf375"u8),
                        ["device_class"] = "enum",
                        ["icon"] = "mdi:shield-home-outline",
                        ["name"] = ComputeEntityName(
                            name    : "Alarm state",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.WirelessBurglarAlarmState))
                                .CountAsync(cancellationToken)),
                        ["state_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.WirelessBurglarAlarmState}",
                        ["options"] = new JsonArray(
                        [
                            "Disarmed",
                            "Armed",
                            "Partially armed",
                            "Exit delay elapsed",
                            "Triggered",
                            "Event detected"
                        ]),
                        ["value_template"] = """
                            {% set map = {
                              'disarmed': 'Disarmed',
                              'armed': 'Armed',
                              'partially_armed': 'Partially armed',
                              'exit_delay_elapsed': 'Exit delay elapsed',
                              'triggered': 'Triggered',
                              'event_detected': 'Event detected'
                            } %}
                            {{ map[value] }}
                            """,
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.ZigbeeBinding))
                {
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "d04dc07d-b614-4e3e-aeac-ccaead40d919"u8),
                        ["icon"] = "mdi:link",
                        ["name"] = ComputeEntityName(
                            name    : "Bind to gateway",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeBinding))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeBinding}/set",
                        ["payload_press"] = "bind"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "55a25c45-cb7a-4b74-baa4-7f9b87465d1a"u8),
                        ["icon"] = "mdi:link-off",
                        ["name"] = ComputeEntityName(
                            name    : "Unbind from gateway",
                            endpoint: endpoint,
                            setting : null,
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
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "f366a06c-6d7f-4741-a99a-490fedeabf9f"u8),
                        ["icon"] = "mdi:new-box",
                        ["name"] = ComputeEntityName(
                            name    : "Create and open Zigbee network",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeNetwork}/set",
                        ["payload_press"] = "create"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "1c56979a-3ad2-4b9b-9116-cd7321416b6a"u8),
                        ["icon"] = "mdi:download-network",
                        ["name"] = ComputeEntityName(
                            name    : "Join Zigbee network",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeNetwork}/set",
                        ["payload_press"] = "join"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "63096c5c-3fba-4ea7-a30d-3568b0680e18"u8),
                        ["icon"] = "mdi:upload-network",
                        ["name"] = ComputeEntityName(
                            name    : "Leave Zigbee network",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeNetwork}/set",
                        ["payload_press"] = "leave"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "09953b7d-0e18-4fc9-9b8c-2a11b52a15c5"u8),
                        ["icon"] = "mdi:lock-open",
                        ["name"] = ComputeEntityName(
                            name    : "Open Zigbee network",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeNetwork}/set",
                        ["payload_press"] = "open"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "7e344b36-199e-4a43-987f-59fccacc859b"u8),
                        ["icon"] = "mdi:lock",
                        ["name"] = ComputeEntityName(
                            name    : "Close Zigbee network",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeNetwork}/set",
                        ["payload_press"] = "close"
                    });
                }

                if (endpoint.HasCapability(OpenNettyCapabilities.ZigbeeSupervision))
                {
                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "f6a3d89f-a3a4-4776-a6b0-a0e3328183ee"u8),
                        ["icon"] = "mdi:eye-check",
                        ["name"] = ComputeEntityName(
                            name    : "Enable supervisor mode",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeSupervision))
                                .CountAsync(cancellationToken)),
                        ["command_topic"] = $"{options.RootTopic}/{topic}/{OpenNettyMqttAttributes.ZigbeeSupervision}/set",
                        ["payload_press"] = "enable"
                    });

                    components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", new JsonObject
                    {
                        ["platform"] = "button",
                        ["unique_id"] = ComputeEntityUniqueId(endpoint, "35f91e70-674d-4977-9475-ba231553051d"u8),
                        ["icon"] = "mdi:eye-remove",
                        ["name"] = ComputeEntityName(
                            name    : "Disable supervisor mode",
                            endpoint: endpoint,
                            setting : null,
                            count   : await _manager.EnumerateEndpointsAsync(cancellationToken)
                                .Where(endpoint => endpoint.Device == device)
                                .Where(endpoint => endpoint.HasCapability(OpenNettyCapabilities.ZigbeeSupervision))
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

        static JsonObject CreateDeviceNode(OpenNettyDevice device)
        {
            var node = new JsonObject
            {
                ["identifiers"] = new JsonArray([ComputeDeviceUniqueId(device)]),
                ["manufacturer"] = Enum.GetName(device.Identity.Brand),
                ["model"] = device.Identity.Description,
                ["model_id"] = device.Identity.Model,
                ["serial_number"] = device.Identifier.ToString(),
                ["name"] = $"{Enum.GetName(device.Identity.Brand)} {device.Identity.Model} ({device.Identifier})"
            };

            if (device.Identifier.Type is OpenNettyDeviceIdentifierType.MacAddress)
            {
                var address = OpenNettyDeviceIdentifier.ToMacAddress(device.Identifier);
                node["connections"] = new JsonArray([new JsonArray(["mac", address.ToString()])]);
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

        static string ComputeEntityName(string name, OpenNettyEndpoint endpoint, OpenNettySetting? setting, int count)
        {
            if (count is < 2)
            {
                return name;
            }

            if (setting is not null && endpoint.GetStringSetting(setting.Value) is { Length: > 0 } description)
            {
                return $"{name} [{description}]";
            }

            if (endpoint.Unit is not null)
            {
                return $"{name} [{endpoint.Unit.Definition.Description}]";
            }

            if (endpoint.Address is null)
            {
                return $"{name} [Local gateway]";
            }

            return name;
        }

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

            // If the endpoint also supports lighting commands, ensure the actuator type
            // associated with the endpoint is appropriate for the requested operation.
            if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchControl) ||
                endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingControl) ||
                endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingControl))
            {
                var type = endpoint.GetStringSetting(OpenNettySettings.ActuatorType);
                if (string.IsNullOrEmpty(type))
                {
                    throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
                }

                return type is OpenNettySettings.ActuatorTypes.Automation;
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

            // If the endpoint also supports automation commands, ensure the actuator
            // type associated with the endpoint is appropriate for a light entity.
            if (endpoint.HasCapability(OpenNettyCapabilities.BasicShutterControl) ||
                endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterControl))
            {
                var type = endpoint.GetStringSetting(OpenNettySettings.ActuatorType);
                if (string.IsNullOrEmpty(type))
                {
                    throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
                }

                return type is OpenNettySettings.ActuatorTypes.Lighting;
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
