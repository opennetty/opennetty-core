/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

namespace OpenNetty;

/// <summary>
/// Contains the methods required to ensure that the OpenNetty configuration is valid.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class OpenNettyConfiguration : IPostConfigureOptions<OpenNettyOptions>, IValidateOptions<OpenNettyOptions>
{
    public void PostConfigure(string? name, OpenNettyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var device in options.Devices)
        {
            // If an endpoint targeting the device was configured by the user, do not override it.
            if (!options.Endpoints.Exists(endpoint => endpoint.Device == device && endpoint.Unit is null))
            {
                options.Endpoints.Add(new OpenNettyEndpoint
                {
                    Address = device.Definition.Protocol switch
                    {
                        OpenNettyProtocol.Nitoo  => OpenNettyAddress.FromNitooAddress(device.Identifier,  unit: 0),
                        OpenNettyProtocol.Zigbee => OpenNettyAddress.FromZigbeeAddress(device.Identifier, unit: 0),

                        _ => null
                    },
                    Capabilities = [],
                    Device = device,
                    Gateway = device.Gateway ?? options.Gateways.FirstOrDefault(gateway => gateway.Device == device)
                        ?? throw new InvalidOperationException(SR.FormatID0107("Gateway")),
                    Medium = device.Definition.Medium,
                    Name = ComputeDefaultEndpointName(device),
                    Protocol = device.Definition.Protocol,
                    Settings = ImmutableDictionary.Create<OpenNettySetting, string>()
                });

                static string ComputeDefaultEndpointName(OpenNettyDevice device)
                    => new StringBuilder(Enum.GetName(device.Definition.Protocol))
                        .Append('/')
                        .Append(new string(device.Identifier.ToString().Where(char.IsAsciiHexDigit).ToArray()))
                        .ToString();
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

                    options.Endpoints.Add(new OpenNettyEndpoint
                    {
                        Address = device.Definition.Protocol switch
                        {
                            OpenNettyProtocol.Nitoo  => OpenNettyAddress.FromNitooAddress(device.Identifier,  unit: definition.Id),
                            OpenNettyProtocol.Zigbee => OpenNettyAddress.FromZigbeeAddress(device.Identifier, unit: definition.Id),

                            _ => null
                        },
                        Capabilities = [],
                        Device = device,
                        Gateway = device.Gateway ?? options.Gateways.FirstOrDefault(gateway => gateway.Device == device)
                            ?? throw new InvalidOperationException(SR.FormatID0107("Gateway")),
                        Medium = device.Definition.Medium,
                        Name = ComputeDefaultEndpointName(device, definition),
                        Protocol = device.Definition.Protocol,
                        Settings = ImmutableDictionary.Create<OpenNettySetting, string>(),
                        Unit = device.Units.SingleOrDefault(unit => unit.Definition == definition) ?? new OpenNettyUnit
                        {
                            Definition = definition,
                            Scenarios = [],
                            Settings = ImmutableDictionary.Create<OpenNettySetting, string>()
                        }
                    });

                    static string ComputeDefaultEndpointName(OpenNettyDevice device, OpenNettyUnitDefinition unit)
                        => new StringBuilder(Enum.GetName(device.Definition.Protocol))
                            .Append('/')
                            .Append(new string(device.Identifier.ToString().Where(char.IsAsciiHexDigit).ToArray()))
                            .Append('/')
                            .Append(unit.Id)
                            .ToString();
                }
            }
        }
    }

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, OpenNettyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Devices.GroupBy(static device => device.Identifier)
            .Where(static group => group.Count() is > 1)
            .Select(static group => group.Key)
            .OfType<OpenNettyDeviceIdentifier?>()
            .FirstOrDefault() is OpenNettyDeviceIdentifier identifier)
        {
            return ValidateOptionsResult.Fail(SR.FormatID2012(identifier.ToString()));
        }

        if (options.Endpoints.GroupBy(static endpoint => endpoint.Name)
            .Where(static endpoint => endpoint.Count() is > 1)
            .Select(static endpoint => endpoint.Key)
            .OfType<string?>()
            .FirstOrDefault() is string value)
        {
            return ValidateOptionsResult.Fail(SR.FormatID2013(value));
        }

        foreach (var endpoint in options.Endpoints)
        {
            if (!string.IsNullOrEmpty(endpoint.Name) &&
                (endpoint.Name.Contains('+', StringComparison.OrdinalIgnoreCase) ||
                 endpoint.Name.Contains('*', StringComparison.OrdinalIgnoreCase)))
            {
                return ValidateOptionsResult.Fail(SR.FormatID2000(endpoint.Name));
            }

            switch (endpoint.GetBooleanSetting(OpenNettySettings.ActionValidation))
            {
                case null: break;

                case not null:
                    if (endpoint.Protocol is not OpenNettyProtocol.Nitoo)
                    {
                        return ValidateOptionsResult.Fail(SR.GetResourceString(SR.ID2010));
                    }

                    if (endpoint.Address is null)
                    {
                        return ValidateOptionsResult.Fail(SR.GetResourceString(SR.ID2011));
                    }
                    break;
            }

            switch (endpoint.GetStringSetting(OpenNettySettings.FunctionType))
            {
                // If the endpoint supports both lighting and automation commands,
                // require that the function type be configured via the dedicated setting.
                case null or { Length: 0 } when SupportsLightControl(endpoint) && SupportsShutterControl(endpoint):
                    return ValidateOptionsResult.Fail(SR.FormatID2002(endpoint.Name));

                // If the endpoint supports both pressure scenarios and pressure scenarios plus,
                // require that the function type be configured via the dedicated setting.
                case null or { Length: 0 } when
                    endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioEvent) &&
                    endpoint.HasCapability(OpenNettyCapabilities.PressureScenarioPlusEvent):
                    return ValidateOptionsResult.Fail(SR.FormatID2014(endpoint.Name));

                case string type when type is not (
                    OpenNettySettings.FunctionTypes.AutomationActuator or
                    OpenNettySettings.FunctionTypes.LightActuator      or 
                    OpenNettySettings.FunctionTypes.ScheduledScenario  or
                    OpenNettySettings.FunctionTypes.ScheduledScenarioPlus):
                    return ValidateOptionsResult.Fail(SR.FormatID2003(endpoint.Name, type));
            }

            switch (endpoint.GetStringSetting(OpenNettySettings.SwitchMode))
            {
                case string mode when mode is not (
                    OpenNettySettings.SwitchModes.Default or
                    OpenNettySettings.SwitchModes.PushButton):
                    return ValidateOptionsResult.Fail(SR.FormatID2004(endpoint.Name, mode));
            }

            switch (endpoint.GetStringSetting(OpenNettySettings.PushButtonNumbers))
            {
                case null or { Length: 0 } when endpoint.HasCapability(OpenNettyCapabilities.ConfigurablePushButtonNumbers):
                    return ValidateOptionsResult.Fail(SR.FormatID2015(endpoint.Name));

                case string numbers when numbers.Split(',', StringSplitOptions.RemoveEmptyEntries) is not [_, ..] array ||
                    array.Any(number => !byte.TryParse(number, CultureInfo.InvariantCulture, out _)):
                    return ValidateOptionsResult.Fail(SR.FormatID2016(endpoint.Name));
            }

            static bool SupportsLightControl(OpenNettyEndpoint endpoint) =>
                endpoint.HasCapability(OpenNettyCapabilities.OnOffSwitchControl) ||
                endpoint.HasCapability(OpenNettyCapabilities.BasicDimmingControl) ||
                endpoint.HasCapability(OpenNettyCapabilities.AdvancedDimmingControl);

            static bool SupportsShutterControl(OpenNettyEndpoint endpoint) =>
                endpoint.HasCapability(OpenNettyCapabilities.BasicShutterControl) ||
                endpoint.HasCapability(OpenNettyCapabilities.AdvancedShutterControl);
        }

        return ValidateOptionsResult.Success;
    }
}
