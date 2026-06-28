/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace OpenNetty;

/// <summary>
/// Exposes high-level events that are automatically inferred from
/// incoming or outgoing OpenWebNet frames by the OpenNetty coordinator.
/// </summary>
public sealed class OpenNettyEvents : IDisposable
{
    private readonly Channel<EventArgs> _channel = Channel.CreateUnbounded<EventArgs>();
    private readonly IConnectableAsyncObservable<EventArgs> _observable;
    private readonly CancellationTokenRegistration _registration;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyEvents"/> class.
    /// </summary>
    /// <param name="lifetime">The host application lifetime.</param>
    public OpenNettyEvents(IHostApplicationLifetime lifetime)
    {
        _observable = AsyncObservable.Create<EventArgs>(observer =>
        {
            return TaskPoolAsyncScheduler.Default.ScheduleAsync(async cancellationToken =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (!await _channel.Reader.WaitToReadAsync(cancellationToken))
                        {
                            await observer.OnCompletedAsync();
                            return;
                        }

                        while (_channel.Reader.TryRead(out EventArgs? arguments))
                        {
                            await observer.OnNextAsync(arguments);
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
            });
        })
        .Retry()
        .Multicast(new ConcurrentSimpleAsyncSubject<EventArgs>());

        // Marks the channel as completed when the host indicates the application is shutting down.
        _registration = lifetime.ApplicationStopping.Register(static state =>
            ((OpenNettyEvents) state!)._channel.Writer.TryComplete(), this);
    }

    /// <summary>
    /// Gets an event triggered when an action scenario is reported.
    /// </summary>
    public IAsyncObservable<ActionScenarioReportedEventArgs> ActionScenarioReported
        => _observable.OfType<EventArgs, ActionScenarioReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when the availability of an endpoint is reported.
    /// </summary>
    public IAsyncObservable<AvailabilityReportedEventArgs> AvailabilityReported
        => _observable.OfType<EventArgs, AvailabilityReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a battery alert is reported.
    /// </summary>
    public IAsyncObservable<BatteryAlertReportedEventArgs> BatteryAlertReported
        => _observable.OfType<EventArgs, BatteryAlertReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a battery level is reported.
    /// </summary>
    public IAsyncObservable<BatteryLevelReportedEventArgs> BatteryLevelReported
        => _observable.OfType<EventArgs, BatteryLevelReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a brightness level is reported.
    /// </summary>
    public IAsyncObservable<BrightnessReportedEventArgs> BrightnessReported
        => _observable.OfType<EventArgs, BrightnessReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a device communication is reported.
    /// </summary>
    public IAsyncObservable<DeviceCommunicationReportedEventArgs> DeviceCommunicationReported
        => _observable.OfType<EventArgs, DeviceCommunicationReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a dimming scenario is reported.
    /// </summary>
    public IAsyncObservable<DimmingScenarioReportedEventArgs> DimmingScenarioReported
        => _observable.OfType<EventArgs, DimmingScenarioReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a dry contact scenario is reported.
    /// </summary>
    public IAsyncObservable<DryContactScenarioReportedEventArgs> DryContactScenarioReported
        => _observable.OfType<EventArgs, DryContactScenarioReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a dry contact state is reported.
    /// </summary>
    public IAsyncObservable<DryContactStateReportedEventArgs> DryContactStateReported
        => _observable.OfType<EventArgs, DryContactStateReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when an incoming message is reported.
    /// </summary>
    public IAsyncObservable<IncomingMessageReportedEventArgs> IncomingMessageReported
        => _observable.OfType<EventArgs, IncomingMessageReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when an ON/OFF scenario is reported.
    /// </summary>
    public IAsyncObservable<OnOffScenarioReportedEventArgs> OnOffScenarioReported
        => _observable.OfType<EventArgs, OnOffScenarioReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when an outgoing message is reported.
    /// </summary>
    public IAsyncObservable<OutgoingMessageReportedEventArgs> OutgoingMessageReported
        => _observable.OfType<EventArgs, OutgoingMessageReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a pilot wire derogation mode is reported.
    /// </summary>
    public IAsyncObservable<PilotWireDerogationModeReportedEventArgs> PilotWireDerogationModeReported
        => _observable.OfType<EventArgs, PilotWireDerogationModeReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a pilot wire setpoint mode is reported.
    /// </summary>
    public IAsyncObservable<PilotWireSetpointModeReportedEventArgs> PilotWireSetpointModeReported
        => _observable.OfType<EventArgs, PilotWireSetpointModeReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a pilot wire shutdown mode is reported.
    /// </summary>
    public IAsyncObservable<PilotWireShutdownModeReportedEventArgs> PilotWireShutdownModeReported
        => _observable.OfType<EventArgs, PilotWireShutdownModeReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a pressure scenario is reported.
    /// </summary>
    public IAsyncObservable<PressureScenarioReportedEventArgs> PressureScenarioReported
        => _observable.OfType<EventArgs, PressureScenarioReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a pressure scenario plus is reported.
    /// </summary>
    public IAsyncObservable<PressureScenarioPlusReportedEventArgs> PressureScenarioPlusReported
        => _observable.OfType<EventArgs, PressureScenarioPlusReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a progressive scenario is reported.
    /// </summary>
    public IAsyncObservable<ProgressiveScenarioReportedEventArgs> ProgressiveScenarioReported
        => _observable.OfType<EventArgs, ProgressiveScenarioReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a shutter position is reported.
    /// </summary>
    public IAsyncObservable<ShutterPositionReportedEventArgs> ShutterPositionReported
        => _observable.OfType<EventArgs, ShutterPositionReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a shutter state is reported.
    /// </summary>
    public IAsyncObservable<ShutterStateReportedEventArgs> ShutterStateReported
        => _observable.OfType<EventArgs, ShutterStateReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a smart meter power cut mode is reported.
    /// </summary>
    public IAsyncObservable<SmartMeterPowerCutModeReportedEventArgs> SmartMeterPowerCutModeReported
        => _observable.OfType<EventArgs, SmartMeterPowerCutModeReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a smart meter rate type is reported.
    /// </summary>
    public IAsyncObservable<SmartMeterRateTypeReportedEventArgs> SmartMeterRateTypeReported
        => _observable.OfType<EventArgs, SmartMeterRateTypeReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a STOP/UP/DOWN scenario is reported.
    /// </summary>
    public IAsyncObservable<StopUpDownScenarioReportedEventArgs> StopUpDownScenarioReported
        => _observable.OfType<EventArgs, StopUpDownScenarioReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a switch state is reported.
    /// </summary>
    public IAsyncObservable<SwitchStateReportedEventArgs> SwitchStateReported
        => _observable.OfType<EventArgs, SwitchStateReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a timed scenario is reported.
    /// </summary>
    public IAsyncObservable<TimedScenarioReportedEventArgs> TimedScenarioReported
        => _observable.OfType<EventArgs, TimedScenarioReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a toggle scenario is reported.
    /// </summary>
    public IAsyncObservable<ToggleScenarioReportedEventArgs> ToggleScenarioReported
        => _observable.OfType<EventArgs, ToggleScenarioReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a water heater setpoint mode is reported.
    /// </summary>
    public IAsyncObservable<WaterHeaterSetpointModeReportedEventArgs> WaterHeaterSetpointModeReported
        => _observable.OfType<EventArgs, WaterHeaterSetpointModeReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a water heater state is reported.
    /// </summary>
    public IAsyncObservable<WaterHeaterStateReportedEventArgs> WaterHeaterStateReported
        => _observable.OfType<EventArgs, WaterHeaterStateReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a wireless burglar alarm state is reported.
    /// </summary>
    public IAsyncObservable<WirelessBurglarAlarmStateReportedEventArgs> WirelessBurglarAlarmStateReported
        => _observable.OfType<EventArgs, WirelessBurglarAlarmStateReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a Zigbee binding event is reported.
    /// </summary>
    public IAsyncObservable<ZigbeeBindingEventReportedEventArgs> ZigbeeBindingEventReported
        => _observable.OfType<EventArgs, ZigbeeBindingEventReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a Zigbee network event is reported.
    /// </summary>
    public IAsyncObservable<ZigbeeNetworkEventReportedEventArgs> ZigbeeNetworkEventReported
        => _observable.OfType<EventArgs, ZigbeeNetworkEventReportedEventArgs>();

    /// <summary>
    /// Connects the <see cref="IAsyncObservable{T}"/> so that events can start being processed.
    /// </summary>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous
    /// operation and whose result is used as a signal by the OpenNetty hosted service
    /// to inform the pipeline that no additional event will be processed.
    /// </returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTask<IAsyncDisposable> ConnectAsync() => _observable.ConnectAsync();

    /// <inheritdoc/>
    public void Dispose() => _registration.Dispose();

    /// <summary>
    /// Publishes a new event.
    /// </summary>
    /// <param name="arguments">The arguments associated with the event.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTask PublishAsync<TEventArgs>(TEventArgs arguments, CancellationToken cancellationToken = default)
        where TEventArgs : notnull, EventArgs
        => _channel.Writer.WriteAsync(arguments, cancellationToken);

    /// <summary>
    /// Represents abstract event arguments used by OpenNetty.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    public abstract record class EventArgs(OpenNettyEndpoint Endpoint);

    /// <summary>
    /// Represents event arguments used when an action scenario is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Type">The action scenario type.</param>
    public sealed record class ActionScenarioReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.ScenariosPlus.ActionScenarioType Type) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when the availability of an endpoint is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Availability">The gateway availability.</param>
    public sealed record class AvailabilityReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.Diagnostics.Availability Availability) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a battery alert is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    public sealed record class BatteryAlertReportedEventArgs(OpenNettyEndpoint Endpoint) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a battery level is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Level">The battery level.</param>
    public sealed record class BatteryLevelReportedEventArgs(OpenNettyEndpoint Endpoint, byte Level) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a brightness level is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Level">The brightness level, from 0 to 100.</param>
    public sealed record class BrightnessReportedEventArgs(OpenNettyEndpoint Endpoint, byte Level) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a device communication is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Message">The message received from the remote device.</param>
    public sealed record class DeviceCommunicationReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyMessage Message) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a dimming scenario is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Step">The dimming step (positive or negative).</param>
    public sealed record class DimmingScenarioReportedEventArgs(OpenNettyEndpoint Endpoint, short Step) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a dry contact scenario is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Type">The dry contact scenario type.</param>
    public sealed record class DryContactScenarioReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.ScenariosPlus.DryContactScenarioType Type) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a dry contact state is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="State">The state of the dry contact.</param>
    /// <param name="Origin">The origin of the dry contact state report.</param>
    public sealed record class DryContactStateReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.ScenariosPlus.DryContactState State,
        OpenNettyModels.ScenariosPlus.DryContactStateOrigin Origin) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when an incoming message is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Message">The message.</param>
    /// <param name="Session">The session that was used to receive the message.</param>
    public sealed record class IncomingMessageReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyMessage Message, OpenNettySession Session) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when an ON/OFF scenario is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Type">The switch scenario type.</param>
    public sealed record class OnOffScenarioReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.Lighting.OnOffScenarioType Type) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when an outgoing message is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Message">The message.</param>
    /// <param name="Session">The session that was used to send the message.</param>
    public sealed record class OutgoingMessageReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyMessage Message, OpenNettySession Session) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a pilot wire derogation mode is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Mode">The derogation mode.</param>
    /// <param name="Duration">The derogation duration.</param>
    public sealed record class PilotWireDerogationModeReportedEventArgs(
        OpenNettyEndpoint Endpoint,
        OpenNettyModels.TemperatureControl.PilotWireMode? Mode,
        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration? Duration) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a pilot wire setpoint mode is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Mode">The setpoint mode.</param>
    public sealed record class PilotWireSetpointModeReportedEventArgs(
        OpenNettyEndpoint Endpoint, OpenNettyModels.TemperatureControl.PilotWireMode Mode) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a pilot wire shutdown mode is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Active">A boolean indicating whether the shutdown mode is active or not.</param>
    public sealed record class PilotWireShutdownModeReportedEventArgs(
        OpenNettyEndpoint Endpoint, bool Active) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a pressure scenario is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Type">The pressure scenario type.</param>
    /// <param name="Button">The button identifier.</param>
    public sealed record class PressureScenarioReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.Scenarios.PressureScenarioType Type, byte Button) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a pressure scenario plus is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Type">The pressure scenario type.</param>
    /// <param name="Button">The button identifier, if applicable.</param>
    public sealed record class PressureScenarioPlusReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.ScenariosPlus.PressureScenarioType Type, byte? Button) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a progressive scenario is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Duration">The scenario duration.</param>
    public sealed record class ProgressiveScenarioReportedEventArgs(OpenNettyEndpoint Endpoint, TimeSpan Duration) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a shutter position is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Position">The shutter position, from 0 to 100.</param>
    public sealed record class ShutterPositionReportedEventArgs(OpenNettyEndpoint Endpoint, byte Position) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a shutter state is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="State">The shutter state.</param>
    public sealed record class ShutterStateReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.Automation.ShutterState State) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a smart meter power cut mode is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Active">A boolean indicating whether the power cut mode is active or not.</param>
    public sealed record class SmartMeterPowerCutModeReportedEventArgs(OpenNettyEndpoint Endpoint, bool Active) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a smart meter rate type is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Type">The rate type.</param>
    public sealed record class SmartMeterRateTypeReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.TemperatureControl.SmartMeterRateType Type) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a STOP/UP/DOWN scenario is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Type">The shutter scenario type.</param>
    public sealed record class StopUpDownScenarioReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.Automation.StopUpDownScenarioType Type) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a switch state is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="State">The switch state.</param>
    public sealed record class SwitchStateReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.Lighting.SwitchState State) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a timed scenario is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Duration">The duration after which associated devices change their state.</param>
    public sealed record class TimedScenarioReportedEventArgs(OpenNettyEndpoint Endpoint, TimeSpan Duration) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a toggle scenario is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    public sealed record class ToggleScenarioReportedEventArgs(OpenNettyEndpoint Endpoint) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a water heater setpoint mode is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Mode">The setpoint mode.</param>
    public sealed record class WaterHeaterSetpointModeReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.TemperatureControl.WaterHeaterMode Mode) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a water heater state is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="State">The state.</param>
    public sealed record class WaterHeaterStateReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.TemperatureControl.WaterHeaterState State) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a wireless burglar alarm state is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="State">The state.</param>
    public sealed record class WirelessBurglarAlarmStateReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.Alarm.WirelessBurglarAlarmState State) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a Zigbee binding event is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Type">The event type.</param>
    public sealed record class ZigbeeBindingEventReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.ScenariosPlus.ZigbeeBindingEventType Type) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a Zigbee network event is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Type">The event type.</param>
    public sealed record class ZigbeeNetworkEventReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.Management.ZigbeeNetworkEventType Type) : EventArgs(Endpoint);
}
