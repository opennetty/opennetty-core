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
            if (device.Definition.Protocol is not (OpenNettyProtocol.Nitoo or OpenNettyProtocol.Zigbee) ||
                string.IsNullOrEmpty(device.SerialNumber))
            {
                continue;
            }

            // If an endpoint targeting the device was configured by the user, do not override it.
            if (!options.Endpoints.Exists(endpoint => endpoint.Device == device && endpoint.Unit is null))
            {
                options.Endpoints.Add(new OpenNettyEndpoint
                {
                    Address = device.Definition.Protocol switch
                    {
                        OpenNettyProtocol.Nitoo => OpenNettyAddress.FromNitooAddress(
                            identifier: uint.Parse(device.SerialNumber ?? throw new InvalidOperationException(SR.FormatID0089("SerialNumber")), CultureInfo.InvariantCulture),
                            unit      : 0),

                        OpenNettyProtocol.Zigbee => OpenNettyAddress.FromHexadecimalZigbeeAddress(
                            identifier: device.SerialNumber ?? throw new InvalidOperationException(SR.FormatID0094("SerialNumber")),
                            unit      : 0),

                        _ => null
                    },
                    Capabilities = [],
                    Device = device,
                    Gateway = null,
                    Medium = device.Definition.Medium,
                    Name = ComputeDefaultEndpointName(device),
                    Protocol = device.Definition.Protocol,
                    Settings = ImmutableDictionary.Create<OpenNettySetting, string>()
                });

                static string ComputeDefaultEndpointName(OpenNettyDevice device)
                    => new StringBuilder(Enum.GetName(device.Definition.Protocol))
                        .Append('/')
                        .Append(device.SerialNumber)
                        .ToString();
            }

            // For Nitoo and Zigbee devices, add implicit endpoints for all the units present
            // in the device definition but that have not been explicitly added by the user.
            if (device.Definition.Units.Length is not 0)
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
                            OpenNettyProtocol.Nitoo => OpenNettyAddress.FromNitooAddress(
                                identifier: uint.Parse(device.SerialNumber ?? throw new InvalidOperationException(SR.FormatID0089("SerialNumber")), CultureInfo.InvariantCulture),
                                unit      : definition.Id),

                            OpenNettyProtocol.Zigbee => OpenNettyAddress.FromHexadecimalZigbeeAddress(
                                identifier: device.SerialNumber ?? throw new InvalidOperationException(SR.FormatID0094("SerialNumber")),
                                unit      : definition.Id),

                            _ => null
                        },
                        Capabilities = [],
                        Device = device,
                        Gateway = null,
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
                            .Append(device.SerialNumber)
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

        foreach (var device in options.Devices)
        {
            if (!string.IsNullOrEmpty(device.SerialNumber) && device.SerialNumber.Any(static character =>
                character is not ((>= '0' and <= '9') or
                                  (>= 'a' and <= 'f') or
                                  (>= 'A' and <= 'F'))))
            {
                return ValidateOptionsResult.Fail(SR.FormatID2006(device.SerialNumber));
            }
        }

        foreach (var endpoint in options.Endpoints)
        {
            if (!string.IsNullOrEmpty(endpoint.Name) &&
                (endpoint.Name.Contains('+', StringComparison.OrdinalIgnoreCase) ||
                 endpoint.Name.Contains('*', StringComparison.OrdinalIgnoreCase)))
            {
                return ValidateOptionsResult.Fail(SR.FormatID2000(endpoint.Name));
            }

            switch (endpoint.GetStringSetting(OpenNettySettings.ActuatorType))
            {
                // If the endpoint supports both lighting and automation commands,
                // require that the actuator type be configured via the dedicated setting.
                case null or { Length: 0 } when SupportsLightControl(endpoint) && SupportsShutterControl(endpoint):
                    return ValidateOptionsResult.Fail(SR.FormatID2002(endpoint.Name));

                case string type when type is not (
                    OpenNettySettings.ActuatorTypes.Automation or
                    OpenNettySettings.ActuatorTypes.Lighting):
                    return ValidateOptionsResult.Fail(SR.FormatID2003(endpoint.Name, type));
            }

            switch (endpoint.GetStringSetting(OpenNettySettings.SwitchMode))
            {
                case null or { Length: 0 }: break;

                case string mode when mode is not (
                    OpenNettySettings.SwitchModes.Default or
                    OpenNettySettings.SwitchModes.PushButton):
                    return ValidateOptionsResult.Fail(SR.FormatID2004(endpoint.Name, mode));
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
