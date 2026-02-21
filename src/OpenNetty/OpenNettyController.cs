/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Globalization;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;

namespace OpenNetty;

/// <summary>
/// Represents a high-level service that can be used to execute common OpenWebNet operations.
/// </summary>
public class OpenNettyController
{
    private readonly OpenNettyManager _manager;
    private readonly IOpenNettyService _service;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyController"/> class.
    /// </summary>
    /// <param name="manager">The OpenNetty manager.</param>
    /// <param name="service">The OpenNetty service.</param>
    public OpenNettyController(
        OpenNettyManager manager,
        IOpenNettyService service)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    /// <summary>
    /// Activates the pilot wire shutdown mode for the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask ActivatePilotWireShutdownModeAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.PilotWireShutdown))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }
        
        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.TemperatureControl.WirePilotShutdownMode,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Multicast,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Adds a new entry to the memory of the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="data">The data to add.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask AddMemoryDataAsync(
        OpenNettyEndpoint endpoint,
        OpenNettyModels.Diagnostics.MemoryData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(data);

        if (!endpoint.HasCapability(OpenNettyCapabilities.MemoryWriting))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.SetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Diagnostics.MemoryWrite,
            values           :
            [
                data.Medium switch
                {
                    OpenNettyMedium.Radio     => "64",
                    OpenNettyMedium.Powerline => "96",
                    OpenNettyMedium.Infrared  => "128",

                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                },
                data.Address.ToString(),
                data.FunctionCode.ToString(CultureInfo.InvariantCulture)
            ],
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Adds the specified endpoint to the list of devices associated with the Zigbee scenario that is currently open.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask BindAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.ZigbeeBinding))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.ScenariosPlus.BindingRequest,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Disables the supervisor mode for the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask DisableSupervisionAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Management.SupervisorRemove,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enables the supervisor mode for the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask EnableSupervisionAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Management.Supervisor,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Cancels the pilot wire derogation mode currently enforced by the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask CancelPilotWireDerogationModeAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.PilotWireDerogation))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }
        
        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.TemperatureControl.CancelWirePilotDerogationMode,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Multicast,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Cancels the pilot wire shutdown mode currently enforced by the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask CancelPilotWireShutdownModeAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.PilotWireShutdown))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }
        
        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.TemperatureControl.CancelWirePilotShutdownMode,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Multicast,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Asks the specified endpoint to close a Zigbee network.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask CloseZigbeeNetworkAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Management.CloseZigbeeNetwork,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Asks the specified endpoint how many Zigbee devices are registered in its database.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous
    /// operation and whose result returns the number of Zigbee devices registered the database.
    /// </returns>
    public virtual async ValueTask<byte> CountZigbeeDevicesAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        var values = await _service.GetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Management.NumberOfProducts,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);

        return byte.Parse(values[0], CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Asks the specified endpoint to create a Zigbee network.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask CreateZigbeeNetworkAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // Note: creating a Zigbee network can take a while and the gateway only returns an
        // acknowledgment frame after the operation is complete. To ensure the gateway is
        // given enough time to create the network, the timeouts are increased if necessary.
        var options = GetTransmissionOptions(endpoint);
        if (options.FrameAcknowledgementTimeout < TimeSpan.FromSeconds(20))
        {
            options = options with { FrameAcknowledgementTimeout = TimeSpan.FromSeconds(20) };
        }

        if (options.OutgoingMessageProcessingTimeout < TimeSpan.FromSeconds(30))
        {
            options = options with { OutgoingMessageProcessingTimeout = TimeSpan.FromSeconds(30) };
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Management.CreateZigbeeNetwork,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : options,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Dispatches a virtual action scenario for the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="type">The type of scenario to dispatch.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask DispatchActionScenarioAsync(
        OpenNettyEndpoint endpoint,
        OpenNettyModels.ScenariosPlus.ActionScenarioType type,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.ActionScenarioActivation))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : type switch
            {
                OpenNettyModels.ScenariosPlus.ActionScenarioType.Action     => OpenNettyCommands.ScenariosPlus.Action,
                OpenNettyModels.ScenariosPlus.ActionScenarioType.StopAction => OpenNettyCommands.ScenariosPlus.StopAction,

                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
            },
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Dispatches a virtual dimming scenario for the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="step">The dimming step (positive or negative).</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask DispatchDimmingScenarioAsync(
        OpenNettyEndpoint endpoint,
        short step,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (step is not (>= -100 and <= 100))
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }

        if (!endpoint.HasCapability(OpenNettyCapabilities.DimmingScenarioActivation))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.SetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Lighting.DimmerStep,
            values           : [(step is < 0 ? step + 256 : step).ToString(CultureInfo.InvariantCulture)],
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Broadcast,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Dispatches a virtual ON/OFF scenario for the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="type">The type of scenario to dispatch.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask DispatchOnOffScenarioAsync(
        OpenNettyEndpoint endpoint,
        OpenNettyModels.Lighting.OnOffScenarioType type,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.OnOffScenarioActivation))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : type switch
            {
                OpenNettyModels.Lighting.OnOffScenarioType.Off => OpenNettyCommands.Lighting.Off,
                OpenNettyModels.Lighting.OnOffScenarioType.On  => OpenNettyCommands.Lighting.On,

                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
            },
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Broadcast,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Dispatches a virtual pressure scenario for the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="type">The type of scenario to dispatch.</param>
    /// <param name="button">The button number.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask DispatchPressureScenarioAsync(
        OpenNettyEndpoint endpoint,
        OpenNettyModels.Scenarios.PressureScenarioType type,
        byte button,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioActivation))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        var command = type switch
        {
            OpenNettyModels.Scenarios.PressureScenarioType.Pressure
                => new OpenNettyCommand(OpenNettyCategories.Scenarios, button.ToString(CultureInfo.InvariantCulture)),

            OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterShortPressure
                => new OpenNettyCommand(OpenNettyCategories.Scenarios, button.ToString(CultureInfo.InvariantCulture), ["1"]),

            OpenNettyModels.Scenarios.PressureScenarioType.ReleaseAfterExtendedPressure
                => new OpenNettyCommand(OpenNettyCategories.Scenarios, button.ToString(CultureInfo.InvariantCulture), ["2"]),

            OpenNettyModels.Scenarios.PressureScenarioType.ExtendedPressure
                => new OpenNettyCommand(OpenNettyCategories.Scenarios, button.ToString(CultureInfo.InvariantCulture), ["3"]),

            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
        };

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : command,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Dispatches a virtual short pressure scenario plus for the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="type">The type of scenario to dispatch.</param>
    /// <param name="button">The button number, if applicable.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask DispatchPressureScenarioPlusAsync(
        OpenNettyEndpoint endpoint,
        OpenNettyModels.ScenariosPlus.PressureScenarioType type,
        byte? button = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (button is null && endpoint.HasCapability(OpenNettyCapabilities.ConfigurablePushButtonNumbers))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0117));
        }

        if (button is not null && !endpoint.HasCapability(OpenNettyCapabilities.ConfigurablePushButtonNumbers))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0118));
        }

        if (!endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioPlusActivation))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        var command = type switch
        {
            OpenNettyModels.ScenariosPlus.PressureScenarioType.ShortPressure           => OpenNettyCommands.ScenariosPlus.ShortPressure,
            OpenNettyModels.ScenariosPlus.PressureScenarioType.StartOfExtendedPressure => OpenNettyCommands.ScenariosPlus.StartOfExtendedPressure,
            OpenNettyModels.ScenariosPlus.PressureScenarioType.ExtendedPressure        => OpenNettyCommands.ScenariosPlus.ExtendedPressure,
            OpenNettyModels.ScenariosPlus.PressureScenarioType.EndOfExtendedPressure   => OpenNettyCommands.ScenariosPlus.EndOfExtendedPressure,

            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
        };

        if (button is not null)
        {
            command = command.WithParameters(button.Value.ToString(CultureInfo.InvariantCulture));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : command,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Dispatches a virtual progressive scenario for the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="duration">The scenario duration.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask DispatchProgressiveScenarioAsync(
        OpenNettyEndpoint endpoint,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.ProgressiveScenarioActivation))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.ScenariosPlus.ActionInTime.WithParameters(
                /* TIME: */ ((long) (duration.TotalSeconds * 5 + .5)).ToString(CultureInfo.InvariantCulture)),
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Dispatches a virtual STOP/UP/DOWN scenario for the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="type">The type of scenario to dispatch.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask DispatchStopUpDownScenarioAsync(
        OpenNettyEndpoint endpoint,
        OpenNettyModels.Automation.StopUpDownScenarioType type,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.StopUpDownScenarioActivation))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : type switch
            {
                OpenNettyModels.Automation.StopUpDownScenarioType.Stop => OpenNettyCommands.Automation.Stop,
                OpenNettyModels.Automation.StopUpDownScenarioType.Up   => OpenNettyCommands.Automation.Up,
                OpenNettyModels.Automation.StopUpDownScenarioType.Down => OpenNettyCommands.Automation.Down,

                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
            },
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Broadcast,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Dispatches a virtual timed scenario for the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="duration">The duration after which associated devices will change their state.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask DispatchTimedScenarioAsync(
        OpenNettyEndpoint endpoint,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.TimedScenarioActivation))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.ScenariosPlus.ActionForTime.WithParameters(
                /* TIME: */ ((long) (duration.TotalSeconds * 5 + .5)).ToString(CultureInfo.InvariantCulture)),
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enumerates the current switch state of all the endpoints matching the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="IAsyncEnumerable{T}"/> that can be used to iterate the switch
    /// states returned by all the endpoints matching the specified endpoint.
    /// </returns>
    public virtual IAsyncEnumerable<(OpenNettyEndpoint Endpoint, byte Level)> EnumerateBrightnessAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) &&
            !endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // If the endpoint has a function type attached, ensure it is suitable for the requested operation.
        if (endpoint.GetStringSetting(OpenNettySettings.FunctionType) is not (null or OpenNettySettings.FunctionTypes.LightActuator))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
        }

        switch (endpoint.Protocol)
        {
            case OpenNettyProtocol.Nitoo:
            case OpenNettyProtocol.Scs when endpoint.Address is { Type: OpenNettyAddressType.ScsLightPoint } &&
                OpenNettyAddress.IsScsLightPointPointToPointAddress(endpoint.Address.Value):
            case OpenNettyProtocol.Zigbee when endpoint.Address is { Type: OpenNettyAddressType.Zigbee } address &&
                OpenNettyAddress.ToZigbeeAddress(address) is { Identifier: not 0, Unit: not 0 }:
                return GetBrightnessAsync(endpoint, cancellationToken)
                    .AsTask()
                    .ToAsyncEnumerable()
                    .Select(brightness => (endpoint, brightness));

            default:
                return ExecuteAsync(cancellationToken);
        }

        async IAsyncEnumerable<(OpenNettyEndpoint Endpoint, byte Level)> ExecuteAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Note: this method supports resolving the brightless level of endpoints that support advanced
            // or basic dimming (depending on whether the virtual endpoint has the associated capabilities).
            //
            // For that, a first pass is made to collect the state of endpoints supporting advanced dimming and a second
            // pass is used to collect the state of the endpoints for which no state was extracted during the first pass.

            HashSet<OpenNettyEndpoint> set = [];

            if (endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
            {
                // Note: while the brightness level is requested using the "DIMMER SPEED/LEVEL" DIMENSION, the result
                // might be returned using a different DIMENSION, "DIMMER STATUS". To ensure the brightness is
                // correctly resolved, both dimensions are observed before sending the DIMENSION REQUEST.
                var messages = _service.ObserveMessagesAsync(
                    message          : OpenNettyMessage.CreateDimensionRequest(
                        protocol : endpoint.Protocol,
                        dimension: OpenNettyDimensions.Lighting.DimmerLevelSpeed,
                        address  : endpoint.Address,
                        medium   : endpoint.Medium,
                        mode     : null),
                    gateway          : endpoint.Gateway,
                    options          : GetTransmissionOptions(endpoint),
                    cancellationToken: cancellationToken);

                await foreach (var result in messages
                    .TakeWhile(static message => message.Type is not (OpenNettyMessageType.Acknowledgement             or
                                                                      OpenNettyMessageType.BusyNegativeAcknowledgement or
                                                                      OpenNettyMessageType.NegativeAcknowledgement))
                    .Where(static message => message.Dimension == OpenNettyDimensions.Lighting.DimmerLevelSpeed ||
                                             message.Dimension == OpenNettyDimensions.Lighting.DimmerStatus)
                    .Timeout(TimeSpan.FromSeconds(10))
                    .ToAsyncEnumerable()
                    .Select(async (message, cancellationToken) => (
                        Values  : message.Values,
                        Endpoint: await _manager.FindEndpointByAddressAsync(endpoint.Gateway, message.Address!.Value, cancellationToken)))
                    .Where(static arguments => arguments.Endpoint is not null)
                    .Where(static arguments => arguments.Endpoint!.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
                    .Where(arguments => set.Add(arguments.Endpoint!))
                    .Select(static arguments => (arguments.Endpoint!,
                        (byte) (byte.Parse(arguments.Values[0], CultureInfo.InvariantCulture) - 100))))
                {
                    yield return result;
                }
            }

            if (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState))
            {
                var results = _service.EnumerateStatusesAsync(
                    protocol         : endpoint.Protocol,
                    category         : OpenNettyCategories.Lighting,
                    address          : endpoint.Address,
                    medium           : endpoint.Medium,
                    mode             : null,
                    filter           : static command => ValueTask.FromResult(
                        command == OpenNettyCommands.Lighting.Off  ||
                        command == OpenNettyCommands.Lighting.On   ||
                        command == OpenNettyCommands.Lighting.On20 ||
                        command == OpenNettyCommands.Lighting.On30 ||
                        command == OpenNettyCommands.Lighting.On40 ||
                        command == OpenNettyCommands.Lighting.On50 ||
                        command == OpenNettyCommands.Lighting.On60 ||
                        command == OpenNettyCommands.Lighting.On70 ||
                        command == OpenNettyCommands.Lighting.On80 ||
                        command == OpenNettyCommands.Lighting.On90 ||
                        command == OpenNettyCommands.Lighting.On100),
                    gateway          : endpoint.Gateway,
                    options          : GetTransmissionOptions(endpoint),
                    cancellationToken: cancellationToken);

                await foreach (var result in results
                    .Select(async (arguments, cancellationToken) => (
                        Command : arguments.Command,
                        Endpoint: await _manager.FindEndpointByAddressAsync(endpoint.Gateway, arguments.Address, cancellationToken)))
                    .Where(static arguments => arguments.Endpoint is not null)
                    .Where(static arguments => arguments.Endpoint!.HasCapability(OpenNettyCapabilities.BasicDimmingState))
                    .Where(arguments => set.Add(arguments.Endpoint!))
                    .Select(static arguments => (arguments.Endpoint!,
                        arguments.Command == OpenNettyCommands.Lighting.Off   ? (byte) 0   :
                        arguments.Command == OpenNettyCommands.Lighting.On    ? (byte) 100 :
                        arguments.Command == OpenNettyCommands.Lighting.On20  ? (byte) 20  :
                        arguments.Command == OpenNettyCommands.Lighting.On30  ? (byte) 30  :
                        arguments.Command == OpenNettyCommands.Lighting.On40  ? (byte) 40  :
                        arguments.Command == OpenNettyCommands.Lighting.On50  ? (byte) 50  :
                        arguments.Command == OpenNettyCommands.Lighting.On60  ? (byte) 60  :
                        arguments.Command == OpenNettyCommands.Lighting.On70  ? (byte) 70  :
                        arguments.Command == OpenNettyCommands.Lighting.On80  ? (byte) 80  :
                        arguments.Command == OpenNettyCommands.Lighting.On90  ? (byte) 90  : (byte) 100)))
                {
                    yield return result;
                }
            }
        }
    }

    /// <summary>
    /// Enumerates the current shutter positions of all the endpoints matching the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="IAsyncEnumerable{T}"/> that can be used to iterate the shutter
    /// positions returned by all the endpoints matching the specified endpoint.
    /// </returns>
    public virtual IAsyncEnumerable<(OpenNettyEndpoint Endpoint, byte? Position)> EnumerateShutterPositionsAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // If the endpoint has a function type attached, ensure it is suitable for the requested operation.
        if (endpoint.GetStringSetting(OpenNettySettings.FunctionType) is not (null or OpenNettySettings.FunctionTypes.AutomationActuator))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
        }

        switch (endpoint.Protocol)
        {
            case OpenNettyProtocol.Nitoo:
            case OpenNettyProtocol.Scs when endpoint.Address is { Type: OpenNettyAddressType.ScsLightPoint } &&
                OpenNettyAddress.IsScsLightPointPointToPointAddress(endpoint.Address.Value):
            case OpenNettyProtocol.Zigbee when endpoint.Address is { Type: OpenNettyAddressType.Zigbee } &&
                OpenNettyAddress.ToZigbeeAddress(endpoint.Address.Value) is { Identifier: not 0, Unit: not 0 }:
                return GetShutterPositionAsync(endpoint, cancellationToken)
                    .AsTask()
                    .ToAsyncEnumerable()
                    .Select(position => (endpoint, position));

            default:
                var dimensions = _service.EnumerateDimensionsAsync(
                    protocol         : endpoint.Protocol,
                    dimension        : OpenNettyDimensions.Automation.ShutterStatus,
                    gateway          : endpoint.Gateway,
                    options          : GetTransmissionOptions(endpoint),
                    cancellationToken: cancellationToken);

                return dimensions
                    .Select(async (message, cancellationToken) => (
                        Values  : message.Values,
                        Endpoint: await _manager.FindEndpointByAddressAsync(endpoint.Gateway, message.Address, cancellationToken)))
                    .Where(static arguments => arguments.Endpoint is not null)
                    .Where(static arguments => arguments.Endpoint!.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
                    .Select(static arguments => (arguments.Endpoint!, byte.Parse(arguments.Values[1], CultureInfo.InvariantCulture) switch
                    {
                              0       => (byte?) 0,
                             100      => (byte?) 100,
                             255      => null,
                        byte position => position
                    }));
        }
    }

    /// <summary>
    /// Enumerates the current shutter state of all the endpoints matching the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="IAsyncEnumerable{T}"/> that can be used to iterate the shutter
    /// state returned by all the endpoints matching the specified endpoint.
    /// </returns>
    public virtual IAsyncEnumerable<(OpenNettyEndpoint Endpoint, OpenNettyModels.Automation.ShutterState State)> EnumerateShutterStatesAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.BasicShutterState) &&
            !endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // If the endpoint has a function type attached, ensure it is suitable for the requested operation.
        if (endpoint.GetStringSetting(OpenNettySettings.FunctionType) is not (null or OpenNettySettings.FunctionTypes.AutomationActuator))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
        }

        switch (endpoint.Protocol)
        {
            case OpenNettyProtocol.Nitoo:
            case OpenNettyProtocol.Scs when endpoint.Address is { Type: OpenNettyAddressType.ScsLightPoint } &&
                OpenNettyAddress.IsScsLightPointPointToPointAddress(endpoint.Address.Value):
            case OpenNettyProtocol.Zigbee when endpoint.Address is { Type: OpenNettyAddressType.Zigbee } &&
                OpenNettyAddress.ToZigbeeAddress(endpoint.Address.Value) is { Identifier: not 0, Unit: not 0 }:
                return GetShutterStateAsync(endpoint, cancellationToken)
                    .AsTask()
                    .ToAsyncEnumerable()
                    .Select(state => (endpoint, state));

            default:
                return ExecuteAsync(cancellationToken);
        }

        async IAsyncEnumerable<(OpenNettyEndpoint Endpoint, OpenNettyModels.Automation.ShutterState State)> ExecuteAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Note: this method supports resolving the shutter status of endpoints that support advanced
            // or basic actuation (depending on whether the virtual endpoint has the associated capabilities).
            //
            // For that, a first pass is made to collect the state of endpoints supporting advanced actuation and a second
            // pass is used to collect the state of the endpoints for which no state was extracted during the first pass.

            HashSet<OpenNettyEndpoint> set = [];

            if (endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
            {
                var dimensions = _service.EnumerateDimensionsAsync(
                    protocol         : endpoint.Protocol,
                    dimension        : OpenNettyDimensions.Automation.ShutterStatus,
                    gateway          : endpoint.Gateway,
                    options          : GetTransmissionOptions(endpoint),
                    cancellationToken: cancellationToken);

                await foreach (var result in dimensions
                    .Select(async (message, cancellationToken) => (
                        Values  : message.Values,
                        Endpoint: await _manager.FindEndpointByAddressAsync(endpoint.Gateway, message.Address, cancellationToken)))
                    .Where(static arguments => arguments.Endpoint is not null)
                    .Where(static arguments => arguments.Endpoint!.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
                    .Where(arguments => set.Add(arguments.Endpoint!))
                    .Select(static arguments => (arguments.Endpoint!, arguments.Values switch
                    {
                        ["10", string position, ..] => byte.Parse(position, CultureInfo.InvariantCulture) switch
                        {
                                   0        => OpenNettyModels.Automation.ShutterState.Closed,
                            >= 1 and <= 100 => OpenNettyModels.Automation.ShutterState.Open,
                                  255       => OpenNettyModels.Automation.ShutterState.Stopped,

                            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                        },

                        ["11" or "13", ..] => OpenNettyModels.Automation.ShutterState.Opening,
                        ["12" or "14", ..] => OpenNettyModels.Automation.ShutterState.Closing,

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                    })))
                {
                    yield return result;
                }
            }

            if (endpoint.HasCapability(OpenNettyCapabilities.BasicShutterState))
            {
                var results = _service.EnumerateStatusesAsync(
                    protocol         : endpoint.Protocol,
                    category         : OpenNettyCategories.Automation,
                    address          : endpoint.Address,
                    medium           : endpoint.Medium,
                    mode             : null,
                    filter           : static command => ValueTask.FromResult(
                        command == OpenNettyCommands.Automation.Stop ||
                        command == OpenNettyCommands.Automation.Up   ||
                        command == OpenNettyCommands.Automation.Down),
                    gateway          : endpoint.Gateway,
                    options          : GetTransmissionOptions(endpoint),
                    cancellationToken: cancellationToken);

                await foreach (var result in results
                    .Select(async (arguments, cancellationToken) => (
                        Command : arguments.Command,
                        Endpoint: await _manager.FindEndpointByAddressAsync(endpoint.Gateway, arguments.Address, cancellationToken)))
                    .Where(static arguments => arguments.Endpoint is not null)
                    .Where(static arguments => arguments.Endpoint!.HasCapability(OpenNettyCapabilities.BasicShutterState))
                    .Where(arguments => set.Add(arguments.Endpoint!))
                    .Select(static arguments => (arguments.Endpoint!,
                        arguments.Command == OpenNettyCommands.Automation.Stop ? OpenNettyModels.Automation.ShutterState.Stopped :
                        arguments.Command == OpenNettyCommands.Automation.Up   ? OpenNettyModels.Automation.ShutterState.Opening :
                        arguments.Command == OpenNettyCommands.Automation.Down ? OpenNettyModels.Automation.ShutterState.Closing :
                        throw new InvalidDataException(SR.GetResourceString(SR.ID0068)))))
                {
                    yield return result;
                }
            }
        }
    }

    /// <summary>
    /// Enumerates the current brightness of all the endpoints matching the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="IAsyncEnumerable{T}"/> that can be used to iterate the
    /// brightness returned by all the endpoints matching the specified endpoint.
    /// </returns>
    public virtual IAsyncEnumerable<(OpenNettyEndpoint Endpoint, OpenNettyModels.Lighting.SwitchState State)> EnumerateSwitchStatesAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // If the endpoint has a function type attached, ensure it is suitable for the requested operation.
        if (endpoint.GetStringSetting(OpenNettySettings.FunctionType) is not (null or OpenNettySettings.FunctionTypes.LightActuator))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
        }

        switch (endpoint.Protocol)
        {
            case OpenNettyProtocol.Nitoo:
            case OpenNettyProtocol.Scs when endpoint.Address is { Type: OpenNettyAddressType.ScsLightPoint } &&
                OpenNettyAddress.IsScsLightPointPointToPointAddress(endpoint.Address.Value):
            case OpenNettyProtocol.Zigbee when endpoint.Address is { Type: OpenNettyAddressType.Zigbee } &&
                OpenNettyAddress.ToZigbeeAddress(endpoint.Address.Value) is { Identifier: not 0, Unit: not 0 }:
                return GetSwitchStateAsync(endpoint, cancellationToken)
                    .AsTask()
                    .ToAsyncEnumerable()
                    .Select(state => (endpoint, state));

            default:
                var results = _service.EnumerateStatusesAsync(
                    protocol         : endpoint.Protocol,
                    category         : OpenNettyCategories.Lighting,
                    address          : endpoint.Address,
                    medium           : endpoint.Medium,
                    mode             : null,
                    filter           : static command => ValueTask.FromResult(
                        command == OpenNettyCommands.Lighting.Off  ||
                        command == OpenNettyCommands.Lighting.On   ||
                        command == OpenNettyCommands.Lighting.On20 ||
                        command == OpenNettyCommands.Lighting.On30 ||
                        command == OpenNettyCommands.Lighting.On40 ||
                        command == OpenNettyCommands.Lighting.On50 ||
                        command == OpenNettyCommands.Lighting.On60 ||
                        command == OpenNettyCommands.Lighting.On70 ||
                        command == OpenNettyCommands.Lighting.On80 ||
                        command == OpenNettyCommands.Lighting.On90 ||
                        command == OpenNettyCommands.Lighting.On100),
                    gateway          : endpoint.Gateway,
                    options          : GetTransmissionOptions(endpoint),
                    cancellationToken: cancellationToken);

                return results
                    .Select(async (arguments, cancellationToken) => (
                        Command : arguments.Command,
                        Endpoint: await _manager.FindEndpointByAddressAsync(endpoint.Gateway, arguments.Address, cancellationToken)))
                    .Where(static arguments => arguments.Endpoint is not null)
                    .Where(static arguments => arguments.Endpoint!.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
                    .Select(static arguments => (arguments.Endpoint!, arguments.Command != OpenNettyCommands.Lighting.Off ?
                        OpenNettyModels.Lighting.SwitchState.On :
                        OpenNettyModels.Lighting.SwitchState.Off));
        }
    }

    /// <summary>
    /// Inform all the Nitoo devices that memory entries pointing to the specified endpoint should be deleted.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask EraseAddressAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.MemoryWriting))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Diagnostics.AddressErase,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Broadcast,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Resolves the current brightness of the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous
    /// operation and whose result returns the current brightness of the specified endpoint.
    /// </returns>
    public virtual async ValueTask<byte> GetBrightnessAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) &&
            !endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // If the endpoint has a function type attached, ensure it is suitable for the requested operation.
        if (endpoint.GetStringSetting(OpenNettySettings.FunctionType) is not (null or OpenNettySettings.FunctionTypes.LightActuator))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
        }

        if (endpoint.Protocol is OpenNettyProtocol.Nitoo)
        {
            return await GetUnitDescriptionAsync(endpoint, cancellationToken) switch
            {
                { FunctionCode: 143, Values: [{ Length: > 0 } value, ..] }
                    => (byte) Math.Round(decimal.Parse(value, CultureInfo.InvariantCulture)),

                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
            };
        }

        else if (endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState))
        {
            // Note: while the brightness level is requested using the "DIMMER SPEED/LEVEL" DIMENSION, the result
            // might be returned using a different DIMENSION, "DIMMER STATUS". To ensure the brightness is
            // correctly resolved, both dimensions are observed before sending the DIMENSION REQUEST.
            var messages = _service.ObserveMessagesAsync(
                message          : OpenNettyMessage.CreateDimensionRequest(
                    protocol : endpoint.Protocol,
                    dimension: OpenNettyDimensions.Lighting.DimmerLevelSpeed,
                    address  : endpoint.Address,
                    medium   : endpoint.Medium,
                    mode     : null),
                gateway          : endpoint.Gateway,
                options          : GetTransmissionOptions(endpoint),
                cancellationToken: cancellationToken);

            return await messages
                .Where(static message => message.Dimension == OpenNettyDimensions.Lighting.DimmerLevelSpeed ||
                                         message.Dimension == OpenNettyDimensions.Lighting.DimmerStatus)
                .Where(message => message.Address == endpoint.Address)
                .Select(static arguments => (byte) (byte.Parse(arguments.Values[0], CultureInfo.InvariantCulture) - 100))
                .First()
                .Timeout(TimeSpan.FromSeconds(10))
                .RunAsync(cancellationToken);
        }

        else
        {
            return await _service.GetStatusAsync(
                protocol         : endpoint.Protocol,
                category         : OpenNettyCategories.Lighting,
                address          : endpoint.Address,
                medium           : endpoint.Medium,
                mode             : null,
                filter           : static command => ValueTask.FromResult(
                    command == OpenNettyCommands.Lighting.Off  ||
                    command == OpenNettyCommands.Lighting.On   ||
                    command == OpenNettyCommands.Lighting.On20 ||
                    command == OpenNettyCommands.Lighting.On30 ||
                    command == OpenNettyCommands.Lighting.On40 ||
                    command == OpenNettyCommands.Lighting.On50 ||
                    command == OpenNettyCommands.Lighting.On60 ||
                    command == OpenNettyCommands.Lighting.On70 ||
                    command == OpenNettyCommands.Lighting.On80 ||
                    command == OpenNettyCommands.Lighting.On90 ||
                    command == OpenNettyCommands.Lighting.On100),
                gateway          : endpoint.Gateway,
                options          : GetTransmissionOptions(endpoint),
                cancellationToken: cancellationToken) switch
                {
                    var command when command == OpenNettyCommands.Lighting.Off   => 0,
                    var command when command == OpenNettyCommands.Lighting.On    => 100,
                    var command when command == OpenNettyCommands.Lighting.On20  => 20,
                    var command when command == OpenNettyCommands.Lighting.On30  => 30,
                    var command when command == OpenNettyCommands.Lighting.On40  => 40,
                    var command when command == OpenNettyCommands.Lighting.On50  => 50,
                    var command when command == OpenNettyCommands.Lighting.On60  => 60,
                    var command when command == OpenNettyCommands.Lighting.On70  => 70,
                    var command when command == OpenNettyCommands.Lighting.On80  => 80,
                    var command when command == OpenNettyCommands.Lighting.On90  => 90,
                    var command when command == OpenNettyCommands.Lighting.On100 => 100,

                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                };
        }
    }

    /// <summary>
    /// Resolves the current date/time of the specified SCS gateway endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation
    /// and whose result returns the current date/time of the specified SCS gateway endpoint.
    /// </returns>
    public virtual async ValueTask<DateTimeOffset> GetDateTimeAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.DateTime))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        var values = await _service.GetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Management.DateTime,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);

        return new DateTimeOffset(
            year  : int.Parse(values[7], CultureInfo.InvariantCulture),
            month : int.Parse(values[6], CultureInfo.InvariantCulture),
            day   : int.Parse(values[5], CultureInfo.InvariantCulture),
            hour  : int.Parse(values[0], CultureInfo.InvariantCulture),
            minute: int.Parse(values[1], CultureInfo.InvariantCulture),
            second: int.Parse(values[2], CultureInfo.InvariantCulture),
            offset: values[3] switch
            {
                ['0', .. { Length: > 0 } value] => +TimeSpan.FromHours(int.Parse(value, CultureInfo.InvariantCulture)),
                ['1', .. { Length: > 0 } value] => -TimeSpan.FromHours(int.Parse(value, CultureInfo.InvariantCulture)),

                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
            });
    }

    /// <summary>
    /// Gets the device description of the specified Nitoo device endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation
    /// and whose result returns the device description of the specified Nitoo device endpoint.
    /// </returns>
    public virtual async ValueTask<OpenNettyModels.Diagnostics.DeviceDescription> GetDeviceDescriptionAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.DeviceDescription))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        var values = await _service.GetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Diagnostics.DeviceDescription,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);

        return OpenNettyModels.Diagnostics.DeviceDescription.CreateFromDeviceDescription([.. values]);
    }

    /// <summary>
    /// Resolves the firmware version of the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous
    /// operation and whose result returns the firmware version of the specified endpoint.
    /// </returns>
    public virtual async ValueTask<Version> GetFirmwareVersionAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.FirmwareVersion))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        if (endpoint.Protocol is OpenNettyProtocol.Nitoo && !endpoint.HasCapability(OpenNettyCapabilities.OpenWebNetGateway))
        {
            var description = await GetDeviceDescriptionAsync(endpoint, cancellationToken);
            return description.Version;
        }

        // Note: retrieving the firmware version of a battery-powered Zigbee endpoint
        // can take a while. To ensure the operation is not aborted before the endpoint
        // has a chance to respond, the timeouts are manually increased here.
        var options = GetTransmissionOptions(endpoint);

        if (endpoint.Address is not null && endpoint.HasCapability(OpenNettyCapabilities.ZigbeeEndDevice))
        {
            if (options.FrameAcknowledgementTimeout < TimeSpan.FromSeconds(45))
            {
                options = options with { FrameAcknowledgementTimeout = TimeSpan.FromSeconds(45) };
            }

            if (options.OutgoingMessageProcessingTimeout < TimeSpan.FromSeconds(45))
            {
                options = options with { OutgoingMessageProcessingTimeout = TimeSpan.FromSeconds(45) };
            }

            if (options.UniqueDimensionReplyTimeout < TimeSpan.FromSeconds(45))
            {
                options = options with { UniqueDimensionReplyTimeout = TimeSpan.FromSeconds(45) };
            }
        }

        var values = await _service.GetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Management.FirmwareVersion,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : options,
            cancellationToken: cancellationToken);

        return new Version(
            major: int.Parse(values[0], CultureInfo.InvariantCulture),
            minor: int.Parse(values[1], CultureInfo.InvariantCulture),
            build: int.Parse(values[2], CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Resolves the hardware version of the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous
    /// operation and whose result returns the hardware version of the specified endpoint.
    /// </returns>
    public virtual async ValueTask<Version> GetHardwareVersionAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.HardwareVersion))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // Note: retrieving the hardware version of a battery-powered Zigbee endpoint
        // can take a while. To ensure the operation is not aborted before the endpoint
        // has a chance to respond, the timeouts are manually increased here.
        var options = GetTransmissionOptions(endpoint);

        if (endpoint.Address is not null && endpoint.HasCapability(OpenNettyCapabilities.ZigbeeEndDevice))
        {
            if (options.FrameAcknowledgementTimeout < TimeSpan.FromSeconds(45))
            {
                options = options with { FrameAcknowledgementTimeout = TimeSpan.FromSeconds(45) };
            }

            if (options.OutgoingMessageProcessingTimeout < TimeSpan.FromSeconds(45))
            {
                options = options with { OutgoingMessageProcessingTimeout = TimeSpan.FromSeconds(45) };
            }

            if (options.UniqueDimensionReplyTimeout < TimeSpan.FromSeconds(45))
            {
                options = options with { UniqueDimensionReplyTimeout = TimeSpan.FromSeconds(45) };
            }
        }

        var values = await _service.GetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Management.HardwareVersion,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : options,
            cancellationToken: cancellationToken);

        return new Version(
            major: int.Parse(values[0], CultureInfo.InvariantCulture),
            minor: int.Parse(values[1], CultureInfo.InvariantCulture),
            build: int.Parse(values[2], CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Resolves the MAC address of the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous
    /// operation and whose result returns the MAC address of the specified endpoint.
    /// </returns>
    public virtual async ValueTask<string> GetMacAddressAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.MacAddress))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        var values = await _service.GetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Management.MacAddress,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);

        return string.Join(":", values.Select(static value => uint.Parse(value,
            CultureInfo.InvariantCulture).ToString("X2", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Reads all the memory entries associated with the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation
    /// and whose result returns the memory entries associated with the specified endpoint.
    /// </returns>
    public virtual async ValueTask<ImmutableArray<OpenNettyModels.Diagnostics.MemoryData>> GetMemoryDataAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.MemoryReading))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        var messages = _service.ObserveMessagesAsync(
            message          : OpenNettyMessage.CreateCommand(
                protocol: endpoint.Protocol,
                command : OpenNettyCommands.Diagnostics.MemoryRead,
                address : endpoint.Address,
                medium  : endpoint.Medium,
                mode    : null),
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken).Replay();

        await using var connection = await messages.ConnectAsync();

        // Note: while the memory content is requested using a BUS COMMAND, it is returned asynchronously by
        // Nitoo devices using DIMENSION READ frames after the initial BUS COMMAND has been acknowledged.
        var dimensions = messages
            .Where(static message => message.Type is OpenNettyMessageType.DimensionRead)
            .Where(static message => message.Dimension == OpenNettyDimensions.Diagnostics.MemoryDepth ||
                                     message.Dimension == OpenNettyDimensions.Diagnostics.MemoryData  ||
                                     message.Dimension == OpenNettyDimensions.Diagnostics.ExtendedMemoryData)
            .Where(message => message.Address == endpoint.Address);

        var count = await dimensions
            .First(static message => message.Dimension == OpenNettyDimensions.Diagnostics.MemoryDepth)
            .Select(static message => int.Parse(message.Values[0], CultureInfo.InvariantCulture))
            .Timeout(TimeSpan.FromSeconds(10))
            .RunAsync(cancellationToken);

        if (count is 0)
        {
            return [];
        }

        return [.. await dimensions
            .Where(static message => message.Dimension == OpenNettyDimensions.Diagnostics.MemoryData ||
                                     message.Dimension == OpenNettyDimensions.Diagnostics.ExtendedMemoryData)
            .Take(count)
            .Timeout(TimeSpan.FromSeconds(10))
            .ToAsyncEnumerable()
            .OrderBy(static message => byte.Parse(message.Values[3], CultureInfo.InvariantCulture))
            .Select(static message => OpenNettyModels.Diagnostics.MemoryData.CreateFromUnitDescription([.. message.Values]))
            .ToListAsync(cancellationToken)];
    }

    /// <summary>
    /// Gets the number of memory entries associated with the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation and whose
    /// result returns the number of memory entries associated with the specified endpoint.
    /// </returns>
    public virtual async ValueTask<byte> GetMemoryDepthAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.MemoryReading))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // Note: while the memory depth is requested using a BUS COMMAND, it is returned asynchronously by
        // Nitoo devices using DIMENSION READ frames after the initial BUS COMMAND has been acknowledged.
        var messages = _service.ObserveMessagesAsync(
            message          : OpenNettyMessage.CreateCommand(
                protocol: endpoint.Protocol,
                command : OpenNettyCommands.Diagnostics.MemoryRead,
                address : endpoint.Address,
                medium  : endpoint.Medium,
                mode    : null),
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);

        return await messages
            .Where(static message => message.Type is OpenNettyMessageType.DimensionRead)
            .Where(static message => message.Dimension == OpenNettyDimensions.Diagnostics.MemoryDepth)
            .Where(message => message.Address == endpoint.Address)
            .Select(static message => byte.Parse(message.Values[0], CultureInfo.InvariantCulture))
            .First()
            .Timeout(TimeSpan.FromSeconds(10))
            .RunAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the current pilot wire configuration of the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation and
    /// whose result returns the current pilot wire configuration of the specified endpoint.
    /// </returns>
    public virtual async ValueTask<OpenNettyModels.TemperatureControl.PilotWireConfiguration> GetPilotWireConfigurationAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.PilotWireControl))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        var description = await GetUnitDescriptionAsync(endpoint, cancellationToken);
        if (description.FunctionCode is not (6 or 132))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0068));
        }

        return OpenNettyModels.TemperatureControl.PilotWireConfiguration.CreateFromUnitDescription([.. description.Values]);
    }

    /// <summary>
    /// Resolves the current shutter position of the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous
    /// operation and whose result returns the current shutter position of the specified endpoint.
    /// </returns>
    public virtual async ValueTask<byte?> GetShutterPositionAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // If the endpoint has a function type attached, ensure it is suitable for the requested operation.
        if (endpoint.GetStringSetting(OpenNettySettings.FunctionType) is not (null or OpenNettySettings.FunctionTypes.AutomationActuator))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
        }

        return await _service.GetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Automation.ShutterStatus,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken) switch
            {
                [_, { Length: > 0 } value, ..] => byte.Parse(value, CultureInfo.InvariantCulture) switch
                {
                          0       => 0,
                         100      => 100,
                         255      => null,
                    byte position => position
                },

                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
            };
    }

    /// <summary>
    /// Gets the current shutter state of the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous
    /// operation and whose result returns the current shutter state of the specified endpoint.
    /// </returns>
    public virtual async ValueTask<OpenNettyModels.Automation.ShutterState> GetShutterStateAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.BasicShutterState) &&
            !endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // If the endpoint has a function type attached, ensure it is suitable for the requested operation.
        if (endpoint.GetStringSetting(OpenNettySettings.FunctionType) is not (null or OpenNettySettings.FunctionTypes.AutomationActuator))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
        }

        if (endpoint.Protocol is OpenNettyProtocol.Nitoo)
        {
            return await GetUnitDescriptionAsync(endpoint, cancellationToken) switch
            {
                { FunctionCode: 139, Values: [{ Length: > 0 } value, ..] } => value switch
                {
                    "100" or "102" => OpenNettyModels.Automation.ShutterState.Opening,
                    "0"   or "103" => OpenNettyModels.Automation.ShutterState.Closing,
                           _       => OpenNettyModels.Automation.ShutterState.Stopped
                },

                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
            };
        }

        else if (endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
        {
            return await _service.GetDimensionAsync(
                protocol         : endpoint.Protocol,
                dimension        : OpenNettyDimensions.Automation.ShutterStatus,
                address          : endpoint.Address,
                medium           : endpoint.Medium,
                mode             : null,
                gateway          : endpoint.Gateway,
                options          : GetTransmissionOptions(endpoint),
                cancellationToken: cancellationToken) switch
            {
                ["10", string position, ..] => byte.Parse(position, CultureInfo.InvariantCulture) switch
                {
                           0        => OpenNettyModels.Automation.ShutterState.Closed,
                    >= 1 and <= 100 => OpenNettyModels.Automation.ShutterState.Open,
                          255       => OpenNettyModels.Automation.ShutterState.Stopped,

                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                },

                ["11" or "13", ..] => OpenNettyModels.Automation.ShutterState.Opening,
                ["12" or "14", ..] => OpenNettyModels.Automation.ShutterState.Closing,

                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
            };
        }

        else
        {
            return await _service.GetStatusAsync(
                protocol         : endpoint.Protocol,
                category         : OpenNettyCategories.Automation,
                address          : endpoint.Address,
                medium           : endpoint.Medium,
                mode             : null,
                filter           : static command => ValueTask.FromResult(
                    command == OpenNettyCommands.Automation.Stop ||
                    command == OpenNettyCommands.Automation.Up   ||
                    command == OpenNettyCommands.Automation.Down),
                gateway          : endpoint.Gateway,
                options          : GetTransmissionOptions(endpoint),
                cancellationToken: cancellationToken) switch
            {
                OpenNettyCommand command when command == OpenNettyCommands.Automation.Stop
                    => OpenNettyModels.Automation.ShutterState.Stopped,

                OpenNettyCommand command when command == OpenNettyCommands.Automation.Up
                    => OpenNettyModels.Automation.ShutterState.Opening,

                OpenNettyCommand command when command == OpenNettyCommands.Automation.Down
                    => OpenNettyModels.Automation.ShutterState.Closing,

                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
            };
        }
    }

    /// <summary>
    /// Gets the smart meter indexes contained in the memory of the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation and whose
    /// result returns the smart meter indexes contained in the memory of the specified endpoint.
    /// </returns>
    public virtual async ValueTask<OpenNettyModels.TemperatureControl.SmartMeterIndexes> GetSmartMeterIndexesAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.SmartMeterIndexes))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        var values = await _service.GetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.TemperatureControl.SmartMeterIndexes,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);

        var indexes = OpenNettyModels.TemperatureControl.SmartMeterIndexes.CreateFromDimensionValues([.. values]);

        // Note: Nitoo smart meter devices are affected by an index overflow issue: to work around this limitation,
        // dedicated "index offset" settings can be used to amend the indexes returned by each smart meter device.
        return indexes with
        {
            BaseIndex = indexes.BaseIndex switch
            {
                OpenNettyModels.TemperatureControl.SmartMeterIndex index => new()
                {
                    BaseIndex    = ComputeIndex(index.BaseIndex, endpoint, OpenNettySettings.SmartMeterBaseIndexOffset),
                    OffPeakIndex = default
                },

                _ => null
            },
            BlueIndex = indexes.BlueIndex switch
            {
                OpenNettyModels.TemperatureControl.SmartMeterIndex index => new()
                {
                    BaseIndex    = ComputeIndex(index.BaseIndex,    endpoint, OpenNettySettings.SmartMeterBlueIndexOffsetBase),
                    OffPeakIndex = ComputeIndex(index.OffPeakIndex, endpoint, OpenNettySettings.SmartMeterBlueIndexOffsetOffPeak)
                },

                _ => null
            },
            PeakOffPeakIndex = indexes.PeakOffPeakIndex switch
            {
                OpenNettyModels.TemperatureControl.SmartMeterIndex index => new()
                {
                    BaseIndex    = ComputeIndex(index.BaseIndex,    endpoint, OpenNettySettings.SmartMeterPeakOffPeakIndexOffsetBase),
                    OffPeakIndex = ComputeIndex(index.OffPeakIndex, endpoint, OpenNettySettings.SmartMeterPeakOffPeakIndexOffsetOffPeak)
                },

                _ => null
            },
            RedIndex = indexes.RedIndex switch
            {
                OpenNettyModels.TemperatureControl.SmartMeterIndex index => new()
                {
                    BaseIndex    = ComputeIndex(index.BaseIndex,    endpoint, OpenNettySettings.SmartMeterRedIndexOffsetBase),
                    OffPeakIndex = ComputeIndex(index.OffPeakIndex, endpoint, OpenNettySettings.SmartMeterRedIndexOffsetOffPeak)
                },

                _ => null
            },
            WhiteIndex = indexes.WhiteIndex switch
            {
                OpenNettyModels.TemperatureControl.SmartMeterIndex index => new()
                {
                    BaseIndex    = ComputeIndex(index.BaseIndex,    endpoint, OpenNettySettings.SmartMeterWhiteIndexOffsetBase),
                    OffPeakIndex = ComputeIndex(index.OffPeakIndex, endpoint, OpenNettySettings.SmartMeterWhiteIndexOffsetOffPeak)
                },

                _ => null
            }
        };

        static ulong ComputeIndex(ulong index, OpenNettyEndpoint endpoint, OpenNettySetting setting) => index switch
        {
            ulong value when endpoint.GetIntegerSetting(setting) is long offset => (ulong) (((long) value) + offset),
            ulong value => value
        };
    }

    /// <summary>
    /// Gets the smart meter information resolved from the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation and
    /// whose result returns the smart meter information resolved from the specified endpoint.
    /// </returns>
    public virtual async ValueTask<OpenNettyModels.TemperatureControl.SmartMeterInformation> GetSmartMeterInformationAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.SmartMeterInformation))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        var description = await GetUnitDescriptionAsync(endpoint, cancellationToken);
        if (description.FunctionCode is not 7)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0068));
        }

        return OpenNettyModels.TemperatureControl.SmartMeterInformation.CreateFromUnitDescription([.. description.Values]);
    }

    /// <summary>
    /// Gets the current switch state of the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous
    /// operation and whose result returns the current switch state of the specified endpoint.
    /// </returns>
    public virtual async ValueTask<OpenNettyModels.Lighting.SwitchState> GetSwitchStateAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // If the endpoint has a function type attached, ensure it is suitable for the requested operation.
        if (endpoint.GetStringSetting(OpenNettySettings.FunctionType) is not (null or OpenNettySettings.FunctionTypes.LightActuator))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
        }

        return endpoint.Protocol switch
        {
            OpenNettyProtocol.Nitoo => await GetUnitDescriptionAsync(endpoint, cancellationToken) switch
            {
                { FunctionCode: 129, Values: [{ Length: > 0 } value] } => value is "128" or "129" or "130" ?
                    OpenNettyModels.Lighting.SwitchState.On :
                    OpenNettyModels.Lighting.SwitchState.Off,

                { FunctionCode: 143, Values: [{ Length: > 0 } value, ..] } => value is not "0" ?
                    OpenNettyModels.Lighting.SwitchState.On :
                    OpenNettyModels.Lighting.SwitchState.Off,

                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
            },

            _ => await _service.GetStatusAsync(
                protocol         : endpoint.Protocol,
                category         : OpenNettyCategories.Lighting,
                address          : endpoint.Address,
                medium           : endpoint.Medium,
                mode             : null,
                filter           : static command => ValueTask.FromResult(
                    command == OpenNettyCommands.Lighting.Off  ||
                    command == OpenNettyCommands.Lighting.On   ||
                    command == OpenNettyCommands.Lighting.On20 ||
                    command == OpenNettyCommands.Lighting.On30 ||
                    command == OpenNettyCommands.Lighting.On40 ||
                    command == OpenNettyCommands.Lighting.On50 ||
                    command == OpenNettyCommands.Lighting.On60 ||
                    command == OpenNettyCommands.Lighting.On70 ||
                    command == OpenNettyCommands.Lighting.On80 ||
                    command == OpenNettyCommands.Lighting.On90 ||
                    command == OpenNettyCommands.Lighting.On100),
                gateway          : endpoint.Gateway,
                options          : GetTransmissionOptions(endpoint),
                cancellationToken: cancellationToken) != OpenNettyCommands.Lighting.Off ?
                    OpenNettyModels.Lighting.SwitchState.On :
                    OpenNettyModels.Lighting.SwitchState.Off
        };
    }

    /// <summary>
    /// Gets the unit description of the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation
    /// and whose result returns the unit description of the specified endpoint.
    /// </returns>
    public virtual async ValueTask<OpenNettyModels.Diagnostics.UnitDescription> GetUnitDescriptionAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.UnitDescription))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        var values = await _service.GetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Diagnostics.UnitDescription,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);

        return OpenNettyModels.Diagnostics.UnitDescription.CreateFromUnitDescription([.. values]);
    }

    /// <summary>
    /// Gets the current uptime of the specified SCS gateway endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation
    /// and whose result returns the current uptime of the specified SCS gateway endpoint.
    /// </returns>
    public virtual async ValueTask<TimeSpan> GetUptimeAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.Uptime))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        var values = await _service.GetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Management.Uptime,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);

        return new TimeSpan(
            days   : int.Parse(values[0], CultureInfo.InvariantCulture),
            hours  : int.Parse(values[1], CultureInfo.InvariantCulture),
            minutes: int.Parse(values[2], CultureInfo.InvariantCulture),
            seconds: int.Parse(values[3], CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Gets the current water heater state of the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation and
    /// whose result returns the current water heater state of the specified endpoint.
    /// </returns>
    public virtual async ValueTask<OpenNettyModels.TemperatureControl.WaterHeaterState> GetWaterHeaterStateAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        var description = await GetUnitDescriptionAsync(endpoint, cancellationToken);
        if (description is not { FunctionCode: 133, Values: [{ Length: > 0 }] values })
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0068));
        }

        return values[0] switch
        {
            "0" or "32"         => OpenNettyModels.TemperatureControl.WaterHeaterState.Idle,
            "1" or "17" or "33" => OpenNettyModels.TemperatureControl.WaterHeaterState.Heating,

            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
        };
    }

    /// <summary>
    /// Asks the specified endpoint the Zigbee channel it uses.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous
    /// operation and whose result returns the Zigbee channel of the specified endpoint.
    /// </returns>
    public virtual async ValueTask<byte> GetZigbeeChannelAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        var values = await _service.GetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Management.ZigbeeChannel,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);

        return byte.Parse(values[0], CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Asks the specified endpoint to join a Zigbee network.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask JoinZigbeeNetworkAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // Note: joining a Zigbee network can take a while and the gateway only returns an
        // acknowledgment frame after the operation is complete. To ensure the gateway is
        // given enough time to join the network, the timeouts are increased if necessary.
        var options = GetTransmissionOptions(endpoint);
        if (options.FrameAcknowledgementTimeout < TimeSpan.FromSeconds(20))
        {
            options = options with { FrameAcknowledgementTimeout = TimeSpan.FromSeconds(20) };
        }

        if (options.OutgoingMessageProcessingTimeout < TimeSpan.FromSeconds(30))
        {
            options = options with { OutgoingMessageProcessingTimeout = TimeSpan.FromSeconds(30) };
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Management.JoinZigbeeNetwork,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : options,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Asks the specified endpoint to leave a Zigbee network.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask LeaveZigbeeNetworkAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // Note: leaving a Zigbee network can take a while and the gateway only returns an
        // acknowledgment frame after the operation is complete. To ensure the gateway is
        // given enough time to leave the network, the timeouts are increased if necessary.
        var options = GetTransmissionOptions(endpoint);
        if (options.FrameAcknowledgementTimeout < TimeSpan.FromSeconds(20))
        {
            options = options with { FrameAcknowledgementTimeout = TimeSpan.FromSeconds(20) };
        }

        if (options.OutgoingMessageProcessingTimeout < TimeSpan.FromSeconds(30))
        {
            options = options with { OutgoingMessageProcessingTimeout = TimeSpan.FromSeconds(30) };
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Management.LeaveZigbeeNetwork,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : options,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Moves the specified shutter endpoint down.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask MoveShutterDownAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.BasicShutterControl))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // If the endpoint has a function type attached, ensure it is suitable for the requested operation.
        if (endpoint.GetStringSetting(OpenNettySettings.FunctionType) is not (null or OpenNettySettings.FunctionTypes.AutomationActuator))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Automation.Down,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Moves the specified shutter endpoint up.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask MoveShutterUpAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.BasicShutterControl))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // If the endpoint has a function type attached, ensure it is suitable for the requested operation.
        if (endpoint.GetStringSetting(OpenNettySettings.FunctionType) is not (null or OpenNettySettings.FunctionTypes.AutomationActuator))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Automation.Up,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Asks the specified endpoint to open a Zigbee network.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask OpenZigbeeNetworkAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.ZigbeeNetworkManagement))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Management.OpenZigbeeNetwork,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Clear all the memory entries associated with the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask ResetMemoryDataAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.MemoryWriting))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Diagnostics.MemoryReset,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sets the brightness of the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="level">The brightness level, from 0 to 100.</param>
    /// <param name="duration">The optional transition duration.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask SetBrightnessAsync(
        OpenNettyEndpoint endpoint,
        byte level,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (level is > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        // Note: for unknown reasons, specifying a SPEED parameter that is less than 10 (2 seconds * 5)
        // results in an immediate - rather than progressive - brightness change on Nitoo devices when
        // the requested level is higher than 50%. To discourage users of this API to set values that
        // may exhibit this issue, a sanity check is performed here to require an adequate duration.
        if (endpoint.Protocol is OpenNettyProtocol.Nitoo && level is > 50 &&
            duration is not null && duration < TimeSpan.FromSeconds(2))
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        if (endpoint.Protocol is OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee &&
            duration is not null && duration > TimeSpan.FromSeconds(50))
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        // Note: Nitoo devices support a very long duration, but to encourage users of this API
        // to use reasonable values, the maximum duration allowed is currently set to 5 minutes.
        if (endpoint.Protocol is OpenNettyProtocol.Nitoo &&
            duration is not null && duration > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        if (endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingControl))
        {
            // Note: Zigbee/SCS gateways generally don't treat DimmerLevel100=100 (with 100 meaning "off") as
            // an equivalent of the OFF BUS COMMAND in DIMENSION SET requests. To ensure the light is turned
            // off when the brightness is set to 0, an OFF BUS COMMAND is used for Zigbee/SCS gateways.
            if (level is 0 && endpoint.Protocol is OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee)
            {
                return _service.ExecuteCommandAsync(
                    protocol         : endpoint.Protocol,
                    command          : OpenNettyCommands.Lighting.Off,
                    address          : endpoint.Address,
                    medium           : endpoint.Medium,
                    mode             : null,
                    gateway          : endpoint.Gateway,
                    options          : GetTransmissionOptions(endpoint),
                    cancellationToken: cancellationToken);
            }

            return _service.SetDimensionAsync(
                protocol         : endpoint.Protocol,
                dimension        : OpenNettyDimensions.Lighting.DimmerLevelSpeed,
                values           :
                [
                    // Note: while Zigbee/SCS gateways use 101-200 as the brightness range, the Nitoo gateway uses 0-100.
                    /* LEVEL: */ endpoint.Protocol is OpenNettyProtocol.Nitoo ?
                        level.ToString(CultureInfo.InvariantCulture) :
                        (level + 100).ToString(CultureInfo.InvariantCulture),
                    /* SPEED: */ duration switch
                    {
                        // When explicitly set, use the duration specified by the caller to determine the speed.
                        TimeSpan value => ((long) value.TotalSeconds * 5 + .5).ToString(CultureInfo.InvariantCulture),

                        // For Nitoo devices, compute an optimal speed based on the brightness level to ensure a smooth transition.
                        null when endpoint.Protocol is OpenNettyProtocol.Nitoo && level is <= 50 => "10",
                        null when endpoint.Protocol is OpenNettyProtocol.Nitoo && level is >  50
                            => ((long) ((level / 10) + .5)).ToString(CultureInfo.InvariantCulture),

                        // Otherwise, use the last used speed.
                        null => "0"
                    }
                ],
                address          : endpoint.Address,
                medium           : endpoint.Medium,
                mode             : null,
                gateway          : endpoint.Gateway,
                options          : GetTransmissionOptions(endpoint),
                cancellationToken: cancellationToken);
        }

        else if (endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingControl))
        {
            return _service.ExecuteCommandAsync(
                protocol         : endpoint.Protocol,
                command          : level switch
                {
                           0        => OpenNettyCommands.Lighting.Off,
                    >= 1  and <= 29 => OpenNettyCommands.Lighting.On20,
                    >= 30 and <= 39 => OpenNettyCommands.Lighting.On30,
                    >= 40 and <= 49 => OpenNettyCommands.Lighting.On40,
                    >= 50 and <= 59 => OpenNettyCommands.Lighting.On50,
                    >= 60 and <= 69 => OpenNettyCommands.Lighting.On60,
                    >= 70 and <= 79 => OpenNettyCommands.Lighting.On70,
                    >= 80 and <= 89 => OpenNettyCommands.Lighting.On80,
                    >= 90 and <= 99 => OpenNettyCommands.Lighting.On90,
                           _        => OpenNettyCommands.Lighting.On100
                },
                address          : endpoint.Address,
                medium           : endpoint.Medium,
                mode             : null,
                gateway          : endpoint.Gateway,
                options          : GetTransmissionOptions(endpoint),
                cancellationToken: cancellationToken);
        }

        throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
    }

    /// <summary>
    /// Sets the date/time of the specified SCS gateway endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="date">The date/time.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask SetDateTimeAsync(
        OpenNettyEndpoint endpoint,
        DateTimeOffset date,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.DateTime))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.SetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Management.DateTime,
            values           :
            [
                date.Hour.ToString("00", CultureInfo.InvariantCulture),
                date.Minute.ToString("00", CultureInfo.InvariantCulture),
                date.Second.ToString("00", CultureInfo.InvariantCulture),
                date.Offset switch
                {
                    TimeSpan offset when offset  > TimeSpan.Zero => "0" + offset.TotalHours.ToString("00", CultureInfo.InvariantCulture),
                    TimeSpan offset when offset == TimeSpan.Zero => "000",
                    TimeSpan offset when offset  < TimeSpan.Zero => "1" + offset.TotalHours.ToString("00", CultureInfo.InvariantCulture),

                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
                },
                ((int) date.DayOfWeek).ToString("00", CultureInfo.InvariantCulture),
                date.Day.ToString("00", CultureInfo.InvariantCulture),
                date.Month.ToString("00", CultureInfo.InvariantCulture),
                date.Year.ToString("0000", CultureInfo.InvariantCulture)
            ],
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sets the pilot wire degoration mode that will be enforced by the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="mode">The derogation mode.</param>
    /// <param name="duration">The derogation duration.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask SetPilotWireDerogationModeAsync(
        OpenNettyEndpoint endpoint,
        OpenNettyModels.TemperatureControl.PilotWireMode mode,
        OpenNettyModels.TemperatureControl.PilotWireDerogationDuration duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!Enum.IsDefined(duration))
        {
            throw new InvalidDataException(SR.GetResourceString(SR.ID0068));
        }

        if (!endpoint.HasCapability(OpenNettyCapabilities.PilotWireDerogation))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        var value = mode switch
        {
            OpenNettyModels.TemperatureControl.PilotWireMode.Comfort         => 0,
            OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne => 1,
            OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo => 2,
            OpenNettyModels.TemperatureControl.PilotWireMode.Eco             => 3,
            OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection => 4,

            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
        };

        value |= duration switch
        {
            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.None       => 0b_0000_0000,
            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours  => 0b_0010_0000,
            OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours => 0b_1000_0000,

            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
        };

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.TemperatureControl.WirePilotDerogationMode.WithParameters(
                /* MODE: */ value.ToString(CultureInfo.InvariantCulture)),
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Multicast,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sets the pilot wire setpoint mode that will be applied by the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="mode">The setpoint mode.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask SetPilotWireSetpointModeAsync(
        OpenNettyEndpoint endpoint,
        OpenNettyModels.TemperatureControl.PilotWireMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.PilotWireControl))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        var value = mode switch
        {
            OpenNettyModels.TemperatureControl.PilotWireMode.Comfort         => 0,
            OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne => 1,
            OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo => 2,
            OpenNettyModels.TemperatureControl.PilotWireMode.Eco             => 3,
            OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection => 4,

            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
        };

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.TemperatureControl.WirePilotSetpointMode.WithParameters(
                /* MODE: */ value.ToString(CultureInfo.InvariantCulture)),
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Multicast,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sets the shutter position of the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="position">The shutter position, from 0 to 100.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask SetShutterPositionAsync(
        OpenNettyEndpoint endpoint,
        byte position,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (position is > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (!endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterState))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.SetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Automation.ShutterGoToLevel,
            values           : endpoint.Protocol switch
            {
                OpenNettyProtocol.Zigbee => [     position.ToString(CultureInfo.InvariantCulture)],
                            _            => ["0", position.ToString(CultureInfo.InvariantCulture)]
            },
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sets the water heater setpoint mode that will be applied by the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="mode">The water heater mode.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask SetWaterHeaterSetpointModeAsync(
        OpenNettyEndpoint endpoint,
        OpenNettyModels.TemperatureControl.WaterHeaterMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.SetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.TemperatureControl.WaterHeatingMode,
            values           : [mode switch
            {
                OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOff => "0",
                OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn  => "1",
                OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic => "2",

                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0068))
            }],
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Stops the specified shutter endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask StopShutterAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.BasicShutterControl))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // If the endpoint has a function type attached, ensure it is suitable for the requested operation.
        if (endpoint.GetStringSetting(OpenNettySettings.FunctionType) is not (null or OpenNettySettings.FunctionTypes.AutomationActuator))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Automation.Stop,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Switches the specified endpoint off.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask SwitchOffAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchControl))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // If the endpoint has a function type attached, ensure it is suitable for the requested operation.
        if (endpoint.GetStringSetting(OpenNettySettings.FunctionType) is not (null or OpenNettySettings.FunctionTypes.LightActuator))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
        }

        // If the endpoint was configured to use the special switch mode, OFF commands are not valid.
        if (endpoint.GetStringSetting(OpenNettySettings.SwitchMode) is OpenNettySettings.SwitchModes.PushButton)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Lighting.Off,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Switches the specified endpoint on.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask SwitchOnAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchControl))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // If the endpoint has a function type attached, ensure it is suitable for the requested operation.
        if (endpoint.GetStringSetting(OpenNettySettings.FunctionType) is not (null or OpenNettySettings.FunctionTypes.LightActuator))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Lighting.On,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint) with
            {
                // If the endpoint was configured to use the push-button mode, always disable retransmissions
                // as ON commands are not idempotent when using this mode, which may result in unwanted results.
                DisallowRetransmissions = endpoint.GetStringSetting(OpenNettySettings.SwitchMode)
                    is OpenNettySettings.SwitchModes.PushButton
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Toggles the state of the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual async ValueTask ToggleAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchControl))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        // If the endpoint has a function type attached, ensure it is suitable for the requested operation.
        if (endpoint.GetStringSetting(OpenNettySettings.FunctionType) is not (null or OpenNettySettings.FunctionTypes.LightActuator))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0099));
        }

        // Nitoo and SCS gateways don't natively support toggle BUS COMMANDS (unlike Zigbee
        // gateways). To work around this limitation, the current status of the device/unit
        // is retrieved first and an ON or OFF command is sent depending on the result.
        if (endpoint.Protocol is OpenNettyProtocol.Nitoo or OpenNettyProtocol.Scs)
        {
            if (!endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
            }

            if (endpoint.GetStringSetting(OpenNettySettings.SwitchMode) is OpenNettySettings.SwitchModes.PushButton)
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
            }

            var state = await GetSwitchStateAsync(endpoint, cancellationToken);

            await _service.ExecuteCommandAsync(
                protocol         : endpoint.Protocol,
                command          : state is OpenNettyModels.Lighting.SwitchState.On ?
                    OpenNettyCommands.Lighting.Off :
                    OpenNettyCommands.Lighting.On,
                address          : endpoint.Address,
                medium           : endpoint.Medium,
                mode             : null,
                gateway          : endpoint.Gateway,
                options          : GetTransmissionOptions(endpoint),
                cancellationToken: cancellationToken);
        }

        else
        {
            await _service.ExecuteCommandAsync(
                protocol         : endpoint.Protocol,
                command          : OpenNettyCommands.Lighting.Toggle,
                address          : endpoint.Address,
                medium           : endpoint.Medium,
                mode             : null,
                gateway          : endpoint.Gateway,
                options          : GetTransmissionOptions(endpoint),
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Removes the specified endpoint from the list of devices associated with the Zigbee scenario that is currently open.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask UnbindAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.ZigbeeBinding))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0069));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.ScenariosPlus.UnbindingRequest,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : GetTransmissionOptions(endpoint),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Gets the transmission options that will be used to communicate with the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <returns>The transmission options that will be used to communicate with the specified endpoint.</returns>
    protected virtual OpenNettyTransmissionOptions GetTransmissionOptions(OpenNettyEndpoint endpoint)
        => endpoint.GetBooleanSetting(OpenNettySettings.ActionValidation) switch
        {
            null => endpoint.Gateway.Options.DefaultTransmissionOptions,
            true => endpoint.Gateway.Options.DefaultTransmissionOptions with
            {
                IgnoreActionValidation = true
            },
            false => endpoint.Gateway.Options.DefaultTransmissionOptions with
            {
                IgnoreActionValidation = false
            }
        };
}
