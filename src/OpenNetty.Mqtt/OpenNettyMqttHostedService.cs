/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using System.Globalization;
using System.Net.Mime;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;

namespace OpenNetty.Mqtt;

/// <summary>
/// Contains the logic necessary to propage OpenNetty events over MQTT.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class OpenNettyMqttHostedService : BackgroundService, IOpenNettyHandler
{
    private readonly IManagedMqttClient _client;
    private readonly OpenNettyEvents _events;
    private readonly OpenNettyLogger<OpenNettyMqttHostedService> _logger;
    private readonly IOptionsMonitor<OpenNettyMqttOptions> _options;
    private readonly IOpenNettyMqttWorker _worker;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyMqttHostedService"/> class.
    /// </summary>
    /// <param name="client">The MQTT client.</param>
    /// <param name="events">The OpenNetty events.</param>
    /// <param name="logger">The OpenNetty logger.</param>
    /// <param name="options">The OpenNetty MQTT options.</param>
    /// <param name="worker">The OpenNetty MQTT worker.</param>
    public OpenNettyMqttHostedService(
        IManagedMqttClient client,
        OpenNettyEvents events,
        OpenNettyLogger<OpenNettyMqttHostedService> logger,
        IOptionsMonitor<OpenNettyMqttOptions> options,
        IOpenNettyMqttWorker worker)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
    }

    /// <inheritdoc/>
    async ValueTask<IAsyncDisposable> IOpenNettyHandler.SubscribeAsync()
    {
        return StableCompositeAsyncDisposable.Create(
        [
            await _events.BasicScenarioReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportStringAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, "action"))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.BatteryLevelReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportStringAsync(arguments.Endpoint, OpenNettyMqttAttributes.Battery,
                    arguments.Level.ToString(CultureInfo.InvariantCulture)))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.BrightnessReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportStringAsync(arguments.Endpoint, OpenNettyMqttAttributes.Brightness,
                    arguments.Level.ToString(CultureInfo.InvariantCulture)))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.DimmingStepReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportStringAsync(arguments.Endpoint, OpenNettyMqttAttributes.DimmingStep,
                    arguments.Delta.ToString(CultureInfo.InvariantCulture)))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.OnOffScenarioReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportStringAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario,
                    arguments.State is OpenNettyModels.Lighting.SwitchState.Off ? "OFF" : "ON"))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.PilotWireDerogationModeReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportStringAsync(arguments.Endpoint, OpenNettyMqttAttributes.PilotWireDerogationMode, arguments.Mode switch
                {
                    OpenNettyModels.TemperatureControl.PilotWireMode.Comfort => arguments.Duration switch
                    {
                        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None       => "comfort",
                        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours  => "comfort:4h",
                        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours => "comfort:8h",

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                    },

                    OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne => arguments.Duration switch
                    {
                        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None       => "comfort-1",
                        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours  => "comfort-1:4h",
                        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours => "comfort-1:8h",

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                    },

                    OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo => arguments.Duration switch
                    {
                        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None       => "comfort-2",
                        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours  => "comfort-2:4h",
                        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours => "comfort-2:8h",

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                    },

                    OpenNettyModels.TemperatureControl.PilotWireMode.Eco => arguments.Duration switch
                    {
                        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None       => "eco",
                        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours  => "eco:4h",
                        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours => "eco:8h",

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                    },

                    OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection => arguments.Duration switch
                    {
                        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None       => "frost_protection",
                        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours  => "frost_protection:4h",
                        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours => "frost_protection:8h",

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                    },

                    _ => "none"
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.PilotWireSetpointModeReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportStringAsync(arguments.Endpoint, OpenNettyMqttAttributes.PilotWireSetpointMode, arguments.Mode switch
                {
                    OpenNettyModels.TemperatureControl.PilotWireMode.Comfort         => "comfort",
                    OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne => "comfort-1",
                    OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo => "comfort-2",
                    OpenNettyModels.TemperatureControl.PilotWireMode.Eco             => "eco",
                    OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection => "frost_protection",

                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.ProgressiveScenarioReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportJsonAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, new JsonObject()
                {
                    ["scenario_type"] = "progressive",
                    ["duration"] = arguments.Duration.TotalSeconds
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.SmartMeterIndexesReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportJsonAsync(arguments.Endpoint, OpenNettyMqttAttributes.SmartMeterIndexes, new JsonObject()
                {
                    ["base_index"]        = arguments.Indexes.BaseIndex,
                    ["blue_index"]        = arguments.Indexes.BlueIndex,
                    ["off_peak_index"]    = arguments.Indexes.OffPeakIndex,
                    ["red_index"]         = arguments.Indexes.RedIndex,
                    ["white_index"]       = arguments.Indexes.WhiteIndex,
                    ["subscription_type"] = arguments.Indexes.SubscriptionType switch
                    {
                        OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.Base    => "base",
                        OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.OffPeak => "off_peak",
                        OpenNettyModels.TemperatureControl.SmartMeterSubscriptionType.Tempo   => "tempo",

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                    }
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.SmartMeterPowerCutModeReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportStringAsync(arguments.Endpoint, OpenNettyMqttAttributes.IsPowerCutActive,
                    arguments.Active ? "1" : "0"))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.SmartMeterRateTypeReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportStringAsync(arguments.Endpoint, OpenNettyMqttAttributes.RateType, arguments.Type switch
                {
                    OpenNettyModels.TemperatureControl.SmartMeterRateType.Peak    => "peak",
                    OpenNettyModels.TemperatureControl.SmartMeterRateType.OffPeak => "off_peak",

                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.SwitchStateReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportStringAsync(arguments.Endpoint, OpenNettyMqttAttributes.SwitchState,
                    arguments.State is OpenNettyModels.Lighting.SwitchState.Off ? "OFF": "ON"))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.TimedScenarioReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportJsonAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, new JsonObject()
                {
                    ["scenario_type"] = "timed",
                    ["duration"] = arguments.Duration.TotalSeconds
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.ToggleScenarioReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportStringAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, "toggle"))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.WaterHeaterSetpointModeReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportStringAsync(arguments.Endpoint, OpenNettyMqttAttributes.WaterHeaterSetpointMode, arguments.Mode switch
                {
                    OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic => "automatic",
                    OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOff => "forced_off",
                    OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn  => "forced_on",

                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.WaterHeaterStateReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportStringAsync(arguments.Endpoint, OpenNettyMqttAttributes.WaterHeaterState, arguments.State switch
                {
                    OpenNettyModels.TemperatureControl.WaterHeaterState.Idle    => "idle",
                    OpenNettyModels.TemperatureControl.WaterHeaterState.Heating => "heating",

                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.WirelessBurglarAlarmStateReported
                .Where(static arguments => !string.IsNullOrEmpty(arguments.Endpoint.Name))
                .Do(arguments => ReportStringAsync(arguments.Endpoint, OpenNettyMqttAttributes.WirelessBurglarAlarmState, arguments.State switch
                {
                    OpenNettyModels.Alarm.WirelessBurglarAlarmState.Disarmed         => "disarmed",
                    OpenNettyModels.Alarm.WirelessBurglarAlarmState.Armed            => "armed",
                    OpenNettyModels.Alarm.WirelessBurglarAlarmState.PartiallyArmed   => "partially_armed",
                    OpenNettyModels.Alarm.WirelessBurglarAlarmState.ExitDelayElapsed => "exit_delay_elapsed",
                    OpenNettyModels.Alarm.WirelessBurglarAlarmState.Triggered        => "triggered",
                    OpenNettyModels.Alarm.WirelessBurglarAlarmState.EventDetected    => "event_detected",

                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask)
        ]);

        async ValueTask ReportStringAsync(OpenNettyEndpoint endpoint, string attribute, string value)
        {
            var topic = GetMessageTopic(endpoint, attribute);
            if (string.IsNullOrEmpty(topic))
            {
                return;
            }

            await _client.EnqueueAsync(new MqttApplicationMessageBuilder()
                .WithPayload(value)
                .WithPayloadFormatIndicator(MqttPayloadFormatIndicator.CharacterData)
                .WithTopic(topic)
                .Build());
        }

        async ValueTask ReportJsonAsync(OpenNettyEndpoint endpoint, string attribute, JsonNode value)
        {
            var topic = GetMessageTopic(endpoint, attribute);
            if (string.IsNullOrEmpty(topic))
            {
                return;
            }

            await _client.EnqueueAsync(new MqttApplicationMessageBuilder()
                .WithContentType(MediaTypeNames.Application.Json)
                .WithPayload(value.ToJsonString())
                .WithPayloadFormatIndicator(MqttPayloadFormatIndicator.CharacterData)
                .WithTopic(topic)
                .Build());
        }

        string? GetMessageTopic(OpenNettyEndpoint endpoint, string attribute)
        {
            var name = _options.CurrentValue.EndpointNameProvider(endpoint);
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            return new StringBuilder()
                .Append(_options.CurrentValue.RootTopic)
                .Append('/')
                .Append(name)
                .Append('/')
                .Append(attribute)
                .ToString();
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.CurrentValue.ClientOptions is not MqttClientOptions options)
        {
            return;
        }

        // Create the channel that will be used to dispatch MQTT messages to multiple handlers
        // to allow for parallel processing of commands. Note: operations pointing to the same
        // endpoint are always handled serially to ensure the order of commands is respected.
        var channel = Channel.CreateUnbounded<MqttApplicationMessage>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = false,
            SingleWriter = true
        });

        // Note: MQTTnet's ApplicationMessageReceivedAsync event is never called concurrently
        // even when multiple messages are received at the same time (which guarantees that
        // messages can be processed in the same order as they are received). As such, it is
        // safe to set SingleWriter to true in the channel options.
        _client.ApplicationMessageReceivedAsync += async (MqttApplicationMessageReceivedEventArgs arguments) =>
        {
            await channel.Writer.WriteAsync(arguments.ApplicationMessage);
        };

        // Use the ConnectingFailedAsync event to log connection errors.
        _client.ConnectingFailedAsync += (ConnectingFailedEventArgs arguments) =>
        {
            _logger.MqttBrokerConnectionError(arguments.Exception);

            return Task.CompletedTask;
        };

        // Start the managed MQTT client.
        await _client.StartAsync(new ManagedMqttClientOptions
        {
            ClientOptions = options
        });

        try
        {
            await _client.SubscribeAsync($"{_options.CurrentValue.RootTopic}/#", MqttQualityOfServiceLevel.ExactlyOnce);

            // Ask the worker to process incoming messages for this MQTT client.
            await _worker.ProcessMessagesAsync(_client, channel.Reader, stoppingToken);
        }

        finally
        {
            // Stop the managed MQTT client.
            await _client.StopAsync();
        }
    }
}
