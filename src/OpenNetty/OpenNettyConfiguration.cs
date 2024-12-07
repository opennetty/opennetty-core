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
                return ValidateOptionsResult.Fail(SR.GetResourceString(SR.ID2000));
            }
        }

        return ValidateOptionsResult.Success;
    }
}
