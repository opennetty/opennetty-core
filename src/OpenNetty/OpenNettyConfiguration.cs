/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace OpenNetty;

/// <summary>
/// Contains the methods required to ensure that the OpenNetty configuration is valid.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class OpenNettyConfiguration : IPostConfigureOptions<OpenNettyOptions>, IValidateOptions<OpenNettyOptions>
{
    /// <inheritdoc/>
    public void PostConfigure(string? name, OpenNettyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var device in options.Devices)
        {
            if (device.Identifier is null && !device.HasCapability(OpenNettyCapabilities.OpenWebNetGateway))
            {
                continue;
            }

            // If an endpoint targeting the device was configured by the user, do not overwrite it.
            if (!options.Endpoints.Exists(endpoint => endpoint.Device == device && endpoint.Unit is null))
            {
                var address = device.Definition.Protocol switch
                {
                    OpenNettyProtocol.Nitoo  when device.HasCapability(OpenNettyCapabilities.OpenWebNetGateway) => null,
                    OpenNettyProtocol.Zigbee when device.HasCapability(OpenNettyCapabilities.OpenWebNetGateway) => null,

                    OpenNettyProtocol.Nitoo  when device.Identifier is OpenNettyDeviceIdentifier identifier
                        => OpenNettyAddress.FromNitooAddress(identifier, unit: 0),

                    OpenNettyProtocol.Zigbee when device.Identifier is OpenNettyDeviceIdentifier identifier
                        => OpenNettyAddress.FromZigbeeAddress(identifier, unit: 0),

                    _ => null as OpenNettyAddress?
                };

                if (address is null && !device.HasCapability(OpenNettyCapabilities.OpenWebNetGateway))
                {
                    continue;
                }

                options.Endpoints.Add(new OpenNettyEndpoint
                {
                    Address = address,
                    Capabilities = [],
                    Device = device,
                    Gateway = device.Gateway ?? options.Gateways.FirstOrDefault(gateway => gateway.Device == device)
                        ?? throw new InvalidOperationException(SR.FormatID0107("Gateway")),
                    Medium = device.Definition.Medium,
                    Name = OpenNettyUtilities.ComputeDefaultEndpointName(device.Definition.Protocol, address, device, unit: null),
                    Protocol = device.Definition.Protocol,
                    Settings = []
                });
            }

            // Add implicit endpoints for all the units that have not been explicitly added by the user.
            if (device.Definition.Units.Length is not 0 && device.Definition.Protocol is not OpenNettyProtocol.Scs)
            {
                foreach (var definition in device.Definition.Units)
                {
                    // If an endpoint targeting the unit was configured by the user, do not override it.
                    if (options.Endpoints.Exists(endpoint => endpoint.Device == device && endpoint.Unit?.Definition == definition))
                    {
                        continue;
                    }

                    var address = device.Definition.Protocol switch
                    {
                        OpenNettyProtocol.Nitoo  when device.HasCapability(OpenNettyCapabilities.OpenWebNetGateway) => null,
                        OpenNettyProtocol.Zigbee when device.HasCapability(OpenNettyCapabilities.OpenWebNetGateway) => null,

                        OpenNettyProtocol.Nitoo  when device.Identifier is OpenNettyDeviceIdentifier identifier
                            => OpenNettyAddress.FromNitooAddress(identifier, unit: definition.Id),

                        OpenNettyProtocol.Zigbee when device.Identifier is OpenNettyDeviceIdentifier identifier
                            => OpenNettyAddress.FromZigbeeAddress(identifier, unit: definition.Id),

                        _ => null as OpenNettyAddress?
                    };

                    if (address is null && !device.HasCapability(OpenNettyCapabilities.OpenWebNetGateway))
                    {
                        continue;
                    }

                    var unit = device.Units.SingleOrDefault(unit => unit.Definition == definition) ?? new OpenNettyUnit
                    {
                        Definition = definition,
                        Scenarios = [],
                        Settings = []
                    };

                    options.Endpoints.Add(new OpenNettyEndpoint
                    {
                        Address = address,
                        Capabilities = [],
                        Device = device,
                        Gateway = device.Gateway ?? options.Gateways.FirstOrDefault(gateway => gateway.Device == device)
                            ?? throw new InvalidOperationException(SR.FormatID0107("Gateway")),
                        Medium = device.Definition.Medium,
                        Name = OpenNettyUtilities.ComputeDefaultEndpointName(device.Definition.Protocol, address, device, unit),
                        Protocol = device.Definition.Protocol,
                        Settings = [],
                        Unit = unit
                    });
                }
            }
        }
    }

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, OpenNettyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = new ValidateOptionsResultBuilder();

        if (options.Gateways.Count is 0)
        {
            builder.AddError(SR.GetResourceString(SR.ID2017));
        }

        switch (options.Devices.GroupBy(static device => device.Identifier)
            .Where(static group => group.Key is not null)
            .Where(static group => group.Count() is > 1)
            .Select(static group => group.Key)
            .OfType<OpenNettyDeviceIdentifier?>()
            .FirstOrDefault())
        {
            case OpenNettyDeviceIdentifier value:
                builder.AddError(SR.FormatID2012(value.ToString()));
                break;
        }

        switch (options.Devices.GroupBy(static device => device.Name)
            .Where(static device => device.Count() is > 1)
            .Select(static device => device.Key)
            .OfType<string?>()
            .FirstOrDefault())
        {
            case string value:
                builder.AddError(SR.FormatID2019(value));
                break;
        }

        switch (options.Endpoints.GroupBy(static endpoint => endpoint.Name)
            .Where(static endpoint => endpoint.Count() is > 1)
            .Select(static endpoint => endpoint.Key)
            .OfType<string?>()
            .FirstOrDefault())
        {
            case string value:
                builder.AddError(SR.FormatID2013(value));
                break;
        }

        foreach (var device in options.Devices)
        {
            switch (device.Name)
            {
                case string value when value.Contains('+', StringComparison.OrdinalIgnoreCase) ||
                                       value.Contains('*', StringComparison.OrdinalIgnoreCase):
                    builder.AddError(SR.FormatID2020(device.Name));
                    break;
            }

            switch (device.GetBooleanSetting(OpenNettySettings.ActionValidation))
            {
                case not null when device.Definition.Protocol is not OpenNettyProtocol.Nitoo:
                    builder.AddError(SR.GetResourceString(SR.ID2010));
                    break;
            }
        }

        foreach (var endpoint in options.Endpoints)
        {
            switch (endpoint.Name)
            {
                case string value when value.Contains('+', StringComparison.OrdinalIgnoreCase) ||
                                       value.Contains('*', StringComparison.OrdinalIgnoreCase):
                    builder.AddError(SR.FormatID2000(endpoint.Name));
                    break;
            }

            switch (endpoint.GetBooleanSetting(OpenNettySettings.ActionValidation))
            {
                case not null when endpoint.Protocol is not OpenNettyProtocol.Nitoo:
                    builder.AddError(SR.GetResourceString(SR.ID2010));
                    break;

                case not null when endpoint.Address is null:
                    builder.AddError(SR.GetResourceString(SR.ID2011));
                    break;
            }

            switch (endpoint.GetStringSetting(OpenNettySettings.FunctionType))
            {
                // If the endpoint supports both lighting and automation commands,
                // require that the function type be configured via the dedicated setting.
                case null or { Length: 0 } when SupportsLightControl(endpoint) && SupportsShutterControl(endpoint):
                    builder.AddError(SR.FormatID2002(endpoint.Name));
                    break;

                // If the endpoint supports both pressure scenarios and pressure scenarios plus,
                // require that the function type be configured via the dedicated setting.
                case null or { Length: 0 } when
                    endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioEvent) &&
                    endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioPlusEvent):
                    builder.AddError(SR.FormatID2014(endpoint.Name));
                    break;

                case string value when value is not (
                    OpenNettySettings.FunctionTypes.AutomationActuator or
                    OpenNettySettings.FunctionTypes.LightActuator      or 
                    OpenNettySettings.FunctionTypes.ScheduledScenario  or
                    OpenNettySettings.FunctionTypes.ScheduledScenarioPlus):
                    builder.AddError(SR.FormatID2003(endpoint.Name, value));
                    break;
            }

            switch (endpoint.GetStringSetting(OpenNettySettings.SwitchMode))
            {
                case string value when value is not (
                    OpenNettySettings.SwitchModes.Default or
                    OpenNettySettings.SwitchModes.PushButton):
                    builder.AddError(SR.FormatID2004(endpoint.Name, value));
                    break;
            }

            switch (endpoint.GetStringSetting(OpenNettySettings.PushButtonNumbers))
            {
                case null or { Length: 0 } when endpoint.HasCapability(OpenNettyCapabilities.ConfigurablePushButtonNumbers):
                    builder.AddError(SR.FormatID2015(endpoint.Name));
                    break;

                case string value when value.Split(',', StringSplitOptions.RemoveEmptyEntries) is not [_, ..] array ||
                    array.Any(number => !byte.TryParse(number, CultureInfo.InvariantCulture, out _)):
                    builder.AddError(SR.FormatID2016(endpoint.Name));
                    break;
            }

            static bool SupportsLightControl(OpenNettyEndpoint endpoint) =>
                endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchControl)  ||
                endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingControl) ||
                endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingControl);

            static bool SupportsShutterControl(OpenNettyEndpoint endpoint) =>
                endpoint.HasCapability(OpenNettyCapabilities.BasicShutterControl) ||
                endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterControl);
        }

        return builder.Build();
    }
}
