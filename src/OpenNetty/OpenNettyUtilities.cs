using System.ComponentModel;
using System.Text;

namespace OpenNetty;

/// <summary>
/// Exposes common utilities used by the OpenNetty components.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class OpenNettyUtilities
{
    /// <summary>
    /// Computes the default endpoint name for a given combination of protocol, address, device and unit.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="address">The address.</param>
    /// <param name="unit">The device unit.</param>
    /// <returns>The default endpoint name.</returns>
    public static string ComputeDefaultEndpointName(
        OpenNettyProtocol protocol, OpenNettyAddress? address, OpenNettyUnit? unit)
    {
        var builder = new StringBuilder(protocol switch
        {
            OpenNettyProtocol.Nitoo  => "Nitoo",
            OpenNettyProtocol.Scs    => "SCS",
            OpenNettyProtocol.Zigbee => "Zigbee",

            _ => throw new InvalidEnumArgumentException(nameof(protocol), (int) protocol, typeof(OpenNettyProtocol))
        });

        builder.Append('/');

        switch (protocol)
        {
            case OpenNettyProtocol.Scs when address?.Type is OpenNettyAddressType.ScsLightPoint:
                var (extension, general, group, area, point) = OpenNettyAddress.ToScsLightPointAddress(address.Value);

                if (OpenNettyAddress.IsScsLightPointAreaAddress(address.Value))
                {
                    builder.Append("PL area");
                    builder.Append('/');
                    builder.Append(extension);
                    builder.Append('/');
                    builder.Append(area);
                }

                else if (OpenNettyAddress.IsScsLightPointGeneralAddress(address.Value))
                {
                    builder.Append("PL general");
                    builder.Append('/');
                    builder.Append(extension);
                }

                else if (OpenNettyAddress.IsScsLightPointGroupAddress(address.Value))
                {
                    builder.Append("PL group");
                    builder.Append('/');
                    builder.Append(extension);
                    builder.Append('/');
                    builder.Append(group);
                }

                else if (OpenNettyAddress.IsScsLightPointPointToPointAddress(address.Value))
                {
                    builder.Append("PL point-to-point");
                    builder.Append('/');
                    builder.Append(extension);
                    builder.Append('/');
                    builder.Append(area);
                    builder.Append('/');
                    builder.Append(point);
                }

                builder.Append(address.Value.ToString());
                break;

            case OpenNettyProtocol.Scs when address?.Type is OpenNettyAddressType.ScsScenarioPlus:
                builder.Append("PL scenario plus");
                builder.Append('/');
                builder.Append(OpenNettyAddress.ToScsScenarioPlusAddress(address.Value));
                break;

            case OpenNettyProtocol.Nitoo or OpenNettyProtocol.Scs or OpenNettyProtocol.Zigbee:
                if (unit?.Device?.Identifier is OpenNettyDeviceIdentifier identifier)
                {
                    builder.Append(identifier.ToString());
                    builder.Append('/');
                    builder.Append(unit?.Definition.Id ?? 0);
                }

                else if (!string.IsNullOrEmpty(unit?.Device?.Name))
                {
                    builder.Append(unit.Device.Name);
                    builder.Append('/');
                    builder.Append(unit?.Definition.Id ?? 0);
                }

                else
                {
                    throw new InvalidOperationException(SR.GetResourceString(SR.ID0104));
                }
                break;

        }

        return builder.ToString();
    }

    /// <summary>
    /// Converts the specified <see cref="OpenNettyBrand"/> value to its corresponding brand name string.
    /// </summary>
    /// <param name="brand">The brand value.</param>
    /// <returns>The corresponding brand name string.</returns>
    /// <exception cref="InvalidEnumArgumentException">The specified brand value is not valid.</exception>
    public static string GetBrandName(OpenNettyBrand brand) => brand switch
    {
        OpenNettyBrand.BTicino => "BTicino",
        OpenNettyBrand.Legrand => "Legrand",

        _ => throw new InvalidEnumArgumentException(nameof(brand), (int) brand, typeof(OpenNettyBrand))
    };
}
