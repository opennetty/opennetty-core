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
    /// Gets an event triggered when a basic scenario is reported.
    /// </summary>
    public IAsyncObservable<BasicScenarioReportedEventArgs> BasicScenarioReported
        => _observable.OfType<EventArgs, BasicScenarioReportedEventArgs>();

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
    /// Gets an event triggered when a binding is canceled.
    /// </summary>
    public IAsyncObservable<BindingCanceledEventArgs> BindingCanceled
        => _observable.OfType<EventArgs, BindingCanceledEventArgs>();

    /// <summary>
    /// Gets an event triggered when a binding is closed.
    /// </summary>
    public IAsyncObservable<BindingClosedEventArgs> BindingClosed
        => _observable.OfType<EventArgs, BindingClosedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a basic scenario is open.
    /// </summary>
    public IAsyncObservable<BindingOpenEventArgs> BindingOpen
        => _observable.OfType<EventArgs, BindingOpenEventArgs>();

    /// <summary>
    /// Gets an event triggered when a brightness level is reported.
    /// </summary>
    public IAsyncObservable<BrightnessReportedEventArgs> BrightnessReported
        => _observable.OfType<EventArgs, BrightnessReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a device description is reported.
    /// </summary>
    public IAsyncObservable<DeviceDescriptionReportedEventArgs> DeviceDescriptionReported
        => _observable.OfType<EventArgs, DeviceDescriptionReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a dimming step is reported.
    /// </summary>
    public IAsyncObservable<DimmingStepReportedEventArgs> DimmingStepReported
        => _observable.OfType<EventArgs, DimmingStepReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a firmware version is reported.
    /// </summary>
    public IAsyncObservable<FirmwareVersionReportedEventArgs> FirmwareVersionReported
        => _observable.OfType<EventArgs, FirmwareVersionReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a hardware version is reported.
    /// </summary>
    public IAsyncObservable<HardwareVersionReportedEventArgs> HardwareVersionReported
        => _observable.OfType<EventArgs, HardwareVersionReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a MAC address is reported.
    /// </summary>
    public IAsyncObservable<MacAddressReportedEventArgs> MacAddressReported
        => _observable.OfType<EventArgs, MacAddressReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when an OFF scenario is reported.
    /// </summary>
    public IAsyncObservable<OffScenarioReportedEventArgs> OffScenarioReported
        => _observable.OfType<EventArgs, OffScenarioReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when an ON scenario is reported.
    /// </summary>
    public IAsyncObservable<OnScenarioReportedEventArgs> OnScenarioReported
        => _observable.OfType<EventArgs, OnScenarioReportedEventArgs>();

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
    /// Gets an event triggered when a shutter DOWN scenario is reported.
    /// </summary>
    public IAsyncObservable<ShutterDownScenarioReportedEventArgs> ShutterDownScenarioReported
        => _observable.OfType<EventArgs, ShutterDownScenarioReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a shutter STOP scenario is reported.
    /// </summary>
    public IAsyncObservable<ShutterStopScenarioReportedEventArgs> ShutterStopScenarioReported
        => _observable.OfType<EventArgs, ShutterStopScenarioReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when a shutter UP scenario is reported.
    /// </summary>
    public IAsyncObservable<ShutterUpScenarioReportedEventArgs> ShutterUpScenarioReported
        => _observable.OfType<EventArgs, ShutterUpScenarioReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when smart meter indexes are reported.
    /// </summary>
    public IAsyncObservable<SmartMeterIndexesReportedEventArgs> SmartMeterIndexesReported
        => _observable.OfType<EventArgs, SmartMeterIndexesReportedEventArgs>();

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
    /// Gets an event triggered when an uptime duration is reported.
    /// </summary>
    public IAsyncObservable<UptimeReportedEventArgs> UptimeReported
        => _observable.OfType<EventArgs, UptimeReportedEventArgs>();

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
    /// Gets an event triggered when a Zigbee channel is reported.
    /// </summary>
    public IAsyncObservable<ZigbeeChannelReportedEventArgs> ZigbeeChannelReported
        => _observable.OfType<EventArgs, ZigbeeChannelReportedEventArgs>();

    /// <summary>
    /// Gets an event triggered when the number of Zigbee devices in the database is reported.
    /// </summary>
    public IAsyncObservable<ZigbeeDevicesCountReportedEventArgs> ZigbeeDevicesCountReported
        => _observable.OfType<EventArgs, ZigbeeDevicesCountReportedEventArgs>();

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
    /// Represents event arguments used when a basic scenario is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    public sealed record class BasicScenarioReportedEventArgs(OpenNettyEndpoint Endpoint) : EventArgs(Endpoint);

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
    /// Represents event arguments used when a binding is canceled.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    public sealed record class BindingCanceledEventArgs(OpenNettyEndpoint Endpoint) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a binding is closed.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    public sealed record class BindingClosedEventArgs(OpenNettyEndpoint Endpoint) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a binding is open.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    public sealed record class BindingOpenEventArgs(OpenNettyEndpoint Endpoint) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a brightness level is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Level">The brightness level, from 0 to 100.</param>
    public sealed record class BrightnessReportedEventArgs(OpenNettyEndpoint Endpoint, byte Level) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a device description is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Description">The device description.</param>
    public sealed record class DeviceDescriptionReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.Diagnostics.DeviceDescription Description) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a dimming step is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Delta">The delta (positive or negative).</param>
    public sealed record class DimmingStepReportedEventArgs(OpenNettyEndpoint Endpoint, int Delta) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a firmware version is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Version">The firmware version.</param>
    public sealed record class FirmwareVersionReportedEventArgs(OpenNettyEndpoint Endpoint, Version Version) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a hardware version is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Version">The hardware version.</param>
    public sealed record class HardwareVersionReportedEventArgs(OpenNettyEndpoint Endpoint, Version Version) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a MAC address is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Address">The MAC address.</param>
    public sealed record class MacAddressReportedEventArgs(OpenNettyEndpoint Endpoint, string Address) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when an OFF scenario is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    public sealed record class OffScenarioReportedEventArgs(OpenNettyEndpoint Endpoint) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when an ON scenario is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    public sealed record class OnScenarioReportedEventArgs(OpenNettyEndpoint Endpoint) : EventArgs(Endpoint);

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
    /// Represents event arguments used when a shutter DOWN scenario is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    public sealed record class ShutterDownScenarioReportedEventArgs(OpenNettyEndpoint Endpoint) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a shutter STOP scenario is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    public sealed record class ShutterStopScenarioReportedEventArgs(OpenNettyEndpoint Endpoint) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when a shutter UP scenario is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    public sealed record class ShutterUpScenarioReportedEventArgs(OpenNettyEndpoint Endpoint) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when smart meter indexes are reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Indexes">The indexes.</param>
    public sealed record class SmartMeterIndexesReportedEventArgs(OpenNettyEndpoint Endpoint,
        OpenNettyModels.TemperatureControl.SmartMeterIndexes Indexes) : EventArgs(Endpoint);

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
    /// Represents event arguments used when an uptime duration is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Duration">The uptime duration.</param>
    public sealed record class UptimeReportedEventArgs(OpenNettyEndpoint Endpoint, TimeSpan Duration) : EventArgs(Endpoint);

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
    /// Represents event arguments used when a Zigbee channel is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Channel">The channel.</param>
    public sealed record class ZigbeeChannelReportedEventArgs(OpenNettyEndpoint Endpoint, byte Channel) : EventArgs(Endpoint);

    /// <summary>
    /// Represents event arguments used when the number of Zigbee devices in the database is reported.
    /// </summary>
    /// <param name="Endpoint">The endpoint.</param>
    /// <param name="Count">The number of Zigbee devices in the database.</param>
    public sealed record class ZigbeeDevicesCountReportedEventArgs(OpenNettyEndpoint Endpoint, byte Count) : EventArgs(Endpoint);
}
