/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Buffers.Text;
using System.Globalization;
using System.Net.Mime;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
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
    private readonly OpenNettyLogger<OpenNettyMqttWorker> _logger;
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
        OpenNettyLogger<OpenNettyMqttWorker> logger,
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
                    await _manager.FindEndpointByNameAsync(name) is not OpenNettyEndpoint endpoint)
                {
                    return AsyncObservable.Empty<(MqttApplicationMessage Message, OpenNettyEndpoint Endpoint, string Attribute, OpenNettyMqttOperation Operation)>();
                }

                return AsyncObservable.Return((Message: message, Endpoint: endpoint, Attribute: attribute, Operation: operation.Value));
            })
            .GroupBy(static arguments => arguments.Endpoint.Name)
            .Do(async group => await group
                .ObserveOn(TaskPoolAsyncScheduler.Default)
                .Do(async arguments =>
                {
                    var (message, endpoint, attribute, operation) = arguments;

                    try
                    {
                        switch (attribute.ToLowerInvariant())
                        {
                            case OpenNettyMqttAttributes.Brightness when operation is OpenNettyMqttOperation.Get:
                                _ = await _controller.EnumerateBrightnessAsync(endpoint).ToListAsync();
                                break;

                            case OpenNettyMqttAttributes.Brightness when operation is OpenNettyMqttOperation.Set:
                                if (!byte.TryParse(message.PayloadSegment, CultureInfo.InvariantCulture, out var level))
                                {
                                    throw new InvalidDataException(SR.GetResourceString(SR.ID0075));
                                }

                                await _controller.SetBrightnessAsync(endpoint, level);
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
                                    case "action" when endpoint.HasCapability(OpenNettyCapabilities.BasicScenario):
                                        await _controller.DispatchBasicScenarioAsync(endpoint);
                                        break;

                                    case "down" when endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenario):
                                        await _controller.DispatchShutterDownScenarioAsync(endpoint);
                                        break;

                                    case "on" when endpoint.HasCapability(OpenNettyCapabilities.OnOffScenario):
                                        await _controller.DispatchOnScenarioAsync(endpoint);
                                        break;

                                    case "off" when endpoint.HasCapability(OpenNettyCapabilities.OnOffScenario):
                                        await _controller.DispatchOffScenarioAsync(endpoint);
                                        break;

                                    case "stop" when endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenario):
                                        await _controller.DispatchShutterStopScenarioAsync(endpoint);
                                        break;

                                    case "up" when endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenario):
                                        await _controller.DispatchShutterUpScenarioAsync(endpoint);
                                        break;
                                }
                                break;

                            case OpenNettyMqttAttributes.ShutterPosition when operation is OpenNettyMqttOperation.Get:
                                _ = await _controller.EnumerateShutterPositionsAsync(endpoint).ToListAsync();
                                break;

                            case OpenNettyMqttAttributes.ShutterPosition when operation is OpenNettyMqttOperation.Set:
                                if (!byte.TryParse(message.PayloadSegment, CultureInfo.InvariantCulture, out var position))
                                {
                                    throw new InvalidDataException(SR.GetResourceString(SR.ID0075));
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
                                        await _controller.MoveShutterUpAsync(endpoint);
                                        break;

                                    case "open":
                                        await _controller.MoveShutterDownAsync(endpoint);
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
                .Do(_logger.UnhandledEventHandlerException)
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask))
            .Retry()
            .SubscribeAsync(static arguments => ValueTask.CompletedTask);

        await WaitCancellationAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AnnounceEndpointsAsync(IManagedMqttClient client, CancellationToken cancellationToken)
    {
        if (_options.CurrentValue is not { DisableDiscovery: false } options)
        {
            return;
        }

        await foreach (var endpoints in from endpoint in _manager.EnumerateEndpointsAsync(cancellationToken)
                                        where endpoint.Device is { SerialNumber.Length: > 0 }
                                        where endpoint.Device!.GetBooleanSetting(OpenNettySettings.MqttDiscovery) is not false
                                        group endpoint by endpoint.Device!)
        {
            var components = new JsonObject();

            var device = new JsonObject
            {
                ["identifiers"] = new JsonArray([endpoints.Key.SerialNumber]),
                ["manufacturer"] = Enum.GetName(endpoints.Key.Identity.Brand),
                ["model"] = endpoints.Key.Identity.Model,
                ["serial_number"] = endpoints.Key.SerialNumber,
                ["name"] = $"{Enum.GetName(endpoints.Key.Identity.Brand)} {endpoints.Key.Identity.Model} ({endpoints.Key.SerialNumber})"
            };

            if (endpoints.Key.GetStringSetting(OpenNettySettings.HomeAssistantSuggestedArea) is { Length: > 0 } area)
            {
                device["suggested_area"] = area;
            }

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
                ["device"] = device,
                ["components"] = components,
                ["qos"] = 2
            };

            await foreach (var (endpoint, name) in from endpoint in endpoints
                                                   where endpoint.GetBooleanSetting(OpenNettySettings.MqttDiscovery) is not false
                                                   let name = options.EndpointNameProvider(endpoint)
                                                   where !string.IsNullOrEmpty(name)
                                                   orderby name
                                                   select (Endpoint: endpoint, Name: name))
            {
                switch (endpoint.GetStringSetting(OpenNettySettings.HomeAssistantEntityType))
                {
                    case "Light":
                    case null when endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitching) &&
                                   endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState):
                    {
                        var component = new JsonObject
                        {
                            ["platform"] = "light",
                            ["unique_id"] = Base64Url.EncodeToString(SHA256.HashData(
                            [
                                ..Encoding.UTF8.GetBytes(name),
                                ..Encoding.UTF8.GetBytes(endpoint.Address?.Value ?? string.Empty)
                            ])),
                            ["name"] = endpoint.Name
                        };

                        if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitching))
                        {
                            component["command_topic"] = $"{options.RootTopic}/{name}/{OpenNettyMqttAttributes.SwitchState}/set";
                        }

                        if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                        {
                            component["state_topic"] = $"{options.RootTopic}/{name}/{OpenNettyMqttAttributes.SwitchState}";
                        }

                        if (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingControl) ||
                            endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingControl))
                        {
                            component["brightness_command_topic"] = $"{options.RootTopic}/{name}/{OpenNettyMqttAttributes.Brightness}/set";
                            component["brightness_scale"] = 100;
                            component["on_command_type"] = "brightness";
                        }

                        if (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                            endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                        {
                            component["brightness_state_topic"] = $"{options.RootTopic}/{name}/{OpenNettyMqttAttributes.Brightness}";
                        }

                        components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", component);
                        break;
                    }

                    case "Switch":
                    {
                        var component = new JsonObject
                        {
                            ["platform"] = "switch",
                            ["unique_id"] = Base64Url.EncodeToString(SHA256.HashData(
                            [
                                ..Encoding.UTF8.GetBytes(name),
                                ..Encoding.UTF8.GetBytes(endpoint.Address?.Value ?? string.Empty)
                            ])),
                            ["name"] = endpoint.Name,
                            ["device_class"] = endpoint.GetStringSetting(OpenNettySettings.HomeAssistantDeviceClass) ?? "switch"
                        };

                        if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitching))
                        {
                            component["command_topic"] = $"{options.RootTopic}/{name}/{OpenNettyMqttAttributes.SwitchState}/set";
                        }

                        if (endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                        {
                            component["state_topic"] = $"{options.RootTopic}/{name}/{OpenNettyMqttAttributes.SwitchState}";
                        }

                        components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", component);
                        break;
                    }

                    case "Cover":
                    case null when endpoint.HasCapability(OpenNettyCapabilities.BasicShutterControl) &&
                                   endpoint.HasCapability(OpenNettyCapabilities.BasicShutterState):
                    {
                        var component = new JsonObject
                        {
                            ["platform"] = "cover",
                            ["unique_id"] = Base64Url.EncodeToString(SHA256.HashData(
                            [
                                ..Encoding.UTF8.GetBytes(name),
                                ..Encoding.UTF8.GetBytes(endpoint.Address?.Value ?? string.Empty)
                            ])),
                            ["name"] = endpoint.Name,
                            ["device_class"] = endpoint.GetStringSetting(OpenNettySettings.HomeAssistantDeviceClass) ?? "shutter"
                        };

                        if (endpoint.HasCapability(OpenNettyCapabilities.BasicShutterControl))
                        {
                            component["command_topic"] = $"{options.RootTopic}/{name}/{OpenNettyMqttAttributes.ShutterState}/set";
                        }

                        if (endpoint.HasCapability(OpenNettyCapabilities.BasicShutterState))
                        {
                            component["state_topic"] = $"{options.RootTopic}/{name}/{OpenNettyMqttAttributes.ShutterState}";
                        }

                        if (endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterControl))
                        {
                            component["set_position_topic"] = $"{options.RootTopic}/{name}/{OpenNettyMqttAttributes.ShutterPosition}/set";
                        }

                        if (endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
                        {
                            component["position_topic"] = $"{options.RootTopic}/{name}/{OpenNettyMqttAttributes.ShutterPosition}";
                        }

                        components.Add($"entity{components.Count.ToString(CultureInfo.InvariantCulture)}", component);
                        break;
                    }
                }
            }

            if (components.Count is not 0)
            {
                var node = $"opennetty-{Enum.GetName(endpoints.Key.Definition.Protocol)!.ToLowerInvariant()}";
                var topic = $"{options.DiscoveryRootTopic}/device/{node}/{endpoints.Key.SerialNumber}/config";

                await client.EnqueueAsync(new MqttApplicationMessageBuilder()
                    .WithContentType(MediaTypeNames.Application.Json)
                    .WithPayload(configuration.ToJsonString())
                    .WithPayloadFormatIndicator(MqttPayloadFormatIndicator.CharacterData)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                    .WithRetainFlag()
                    .WithTopic(topic)
                    .Build());
            }
        }
    }

    static (string? FriendlyName, string? attribute, OpenNettyMqttOperation? Operation) ExtractParameters(MqttApplicationMessage message)
        => message.Topic.Split('/', StringSplitOptions.RemoveEmptyEntries) switch
        {
            [_, .. string[] topics, string attribute, string operation] when string.Equals(operation, "get", StringComparison.OrdinalIgnoreCase)
                => (string.Join('/', topics), attribute, OpenNettyMqttOperation.Get),

            [_, .. string[] topics, string attribute, string operation] when string.Equals(operation, "set", StringComparison.OrdinalIgnoreCase)
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
