/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;

namespace OpenNetty;

/// <summary>
/// Exposes static methods allowing to resolve device or unit definitions
/// of Legrand and BTicino products supported by the OpenNetty library.
/// </summary>
public static class OpenNettyDevices
{
    private readonly static Lazy<ImmutableArray<OpenNettyDeviceDefinition>> _devices = new(CreateDeviceDefinitions);

    /// <summary>
    /// Resolves the device definition corresponding to the specified brand and model.
    /// </summary>
    /// <param name="brand">The device brand.</param>
    /// <param name="model">The device model.</param>
    /// <returns>
    /// The device definition corresponding to the specified brand and model or
    /// <see langword="null"/> if the device definition couldn't be found in the database.
    /// </returns>
    /// <exception cref="ArgumentException">The model is null or empty or the brand is not valid.</exception>
    public static OpenNettyDeviceDefinition? GetDeviceByModel(OpenNettyBrand brand, string model)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);

        if (!Enum.IsDefined(brand))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0006), nameof(brand));
        }

        foreach (var device in _devices.Value)
        {
            foreach (var identity in device.Identities)
            {
                if (identity.Brand == brand && string.Equals(identity.Model, model, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the unit definition corresponding to the specified brand, model and unit identifier.
    /// </summary>
    /// <param name="brand">The device brand.</param>
    /// <param name="model">The device model.</param>
    /// <param name="id">The unit identifier.</param>
    /// <returns>
    /// The unit definition corresponding to the specified brand, model and unit identifier or
    /// <see langword="null"/> if the unit definition couldn't be found in the database.
    /// </returns>
    /// <exception cref="ArgumentException">The model is null or empty or the brand is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The unit identifier is out of range.</exception>
    public static OpenNettyUnitDefinition? GetUnitByModel(OpenNettyBrand brand, string model, byte id)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);
        ArgumentOutOfRangeException.ThrowIfZero(id);

        if (!Enum.IsDefined(brand))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0006), nameof(brand));
        }

        foreach (var device in _devices.Value)
        {
            foreach (var identity in device.Identities)
            {
                if (identity.Brand == brand && string.Equals(identity.Model, model, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var unit in device.Units)
                    {
                        if (unit.Id == id)
                        {
                            return unit;
                        }
                    }
                }
            }
        }

        return null;
    }

    private static ImmutableArray<OpenNettyDeviceDefinition> CreateDeviceDefinitions()
    {
        using var stream = Assembly.GetAssembly(typeof(OpenNettyDevices))?.GetManifestResourceStream(
            "OpenNetty.OpenNettyDevices.xml") ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066));

        var document = XDocument.Load(stream);
        if (document.Root is null)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0066));
        }

        var builder = ImmutableArray.CreateBuilder<OpenNettyDeviceDefinition>();

        foreach (var device in document.Root.Elements("Device"))
        {
            builder.Add(CreateDeviceDefinition(device));
        }

        return builder.ToImmutable();
    }

    private static OpenNettyDeviceDefinition CreateDeviceDefinition(XElement node)
    {
        HashSet<OpenNettyCapability> capabilities = [];
        List<OpenNettyDeviceIdentity> identities = [];
        Dictionary<OpenNettySetting, string> settings = [];
        List<OpenNettyUnitDefinition> units = [];

        foreach (var capability in node.Elements("Capability"))
        {
            capabilities.Add(new OpenNettyCapability((string) capability.Attribute("Name")!));
        }

        foreach (var identity in node.Elements("Identity"))
        {
            Dictionary<CultureInfo, string> descriptions = [];

            foreach (var description in identity.Elements("Description"))
            {
                var culture = CultureInfo.GetCultureInfo((string) description.Attribute("Culture")!);
                descriptions.Add(culture, (string) description.Attribute("Value")!);
            }

            identities.Add(new OpenNettyDeviceIdentity
            {
                Brand = Enum.Parse<OpenNettyBrand>((string) identity.Attribute("Brand")!),
                Collection = (string?) identity.Attribute("Collection"),
                Descriptions = descriptions.ToImmutableDictionary(),
                Model = (string) identity.Attribute("Model")!
            });
        }

        foreach (var setting in node.Elements("Setting"))
        {
            settings.Add(new OpenNettySetting((string) setting.Attribute("Name")!), (string) setting.Attribute("Value")!);
        }

        foreach (var unit in node.Elements("Unit"))
        {
            units.Add(CreateUnitDefinition(unit));
        }

        return new OpenNettyDeviceDefinition
        {
            Capabilities = [.. capabilities],
            Identities = [.. identities],
            Medium = Enum.Parse<OpenNettyMedium>((string) node.Attribute("Medium")!),
            Protocol = Enum.Parse<OpenNettyProtocol>((string) node.Attribute("Protocol")!),
            Series = (string) node.Attribute("Series")!,
            Settings = settings.ToImmutableDictionary(),
            Units = [.. units]
        };
    }

    private static OpenNettyUnitDefinition CreateUnitDefinition(XElement node)
    {
        HashSet<OpenNettyCapability> capabilities = [];
        Dictionary<CultureInfo, string> descriptions = [];
        Dictionary<OpenNettySetting, string> settings = [];

        foreach (var capability in node.Elements("Capability"))
        {
            capabilities.Add(new OpenNettyCapability((string) capability.Attribute("Name")!));
        }

        foreach (var description in node.Elements("Description"))
        {
            var culture = CultureInfo.GetCultureInfo((string) description.Attribute("Culture")!);
            descriptions[culture] = (string) description.Attribute("Value")!;
        }

        foreach (var setting in node.Elements("Setting"))
        {
            settings.Add(new OpenNettySetting((string) setting.Attribute("Name")!), (string) setting.Attribute("Value")!);
        }

        return new OpenNettyUnitDefinition
        {
            AssociatedUnitId = (byte?) (uint?) node.Attribute("AssociatedUnitId"),
            Capabilities = [.. capabilities],
            Descriptions = descriptions.ToImmutableDictionary(),
            Id = (byte) (uint) node.Attribute("Id")!,
            Settings = settings.ToImmutableDictionary()
        };
    }
}
