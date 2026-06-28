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
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<OpenNettyMqttHostedService> _logger;
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
        ILogger<OpenNettyMqttHostedService> logger,
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
            await _events.ActionScenarioReported
                .Do(async arguments =>
                {
                    // Note: if the device class is "doorbell", a standard "ring" event is also sent for action scenarios.
                    if (arguments.Type is OpenNettyModels.ScenariosPlus.ActionScenarioType.Action &&
                        arguments.Endpoint.GetStringSetting(OpenNettySettings.HomeAssistantScenarioDeviceClass)
                            is OpenNettySettings.HomeAssistantDeviceClasses.Events.Doorbell)
                    {
                        await ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, builder =>
                        {
                            var node = new JsonObject
                            {
                                ["event_type"] = "ring"
                            };

                            builder.WithContentType(MediaTypeNames.Application.Json);
                            builder.WithPayload(node.ToJsonString());
                        });
                    }

                    await ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, builder =>
                    {
                        var node = new JsonObject
                        {
                            ["event_type"] = arguments.Type switch
                            {
                                OpenNettyModels.ScenariosPlus.ActionScenarioType.Action     => "action",
                                OpenNettyModels.ScenariosPlus.ActionScenarioType.StopAction => "stop_action",

                                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                            }
                        };

                        builder.WithContentType(MediaTypeNames.Application.Json);
                        builder.WithPayload(node.ToJsonString());
                    });
                })
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.AvailabilityReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.Availability, builder =>
                {
                    builder.WithPayload(arguments.Availability switch
                    {
                        OpenNettyModels.Diagnostics.Availability.Offline => "offline",
                        OpenNettyModels.Diagnostics.Availability.Online  => "online",

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                    });
                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.BatteryAlertReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.BatteryAlert, builder =>
                {
                    builder.WithPayload("ON");
                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.BatteryLevelReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.BatteryLevel, builder =>
                {
                    builder.WithPayload(arguments.Level.ToString(CultureInfo.InvariantCulture));
                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.BrightnessReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.Brightness, builder =>
                {
                    builder.WithPayload(arguments.Level.ToString(CultureInfo.InvariantCulture));
                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.DeviceCommunicationReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.LastCommunicationDate, builder =>
                {
                    builder.WithPayload(TimeProvider.System.GetUtcNow().ToString("o", CultureInfo.InvariantCulture));
                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.DimmingScenarioReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, builder =>
                {
                    var node = new JsonObject
                    {
                        ["event_type"] = "dimming",
                        ["dimming_step"] = arguments.Step
                    };

                    builder.WithContentType(MediaTypeNames.Application.Json);
                    builder.WithPayload(node.ToJsonString());
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.DryContactScenarioReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, builder =>
                {
                    var node = new JsonObject
                    {
                        ["event_type"] = arguments.Type switch
                        {
                            OpenNettyModels.ScenariosPlus.DryContactScenarioType.Closed => "contact_closed",
                            OpenNettyModels.ScenariosPlus.DryContactScenarioType.Open   => "contact_open",

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                        }
                    };

                    builder.WithContentType(MediaTypeNames.Application.Json);
                    builder.WithPayload(node.ToJsonString());
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.DryContactStateReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.DryContactState, builder =>
                {
                    var node = new JsonObject
                    {
                        ["state"] = arguments.State switch
                        {
                            OpenNettyModels.ScenariosPlus.DryContactState.Closed => "closed",
                            OpenNettyModels.ScenariosPlus.DryContactState.Open   => "open",

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                        },

                        ["origin"] = arguments.Origin switch
                        {
                            OpenNettyModels.ScenariosPlus.DryContactStateOrigin.StateRequest => "state_request",
                            OpenNettyModels.ScenariosPlus.DryContactStateOrigin.SystemEvent  => "system_event",

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                        }
                    };

                    builder.WithContentType(MediaTypeNames.Application.Json);
                    builder.WithPayload(node.ToJsonString());
                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.IncomingMessageReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.IncomingMessage, builder =>
                {
                    var node = new JsonObject
                    {
                        ["message"] = new JsonObject
                        {
                            ["parsed"] = arguments.Message.ToJsonObject(),
                            ["raw"] = arguments.Message.ToString()
                        },
                        ["session"] = new JsonObject
                        {
                            ["id"] = arguments.Session.Id.ToString(),
                            ["type"] = arguments.Session.Type switch
                            {
                                OpenNettySessionType.Command => "command",
                                OpenNettySessionType.Event   => "event",
                                OpenNettySessionType.Generic => "generic",

                                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                            }
                        }
                    };

                    builder.WithContentType(MediaTypeNames.Application.Json);
                    builder.WithPayload(node.ToJsonString());
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.OnOffScenarioReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, builder =>
                {
                    var node = new JsonObject
                    {
                        ["event_type"] = arguments.Type switch
                        {
                            OpenNettyModels.Lighting.OnOffScenarioType.Off => "switch_off",
                            OpenNettyModels.Lighting.OnOffScenarioType.On  => "switch_on",

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                        }
                    };

                    builder.WithContentType(MediaTypeNames.Application.Json);
                    builder.WithPayload(node.ToJsonString());
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.OutgoingMessageReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.OutgoingMessage, builder =>
                {
                    var node = new JsonObject
                    {
                        ["message"] = new JsonObject
                        {
                            ["parsed"] = arguments.Message.ToJsonObject(),
                            ["raw"] = arguments.Message.ToString()
                        },
                        ["session"] = new JsonObject
                        {
                            ["id"] = arguments.Session.Id.ToString(),
                            ["type"] = arguments.Session.Type switch
                            {
                                OpenNettySessionType.Command => "command",
                                OpenNettySessionType.Event   => "event",
                                OpenNettySessionType.Generic => "generic",

                                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                            }
                        }
                    };

                    builder.WithContentType(MediaTypeNames.Application.Json);
                    builder.WithPayload(node.ToJsonString());
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.PilotWireDerogationModeReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.PilotWireDerogationMode, builder =>
                {
                    builder.WithPayload(arguments.Mode switch
                    {
                        OpenNettyModels.TemperatureControl.PilotWireMode.Comfort => arguments.Duration switch
                        {
                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None       => "comfort",
                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours  => "comfort:4h",
                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours => "comfort:8h",

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                        },

                        OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne => arguments.Duration switch
                        {
                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None       => "comfort-1",
                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours  => "comfort-1:4h",
                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours => "comfort-1:8h",

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                        },

                        OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo => arguments.Duration switch
                        {
                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None       => "comfort-2",
                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours  => "comfort-2:4h",
                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours => "comfort-2:8h",

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                        },

                        OpenNettyModels.TemperatureControl.PilotWireMode.Eco => arguments.Duration switch
                        {
                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None       => "eco",
                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours  => "eco:4h",
                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours => "eco:8h",

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                        },

                        OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection => arguments.Duration switch
                        {
                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None       => "frost_protection",
                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours  => "frost_protection:4h",
                            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours => "frost_protection:8h",

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                        },

                        _ => "none"
                    });

                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.PilotWireSetpointModeReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.PilotWireSetpointMode, builder =>
                {
                    builder.WithPayload(arguments.Mode switch
                    {
                        OpenNettyModels.TemperatureControl.PilotWireMode.Comfort         => "comfort",
                        OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne => "comfort-1",
                        OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo => "comfort-2",
                        OpenNettyModels.TemperatureControl.PilotWireMode.Eco             => "eco",
                        OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection => "frost_protection",

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                    });

                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.PilotWireShutdownModeReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.PilotWireShutdownMode, builder =>
                {
                    builder.WithPayload(arguments.Active ? "ON" : "OFF");
                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.PressureScenarioReported
                .Do(async arguments =>
                {
                    // Note: if the device class is "doorbell", a standard "ring" event is also sent for short pressure scenarios.
                    if (arguments.Type is OpenNettyModels.Scenarios.PressureScenarioType.Pressure &&
                        arguments.Endpoint.GetStringSetting(OpenNettySettings.HomeAssistantScenarioDeviceClass)
                            is OpenNettySettings.HomeAssistantDeviceClasses.Events.Doorbell)
                    {
                        await ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, builder =>
                        {
                            var node = new JsonObject
                            {
                                ["event_type"] = "ring",
                                ["scenario_type"] = "evolved",
                                ["button"] = arguments.Button
                            };

                            builder.WithContentType(MediaTypeNames.Application.Json);
                            builder.WithPayload(node.ToJsonString());
                        });
                    }

                    await ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, builder =>
                    {
                        var node = new JsonObject
                        {
                            ["event_type"] = arguments.Type switch
                            {
                                OpenNettyModels.Scenarios.PressureScenarioType.Pressure                     => "pressure",
                                OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterShortPressure    => "release_after_short_pressure",
                                OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterExtendedPressure => "release_after_extended_pressure",
                                OpenNettyModels.Scenarios.PressureScenarioType.ExtendedPressure             => "extended_pressure",

                                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                            },
                            ["scenario_type"] = "evolved",
                            ["button"] = arguments.Button
                        };

                        builder.WithContentType(MediaTypeNames.Application.Json);
                        builder.WithPayload(node.ToJsonString());
                    });
                })
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.PressureScenarioPlusReported
                .Do(async arguments =>
                {
                    // Note: if the device class is "doorbell", a standard "ring" event is also sent for short pressure scenarios.
                    if (arguments.Type is OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure &&
                        arguments.Endpoint.GetStringSetting(OpenNettySettings.HomeAssistantScenarioDeviceClass)
                            is OpenNettySettings.HomeAssistantDeviceClasses.Events.Doorbell)
                    {
                        await ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, builder =>
                        {
                            var node = new JsonObject
                            {
                                ["event_type"] = "ring",
                                ["scenario_type"] = "plus",
                                ["button"] = arguments.Button
                            };

                            builder.WithContentType(MediaTypeNames.Application.Json);
                            builder.WithPayload(node.ToJsonString());
                        });
                    }

                    await ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, builder =>
                    {
                        var node = new JsonObject
                        {
                            ["event_type"] = arguments.Type switch
                            {
                                OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure           => "short_pressure",
                                OpenNettyModels.ScenariosPlus.PressureScenarioType.StartOfExtendedPressure => "start_of_extended_pressure",
                                OpenNettyModels.ScenariosPlus.PressureScenarioType.ExtendedPressure        => "extended_pressure",
                                OpenNettyModels.ScenariosPlus.PressureScenarioType.EndOfExtendedPressure   => "end_of_extended_pressure",

                                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                            },
                            ["scenario_type"] = "plus",
                            ["button"] = arguments.Button
                        };

                        builder.WithContentType(MediaTypeNames.Application.Json);
                        builder.WithPayload(node.ToJsonString());
                    });
                })
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.ProgressiveScenarioReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, builder =>
                {
                    var node = new JsonObject
                    {
                        ["event_type"] = "progressive_action",
                        ["duration"] = arguments.Duration.TotalSeconds
                    };

                    builder.WithContentType(MediaTypeNames.Application.Json);
                    builder.WithPayload(node.ToJsonString());
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.ShutterPositionReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.ShutterPosition, builder =>
                {
                    builder.WithPayload(arguments.Position.ToString(CultureInfo.InvariantCulture));
                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.ShutterStateReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.ShutterState, builder =>
                {
                    builder.WithPayload(arguments.State switch
                    {
                        OpenNettyModels.Automation.ShutterState.Stopped => "stopped",
                        OpenNettyModels.Automation.ShutterState.Opening => "opening",
                        OpenNettyModels.Automation.ShutterState.Closing => "closing",
                        OpenNettyModels.Automation.ShutterState.Open    => "open",
                        OpenNettyModels.Automation.ShutterState.Closed  => "closed",

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                    });

                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.SmartMeterPowerCutModeReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.SmartMeterPowerCutMode, builder =>
                {
                    builder.WithPayload(arguments.Active ? "ON" : "OFF");
                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.SmartMeterRateTypeReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.SmartMeterRateType, builder =>
                {
                    builder.WithPayload(arguments.Type switch
                    {
                        OpenNettyModels.TemperatureControl.SmartMeterRateType.Peak    => "peak",
                        OpenNettyModels.TemperatureControl.SmartMeterRateType.OffPeak => "off_peak",

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                    });

                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.StopUpDownScenarioReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, builder =>
                {
                    var node = new JsonObject
                    {
                        ["event_type"] = arguments.Type switch
                        {
                            OpenNettyModels.Automation.StopUpDownScenarioType.Stop => "shutter_stop",
                            OpenNettyModels.Automation.StopUpDownScenarioType.Up   => "shutter_up",
                            OpenNettyModels.Automation.StopUpDownScenarioType.Down => "shutter_down",

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                        }
                    };

                    builder.WithContentType(MediaTypeNames.Application.Json);
                    builder.WithPayload(node.ToJsonString());
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.SwitchStateReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.SwitchState, builder =>
                {
                    builder.WithPayload(arguments.State is OpenNettyModels.Lighting.SwitchState.Off ? "OFF": "ON");
                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.TimedScenarioReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, builder =>
                {
                    var node = new JsonObject
                    {
                        ["event_type"] = "timed_action",
                        ["duration"] = arguments.Duration.TotalSeconds
                    };

                    builder.WithContentType(MediaTypeNames.Application.Json);
                    builder.WithPayload(node.ToJsonString());
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.ToggleScenarioReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.Scenario, builder =>
                {
                    var node = new JsonObject
                    {
                        ["event_type"] = "switch_toggle"
                    };

                    builder.WithContentType(MediaTypeNames.Application.Json);
                    builder.WithPayload(node.ToJsonString());
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.WaterHeaterSetpointModeReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.WaterHeaterSetpointMode, builder =>
                {
                    builder.WithPayload(arguments.Mode switch
                    {
                        OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic => "automatic",
                        OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOff => "forced_off",
                        OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn  => "forced_on",

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                    });

                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.WaterHeaterStateReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.WaterHeaterState, builder =>
                {
                    builder.WithPayload(arguments.State switch
                    {
                        OpenNettyModels.TemperatureControl.WaterHeaterState.Idle    => "idle",
                        OpenNettyModels.TemperatureControl.WaterHeaterState.Heating => "heating",

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                    });

                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.WirelessBurglarAlarmStateReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.WirelessBurglarAlarmState, builder =>
                {
                    builder.WithPayload(arguments.State switch
                    {
                        OpenNettyModels.Alarm.WirelessBurglarAlarmState.Disarmed         => "disarmed",
                        OpenNettyModels.Alarm.WirelessBurglarAlarmState.Armed            => "armed",
                        OpenNettyModels.Alarm.WirelessBurglarAlarmState.PartiallyArmed   => "partially_armed",
                        OpenNettyModels.Alarm.WirelessBurglarAlarmState.ExitDelayElapsed => "exit_delay_elapsed",
                        OpenNettyModels.Alarm.WirelessBurglarAlarmState.Triggered        => "triggered",
                        OpenNettyModels.Alarm.WirelessBurglarAlarmState.EventDetected    => "event_detected",

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                    });

                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.ZigbeeBindingEventReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.ZigbeeBinding, builder =>
                {
                    builder.WithPayload(arguments.Type switch
                    {
                        OpenNettyModels.ScenariosPlus.ZigbeeBindingEventType.Closed   => "closed",
                        OpenNettyModels.ScenariosPlus.ZigbeeBindingEventType.Canceled => "canceled",
                        OpenNettyModels.ScenariosPlus.ZigbeeBindingEventType.Open     => "open",

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                    });

                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask),

            await _events.ZigbeeNetworkEventReported
                .Do(arguments => ReportAsync(arguments.Endpoint, OpenNettyMqttAttributes.ZigbeeNetwork, builder =>
                {
                    builder.WithPayload(arguments.Type switch
                    {
                        OpenNettyModels.Management.ZigbeeNetworkEventType.Closed  => "closed",
                        OpenNettyModels.Management.ZigbeeNetworkEventType.Created => "created",
                        OpenNettyModels.Management.ZigbeeNetworkEventType.Joined  => "joined",
                        OpenNettyModels.Management.ZigbeeNetworkEventType.Left    => "left",
                        OpenNettyModels.Management.ZigbeeNetworkEventType.Open    => "open",

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                    });

                    builder.WithRetainFlag();
                }))
                .Retry()
                .SubscribeAsync(static arguments => ValueTask.CompletedTask)
        ]);

        async ValueTask ReportAsync(OpenNettyEndpoint endpoint, string attribute, Action<MqttApplicationMessageBuilder> configuration)
        {
            var builder = new MqttApplicationMessageBuilder()
                .WithPayloadFormatIndicator(MqttPayloadFormatIndicator.CharacterData)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                .WithTopic(GetMessageTopic(endpoint, attribute));

            configuration(builder);

            await _client.EnqueueAsync(builder.Build());
        }

        string GetMessageTopic(OpenNettyEndpoint endpoint, string attribute) => new StringBuilder()
            .Append(_options.CurrentValue.RootTopic)
            .Append('/')
            .Append(endpoint.GetStringSetting(OpenNettySettings.MqttTopic) ?? endpoint.Name.ToLowerInvariant())
            .Append('/')
            .Append(attribute)
            .ToString();
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

        // Use the ApplicationMessageReceivedAsync event to log incoming messages.
        //
        // Note: MQTTnet's ApplicationMessageReceivedAsync event is never called concurrently
        // even when multiple messages are received at the same time (which guarantees that
        // messages can be processed in the same order as they are received). As such, it is
        // safe to set SingleWriter to true in the channel options.
        _client.ApplicationMessageReceivedAsync += async (MqttApplicationMessageReceivedEventArgs arguments) =>
        {
            var message = arguments.ApplicationMessage;
            var topic = message.Topic;

            // Note: ignore all the incoming MQTT messages that don't end with /get or /set.
            if (topic is not { Length: > 4 } || topic.AsSpan()[^4..] is not ("/get" or "/set"))
            {
                return;
            }

            var payload = message.ConvertPayloadToString();

            _logger.LogDebug(6021, SR.GetResourceString(SR.ID6021), topic, payload);

            await channel.Writer.WriteAsync(arguments.ApplicationMessage);
        };

        // Use the ConnectingFailedAsync event to log connection errors.
        _client.ConnectingFailedAsync += (ConnectingFailedEventArgs arguments) =>
        {
            _logger.LogWarning(6019, arguments.Exception, SR.GetResourceString(SR.ID6019));

            return Task.CompletedTask;
        };

        // Use the ApplicationMessageProcessedAsync event to log successful and failed outgoing messages.
        _client.ApplicationMessageProcessedAsync += (ApplicationMessageProcessedEventArgs arguments) =>
        {
            var message = arguments.ApplicationMessage.ApplicationMessage;
            var topic = message.Topic;
            var payload = message.ConvertPayloadToString();

            if (arguments.Exception is Exception exception)
            {
                _logger.LogError(6023, arguments.Exception, SR.GetResourceString(SR.ID6023), topic, payload);
            }

            else
            {
                _logger.LogDebug(6022, SR.GetResourceString(SR.ID6022), topic, payload);
            }

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
            await Task.WhenAll(
                _worker.ProcessMessagesAsync(_client, channel.Reader, stoppingToken),
                _worker.AnnounceEndpointsAsync(_client, stoppingToken));
        }

        finally
        {
            // Stop the managed MQTT client.
            await _client.StopAsync();
        }
    }
}
