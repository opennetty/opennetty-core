/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Reflection;
using System.Xml.Linq;

namespace OpenNetty;

/// <summary>
/// Exposes static methods allowing to resolve device or unit definitions
/// of Legrand and BTicino products supported by the OpenNetty library.
/// </summary>
public static class OpenNettyDevices
{
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

        using var stream = Assembly.GetAssembly(typeof(OpenNettyDevices))?.GetManifestResourceStream(
            "OpenNetty.OpenNettyDevices.xml") ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0073));

        var document = XDocument.Load(stream);
        if (document.Root is null)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0073));
        }

        foreach (var device in document.Root.Elements("Device"))
        {
            foreach (var identity in device.Elements("Identity"))
            {
                if ((string) identity.Attribute("Brand")! != Enum.GetName(brand) ||
                    !string.Equals((string) identity.Attribute("Model")!, model, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return CreateDeviceDefinition(device);
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
    public static OpenNettyUnitDefinition? GetUnitByModel(OpenNettyBrand brand, string model, ushort id)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1u);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(id, 15u);

        if (!Enum.IsDefined(brand))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0006), nameof(brand));
        }

        using var stream = Assembly.GetAssembly(typeof(OpenNettyDevices))?.GetManifestResourceStream(
            "OpenNetty.OpenNettyDevices.xml") ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0073));

        var document = XDocument.Load(stream);
        if (document.Root is null)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0073));
        }

        foreach (var device in document.Root.Elements("Device"))
        {
            foreach (var identity in device.Elements("Identity"))
            {
                if ((string) identity.Attribute("Brand")! != Enum.GetName(brand) ||
                    !string.Equals((string) identity.Attribute("Model")!, model, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var unit in device.Elements("Unit"))
                {
                    if ((uint) unit.Attribute("Id")! != id)
                    {
                        continue;
                    }

                    return CreateUnitDefinition(unit);
                }
            }
        }

        return null;
    }

    private static OpenNettyDeviceDefinition CreateDeviceDefinition(XElement node)
    {
        HashSet<OpenNettyCapability> capabilities = [];
        List<OpenNettyIdentity> identities = [];
        Dictionary<OpenNettySetting, string> settings = [];
        List<OpenNettyUnitDefinition> units = [];

        foreach (var capability in node.Elements("Capability"))
        {
            capabilities.Add(new OpenNettyCapability((string) capability.Attribute("Name")!));
        }

        foreach (var identity in node.Elements("Identity"))
        {
            identities.Add(new OpenNettyIdentity
            {
                Brand = Enum.Parse<OpenNettyBrand>((string) identity.Attribute("Brand")!),
                Collection = (string?) identity.Attribute("Collection"),
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
            Media = Enum.Parse<OpenNettyMedia>((string) node.Attribute("Media")!),
            Protocol = Enum.Parse<OpenNettyProtocol>((string) node.Attribute("Protocol")!),
            Series = (string) node.Attribute("Series")!,
            Settings = settings.ToImmutableDictionary(),
            Units = [.. units]
        };
    }

    private static OpenNettyUnitDefinition CreateUnitDefinition(XElement node)
    {
        HashSet<OpenNettyCapability> capabilities = [];
        Dictionary<OpenNettySetting, string> settings = [];

        foreach (var capability in node.Elements("Capability"))
        {
            capabilities.Add(new OpenNettyCapability((string) capability.Attribute("Name")!));
        }

        foreach (var setting in node.Elements("Setting"))
        {
            settings.Add(new OpenNettySetting((string) setting.Attribute("Name")!), (string) setting.Attribute("Value")!);
        }

        return new OpenNettyUnitDefinition
        {
            AssociatedUnitId = (ushort?) (uint?) node.Attribute("AssociatedUnitId"),
            Capabilities = [.. capabilities],
            Id = (ushort) (uint) node.Attribute("Id")!,
            Settings = settings.ToImmutableDictionary()
        };
    }
}
