/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Globalization;
using System.Reactive.Linq;

namespace OpenNetty;

/// <summary>
/// Represents a high-level service that can be used to execute common OpenWebNet operations.
/// </summary>
public class OpenNettyController
{
    private readonly IOpenNettyService _service;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyController"/> class.
    /// </summary>
    /// <param name="service">The OpenNetty service.</param>
    public OpenNettyController(IOpenNettyService service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));

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
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
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

                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                },
                data.Address.ToString(),
                data.FunctionCode.ToString(CultureInfo.InvariantCulture)
            ],
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Unicast,
            gateway          : endpoint.Gateway,
            options          : endpoint.GetBooleanSetting(OpenNettySettings.ActionValidation) is not false ?
                OpenNettyTransmissionOptions.RequireActionValidation :
                OpenNettyTransmissionOptions.None,
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
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Scenario.BindingRequest,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
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

        if (!endpoint.HasCapability(OpenNettyCapabilities.ZigbeeSupervision))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Management.SupervisorRemove,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
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

        if (!endpoint.HasCapability(OpenNettyCapabilities.ZigbeeSupervision))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Management.Supervisor,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Cancels the pilot wire derogation mode currently enforced by the specified Nitoo gateway endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask CancelPilotWireDerogationModeAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }
        
        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.TemperatureControl.CancelWirePilotDerogationMode,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Multicast,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Dispatches a virtual basic (action) scenario for the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual async ValueTask DispatchBasicScenarioAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.BasicScenario))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        await _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Scenario.Action,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Broadcast,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
            cancellationToken: cancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken);

        await _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Scenario.StopAction,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Broadcast,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Dispatches a virtual ON/OFF scenario for the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="state">The state.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual ValueTask DispatchOnOffScenarioAsync(
        OpenNettyEndpoint endpoint,
        OpenNettyModels.Lighting.SwitchState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.OnOffScenario))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : state == OpenNettyModels.Lighting.SwitchState.On ?
                OpenNettyCommands.Lighting.On : OpenNettyCommands.Lighting.Off,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Broadcast,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Dispatches a virtual progressive scenario for the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="duration">The scenario duration.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual async ValueTask DispatchProgressiveScenarioAsync(
        OpenNettyEndpoint endpoint,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.ProgressiveScenario))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        await _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Scenario.ActionInTime.WithParameters(
                /* TIME: */ ((long) (duration.TotalSeconds * 5 + .5)).ToString(CultureInfo.InvariantCulture)),
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Broadcast,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
            cancellationToken: cancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken);

        await _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Scenario.StopAction,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Broadcast,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Dispatches a virtual timed scenario for the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="duration">The duration after which associated devices will change their state.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public virtual async ValueTask DispatchTimedScenarioAsync(
        OpenNettyEndpoint endpoint,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.TimedScenario))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        await _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Scenario.ActionForTime.WithParameters(
                /* TIME: */ ((long) (duration.TotalSeconds * 5 + .5)).ToString(CultureInfo.InvariantCulture)),
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Broadcast,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
            cancellationToken: cancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken);

        await _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Scenario.StopAction,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Broadcast,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
            cancellationToken: cancellationToken);
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
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Diagnostics.AddressErase,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Broadcast,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
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
    public virtual async ValueTask<ushort> GetBrightnessAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return endpoint.Protocol switch
        {
            OpenNettyProtocol.Nitoo
                when endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState) ||
                     endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState)
                => await GetUnitDescriptionAsync(endpoint, cancellationToken) switch
                {
                    { FunctionCode: 143, Values: [{ Length: > 0 } value, ..] }
                        => (ushort) Math.Round(decimal.Parse(value, CultureInfo.InvariantCulture)),

                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                },

            OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee
                when endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingState)
                => await _service.GetDimensionAsync(
                    protocol         : endpoint.Protocol,
                    dimension        : OpenNettyDimensions.Lighting.DimmerLevelSpeed,
                    address          : endpoint.Address,
                    medium           : endpoint.Medium,
                    mode             : null,
                    filter           : static dimension => ValueTask.FromResult(
                        dimension == OpenNettyDimensions.Lighting.DimmerLevelSpeed ||
                        dimension == OpenNettyDimensions.Lighting.DimmerStatus),
                    gateway          : endpoint.Gateway,
                    options          : OpenNettyTransmissionOptions.None,
                    cancellationToken: cancellationToken) switch
                    {
                        [{ Length: > 0 } value, ..] => (ushort) (ushort.Parse(value, CultureInfo.InvariantCulture) - 100),

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                    },

            OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee
                when endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingState)
                => await _service.GetStatusAsync(
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
                    options          : OpenNettyTransmissionOptions.None,
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

                        _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                    },

            _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0076))
        };
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
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        var values = await _service.GetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Management.DateTime,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            filter           : null,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
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

                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
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
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        var values = await _service.GetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Diagnostics.DeviceDescription,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Unicast,
            filter           : null,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
            cancellationToken: cancellationToken);

        return OpenNettyModels.Diagnostics.DeviceDescription.CreateFromDeviceDescription(values);
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
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        // Note: while the memory content is requested using a BUS COMMAND, it is returned asynchronously by
        // Nitoo devices using DIMENSION READ frames after the initial BUS COMMAND has been acknowledged.
        var dimensions = _service.ObserveDimensionsAsync(OpenNettyProtocol.Nitoo, OpenNettyCategories.Diagnostics, endpoint.Gateway)
            .Where(static arguments => arguments.Dimension == OpenNettyDimensions.Diagnostics.MemoryDepth ||
                                       arguments.Dimension == OpenNettyDimensions.Diagnostics.MemoryData  ||
                                       arguments.Dimension == OpenNettyDimensions.Diagnostics.ExtendedMemoryData)
            .Where(arguments => arguments.Address == endpoint.Address)
            .Replay();

        // Connect the observable just before sending the command to ensure
        // the dimensions are not missed due to a race condition.
        await using var connection = await dimensions.ConnectAsync();

        await _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Diagnostics.MemoryRead,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Unicast,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
            cancellationToken: cancellationToken);

        var count = await dimensions
            .FirstOrDefault(static arguments => arguments.Dimension == OpenNettyDimensions.Diagnostics.MemoryDepth)
            .Select(static arguments => int.Parse(arguments.Values[0], CultureInfo.InvariantCulture))
            .Timeout(TimeSpan.FromSeconds(3))
            .RunAsync(cancellationToken);

        if (count is 0)
        {
            return [];
        }

        return [.. await dimensions
            .Where(static arguments => arguments.Dimension == OpenNettyDimensions.Diagnostics.MemoryData ||
                                       arguments.Dimension == OpenNettyDimensions.Diagnostics.ExtendedMemoryData)
            .Take(count)
            .Timeout(TimeSpan.FromSeconds(10))
            .ToAsyncEnumerable()
            .OrderBy(static arguments => ushort.Parse(arguments.Values[3], CultureInfo.InvariantCulture))
            .Select(static arguments => OpenNettyModels.Diagnostics.MemoryData.CreateFromUnitDescription(arguments.Values))
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
    public virtual async ValueTask<ushort> GetMemoryDepthAsync(
        OpenNettyEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.HasCapability(OpenNettyCapabilities.MemoryReading))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        // Note: while the memory depth is requested using a BUS COMMAND, it is returned asynchronously by
        // Nitoo devices using DIMENSION READ frames after the initial BUS COMMAND has been acknowledged.
        var dimensions = _service.ObserveDimensionsAsync(OpenNettyProtocol.Nitoo, OpenNettyCategories.Diagnostics, endpoint.Gateway)
            .Where(static arguments => arguments.Dimension == OpenNettyDimensions.Diagnostics.MemoryDepth)
            .Where(arguments => arguments.Address == endpoint.Address)
            .Replay();

        // Connect the observable just before sending the command to ensure
        // the dimensions are not missed due to a race condition.
        await using var connection = await dimensions.ConnectAsync();

        await _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Diagnostics.MemoryRead,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Unicast,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
            cancellationToken: cancellationToken);

        return await dimensions
            .Select(static arguments => ushort.Parse(arguments.Values[0], CultureInfo.InvariantCulture))
            .FirstOrDefault()
            .Timeout(TimeSpan.FromSeconds(3))
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

        if (!endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        var description = await GetUnitDescriptionAsync(endpoint, cancellationToken);
        if (description is not { FunctionCode: 6, Values: [{ Length: > 0 }] values })
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        return OpenNettyModels.TemperatureControl.PilotWireConfiguration.CreateFromUnitDescription(values);
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
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        var values = await _service.GetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Diagnostics.UnitDescription,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Unicast,
            filter           : null,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
            cancellationToken: cancellationToken);

        return OpenNettyModels.Diagnostics.UnitDescription.CreateFromUnitDescription(values);
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
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        var values = await _service.GetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.Management.Uptime,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            filter           : null,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
            cancellationToken: cancellationToken);

        return new TimeSpan(
            days   : int.Parse(values[0], CultureInfo.InvariantCulture),
            hours  : int.Parse(values[1], CultureInfo.InvariantCulture),
            minutes: int.Parse(values[2], CultureInfo.InvariantCulture),
            seconds: int.Parse(values[3], CultureInfo.InvariantCulture));
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
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        var values = await _service.GetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.TemperatureControl.SmartMeterIndexes,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Unicast,
            filter           : null,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
            cancellationToken: cancellationToken);

        return OpenNettyModels.TemperatureControl.SmartMeterIndexes.CreateFromDimensionValues(values);
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
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        var description = await GetUnitDescriptionAsync(endpoint, cancellationToken);
        if (description is not { FunctionCode: 7, Values: [{ Length: > 0 }] values })
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        return OpenNettyModels.TemperatureControl.SmartMeterInformation.CreateFromUnitDescription(values);
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
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
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

                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
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
                options          : OpenNettyTransmissionOptions.None,
                cancellationToken: cancellationToken) != OpenNettyCommands.Lighting.Off ?
                    OpenNettyModels.Lighting.SwitchState.On :
                    OpenNettyModels.Lighting.SwitchState.Off
        };
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
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        var description = await GetUnitDescriptionAsync(endpoint, cancellationToken);
        if (description is not { FunctionCode: 133, Values: [{ Length: > 0 }] values })
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        return values[0] switch
        {
            "0" or "32"         => OpenNettyModels.TemperatureControl.WaterHeaterState.Idle,
            "1" or "17" or "33" => OpenNettyModels.TemperatureControl.WaterHeaterState.Heating,

            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
        };
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
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Diagnostics.MemoryReset,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Unicast,
            gateway          : endpoint.Gateway,
            options          : endpoint.GetBooleanSetting(OpenNettySettings.ActionValidation) is not false ?
                OpenNettyTransmissionOptions.RequireActionValidation :
                OpenNettyTransmissionOptions.None,
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
        ushort level,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (level is > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        // Note: for unknown reasons, specifying a SPEED parameter that is less than 10 (2 seconds * 5)
        // results in an immediumte - rather than progressive - brightness change on Nitoo devices when
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
                    options          : OpenNettyTransmissionOptions.None,
                    cancellationToken: cancellationToken);
            }

            // Note: while Zigbee/SCS gateways use 101-200 as the brightness range, the Nitoo gateway uses 0-100.
            else if (endpoint.Protocol is OpenNettyProtocol.Nitoo)
            {
                return _service.SetDimensionAsync(
                    protocol         : endpoint.Protocol,
                    dimension        : OpenNettyDimensions.Lighting.DimmerLevelSpeed,
                    values           : [
                        level.ToString(CultureInfo.InvariantCulture),
                        duration is not null ?
                            ((long) ((duration ?? TimeSpan.FromSeconds(2)).TotalSeconds * 5 + .5)).ToString(CultureInfo.InvariantCulture) :
                            "0"
                    ],
                    address          : endpoint.Address,
                    medium           : endpoint.Medium,
                    mode             : OpenNettyMode.Unicast,
                    gateway          : endpoint.Gateway,
                    options          : endpoint.GetBooleanSetting(OpenNettySettings.ActionValidation) is not false ?
                        OpenNettyTransmissionOptions.RequireActionValidation :
                        OpenNettyTransmissionOptions.None,
                    cancellationToken: cancellationToken);
            }

            return _service.SetDimensionAsync(
                protocol         : endpoint.Protocol,
                dimension        : OpenNettyDimensions.Lighting.DimmerLevelSpeed,
                values           : [
                    (level + 100).ToString(CultureInfo.InvariantCulture),
                    duration is not null ?
                        ((long) ((duration ?? TimeSpan.FromSeconds(2)).TotalSeconds * 5 + .5)).ToString(CultureInfo.InvariantCulture) :
                        "0"
                ],
                address          : endpoint.Address,
                medium           : endpoint.Medium,
                mode             : null,
                gateway          : endpoint.Gateway,
                options          : OpenNettyTransmissionOptions.None,
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
                mode             : endpoint.Protocol is OpenNettyProtocol.Nitoo ? OpenNettyMode.Unicast : null,
                gateway          : endpoint.Gateway,
                options          : endpoint.Protocol is OpenNettyProtocol.Nitoo &&
                    endpoint.GetBooleanSetting(OpenNettySettings.ActionValidation) is not false ?
                    OpenNettyTransmissionOptions.RequireActionValidation :
                    OpenNettyTransmissionOptions.None,
                cancellationToken: cancellationToken);
        }

        throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
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
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
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

                    _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
                },
                ((int) date.DayOfWeek).ToString("00", CultureInfo.InvariantCulture),
                date.Day.ToString("00", CultureInfo.InvariantCulture),
                date.Month.ToString("00", CultureInfo.InvariantCulture),
                date.Year.ToString("0000", CultureInfo.InvariantCulture),
            ],
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
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

        if (!Enum.IsDefined(mode))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        if (!Enum.IsDefined(duration))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        if (!endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        var value = mode switch
        {
            OpenNettyModels.TemperatureControl.PilotWireMode.Comfort         => 0,
            OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne => 1,
            OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo => 2,
            OpenNettyModels.TemperatureControl.PilotWireMode.Eco             => 3,
            OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection => 4,

            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
        };

        if (duration is OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.FourHours)
        {
            value += 32;
        }

        else if (duration is OpenNettyModels.TemperatureControl.PilotWireDerogationDuration.EightHours)
        {
            value += 128;
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.TemperatureControl.WirePilotDerogationMode.WithParameters(
                /* MODE: */ value.ToString(CultureInfo.InvariantCulture)),
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Multicast,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
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

        if (!Enum.IsDefined(mode))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        if (!endpoint.HasCapability(OpenNettyCapabilities.PilotWireHeating))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        var value = mode switch
        {
            OpenNettyModels.TemperatureControl.PilotWireMode.Comfort         => 0,
            OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusOne => 1,
            OpenNettyModels.TemperatureControl.PilotWireMode.ComfortMinusTwo => 2,
            OpenNettyModels.TemperatureControl.PilotWireMode.Eco             => 3,
            OpenNettyModels.TemperatureControl.PilotWireMode.FrostProtection => 4,

            _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
        };

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.TemperatureControl.WirePilotSetpointMode.WithParameters(
                /* MODE: */ value.ToString(CultureInfo.InvariantCulture)),
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Multicast,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
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

        if (!Enum.IsDefined(mode))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        if (!endpoint.HasCapability(OpenNettyCapabilities.WaterHeating))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        return _service.SetDimensionAsync(
            protocol         : endpoint.Protocol,
            dimension        : OpenNettyDimensions.TemperatureControl.WaterHeatingMode,
            values           : [mode switch
            {
                OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOff => "0",
                OpenNettyModels.TemperatureControl.WaterHeaterMode.ForcedOn  => "1",
                OpenNettyModels.TemperatureControl.WaterHeaterMode.Automatic => "2",

                _ => throw new InvalidDataException(SR.GetResourceString(SR.ID0075))
            }],
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : OpenNettyMode.Unicast,
            gateway          : endpoint.Gateway,
            options          : endpoint.GetBooleanSetting(OpenNettySettings.ActionValidation) is not false ?
                OpenNettyTransmissionOptions.RequireActionValidation :
                OpenNettyTransmissionOptions.None,
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

        if (!endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitching))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        if (string.Equals(endpoint.GetStringSetting(OpenNettySettings.SwitchMode),
            "Push button", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Lighting.Off,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : endpoint.Protocol is OpenNettyProtocol.Nitoo &&
                endpoint.GetBooleanSetting(OpenNettySettings.ActionValidation) is not false ?
                OpenNettyTransmissionOptions.RequireActionValidation :
                OpenNettyTransmissionOptions.None,
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

        if (!endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitching))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        var options = OpenNettyTransmissionOptions.None;

        if (endpoint.Protocol is OpenNettyProtocol.Nitoo &&
            endpoint.GetBooleanSetting(OpenNettySettings.ActionValidation) is not false)
        {
            options |= OpenNettyTransmissionOptions.RequireActionValidation;
        }

        // If the endpoint was configured to use the push-button mode, always disable retransmissions
        // as ON commands are not idempotent when using this mode, which may result in unwanted results.
        if (string.Equals(endpoint.GetStringSetting(OpenNettySettings.SwitchMode),
            "Push button", StringComparison.OrdinalIgnoreCase))
        {
            options |= OpenNettyTransmissionOptions.DisallowRetransmissions;
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Lighting.On,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : options,
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

        if (!endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitching))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        // Nitoo and SCS gateways don't natively support toggle BUS COMMANDS (unlike Zigbee
        // gateways). To work around this limitation, the current status of the device/unit
        // is retrieved first and an ON or OFF command is sent depending on the result.
        if (endpoint.Protocol is OpenNettyProtocol.Nitoo or OpenNettyProtocol.Scs)
        {
            if (!endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchState))
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
            }

            if (string.Equals(endpoint.GetStringSetting(OpenNettySettings.SwitchMode),
                "Push button", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
            }

            var state = await GetSwitchStateAsync(endpoint, cancellationToken);

            await _service.ExecuteCommandAsync(
                protocol         : endpoint.Protocol,
                command          : state is OpenNettyModels.Lighting.SwitchState.On ?
                    OpenNettyCommands.Lighting.Off :
                    OpenNettyCommands.Lighting.On,
                address          : endpoint.Address,
                medium           : endpoint.Medium,
                mode             : endpoint.Protocol is OpenNettyProtocol.Nitoo ? OpenNettyMode.Unicast : null,
                gateway          : endpoint.Gateway,
                options          : endpoint.Protocol is OpenNettyProtocol.Nitoo &&
                    endpoint.GetBooleanSetting(OpenNettySettings.ActionValidation) is not false ?
                    OpenNettyTransmissionOptions.RequireActionValidation :
                    OpenNettyTransmissionOptions.None,
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
                options          : OpenNettyTransmissionOptions.None,
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
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0076));
        }

        return _service.ExecuteCommandAsync(
            protocol         : endpoint.Protocol,
            command          : OpenNettyCommands.Scenario.UnbindingRequest,
            address          : endpoint.Address,
            medium           : endpoint.Medium,
            mode             : null,
            gateway          : endpoint.Gateway,
            options          : OpenNettyTransmissionOptions.None,
            cancellationToken: cancellationToken);
    }
}
