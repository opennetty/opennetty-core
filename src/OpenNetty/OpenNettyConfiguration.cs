/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using Microsoft.Extensions.Options;

namespace OpenNetty;

/// <summary>
/// Contains the methods required to ensure that the OpenNetty configuration is valid.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class OpenNettyConfiguration : IValidateOptions<OpenNettyOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, OpenNettyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

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

                case string type when type is not (
                    OpenNettySettings.SwitchModes.Default or
                    OpenNettySettings.SwitchModes.PushButton):
                    return ValidateOptionsResult.Fail(SR.FormatID2004(endpoint.Name, type));
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
