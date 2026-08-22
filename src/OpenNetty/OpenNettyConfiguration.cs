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

        foreach (var gateway in options.Gateways)
        {
            // Note: for devices representing gateways, the gateway attached to the device is always the device itself.
            gateway.Device.Gateway = gateway;
        }

        foreach (var device in options.Devices)
        {
            // If no gateway was explicitly configured for the device, find a gateway that matches the device's
            // protocol: if no gateway can be found, the device will be rejected during the validation phase.
            device.Gateway ??= GetFirstGateway(options.Gateways, device.Definition.Protocol);

            // Add implicit endpoints for all the units that have not been explicitly added by the user.
            if (device.Definition.Units is { IsDefaultOrEmpty: false })
            {
                foreach (var definition in device.Definition.Units)
                {
                    // If an endpoint targeting the unit was configured by the user, do not override it.
                    if (options.Endpoints.Exists(endpoint => endpoint.Unit?.Device == device && endpoint.Unit?.Definition == definition))
                    {
                        continue;
                    }

                    var address = device.Definition.Protocol switch
                    {
                        OpenNettyProtocol.Nitoo  when device.GetUnit(0).HasCapability(OpenNettyCapabilities.OpenWebNetGateway) => null,
                        OpenNettyProtocol.Scs    when device.GetUnit(0).HasCapability(OpenNettyCapabilities.OpenWebNetGateway) => null,
                        OpenNettyProtocol.Zigbee when device.GetUnit(0).HasCapability(OpenNettyCapabilities.OpenWebNetGateway) => null,

                        OpenNettyProtocol.Nitoo  when device.Identifier is OpenNettyDeviceIdentifier identifier
                            => OpenNettyAddress.FromNitooAddress(identifier, unit: definition.Id),

                        OpenNettyProtocol.Zigbee when device.Identifier is OpenNettyDeviceIdentifier identifier
                            => OpenNettyAddress.FromZigbeeAddress(identifier, unit: definition.Id),

                        _ => null as OpenNettyAddress?
                    };

                    if (address is null && !device.GetUnit(0).HasCapability(OpenNettyCapabilities.OpenWebNetGateway))
                    {
                        continue;
                    }

                    var endpoint = new OpenNettyEndpoint
                    {
                        Address = address,
                        Gateway = device.Gateway ?? options.Gateways.FirstOrDefault(gateway => gateway.Device == device)
                            ?? throw new InvalidOperationException(SR.FormatID0107("Gateway")),
                        Medium = device.Definition.Medium,
                        Name = OpenNettyUtilities.ComputeDefaultEndpointName(device.Definition.Protocol,
                            address, device.GetUnit(definition.Id)),
                        Protocol = device.Definition.Protocol,
                        Unit = device.GetUnit(definition.Id)
                    };

                    options.Endpoints.Add(endpoint);
                }
            }
        }

        foreach (var endpoint in options.Endpoints)
        {
            // If no gateway was explicitly configured for the endpoint, find a gateway that matches the endpoint's
            // protocol: if no gateway can be found, the endpoint will be rejected during the validation phase.
            endpoint.Gateway ??= endpoint.Unit?.Device.Gateway ?? GetFirstGateway(options.Gateways, endpoint.Protocol);
        }

        static OpenNettyGateway? GetFirstGateway(IReadOnlyList<OpenNettyGateway> gateways, OpenNettyProtocol protocol)
        {
            for (var index = 0; index < gateways.Count; index++)
            {
                var gateway = gateways[index];
                if (gateway.Protocol == protocol)
                {
                    return gateway;
                }
            }

            return null;
        }
    }

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, OpenNettyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = new ValidateOptionsResultBuilder();

        // Mark the devices and all their units as read-only to prevent further modifications.
        foreach (var device in options.Devices)
        {
            device.MakeReadOnly();

            foreach (var unit in device.Units)
            {
                unit.MakeReadOnly();
            }
        }

        // Mark the endpoints as read-only to prevent further modifications.
        foreach (var endpoint in options.Endpoints)
        {
            endpoint.MakeReadOnly();
        }

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

        switch (options.Devices.GroupBy(static device => device.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static device => device.Count() is > 1)
            .Select(static device => device.Key)
            .OfType<string?>()
            .FirstOrDefault())
        {
            case string value:
                builder.AddError(SR.FormatID2019(value));
                break;
        }

        switch (options.Endpoints.GroupBy(static endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
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
            // Ensure a valid gateway device is attached.
            if (device.Gateway is null ||
               !device.Gateway.Device.GetUnit(0).HasCapability(OpenNettyCapabilities.OpenWebNetGateway))
            {
                builder.AddError(SR.FormatID2022(device.Name));
            }

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
            // Ensure a valid gateway device is attached.
            if (endpoint.Gateway is null ||
               !endpoint.Gateway.Device.GetUnit(0).HasCapability(OpenNettyCapabilities.OpenWebNetGateway))
            {
                builder.AddError(SR.FormatID2023(endpoint.Name));
            }

            // Unless it points to a gateway device, require that an address be attached.
            if (endpoint.Address is null && (endpoint.Unit is not OpenNettyUnit unit ||
                !unit.HasCapability(OpenNettyCapabilities.OpenWebNetGateway)))
            {
                builder.AddError(SR.FormatID2024(endpoint.Name));
            }

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
                // If the endpoint supports multiple conflicting types of operations (e.g light
                // and shutter or pressure scenarios and pressure scenarios plus), require that
                // the function type be configured via the dedicated setting.
                case null or { Length: 0 } when endpoint.HasCapability(OpenNettyCapabilities.ConfigurableFunctionType):
                    builder.AddError(SR.FormatID2002(endpoint.Name));
                    break;

                case string value when value is not (
                    OpenNettySettings.FunctionTypes.AutomationActuator or
                    OpenNettySettings.FunctionTypes.ContactState       or
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
                // If the endpoint supports configurable push button numbers, require that the setting be configured.
                case null or { Length: 0 } when
                    endpoint.HasCapability(OpenNettyCapabilities.ConfigurablePushButtonNumbers) &&
                    endpoint.GetStringSetting(OpenNettySettings.FunctionType) is null or
                        OpenNettySettings.FunctionTypes.ScheduledScenario             or
                        OpenNettySettings.FunctionTypes.ScheduledScenarioPlus:
                    builder.AddError(SR.FormatID2015(endpoint.Name));
                    break;

                case string value when value.Split(',', StringSplitOptions.RemoveEmptyEntries) is not [_, ..] values ||
                    values.Any(static number => !byte.TryParse(number, CultureInfo.InvariantCulture, out _)):
                    builder.AddError(SR.FormatID2016(endpoint.Name));
                    break;
            }
        }

        return builder.Build();
    }
}
