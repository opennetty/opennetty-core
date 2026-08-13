/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
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
    public static OpenNettyDeviceDefinition GetDeviceDefinitionByModel(OpenNettyBrand brand, string model)
        => TryGetDeviceDefinitionByModel(brand, model, out OpenNettyDeviceDefinition? definition)
            ? definition
            : throw new InvalidOperationException(SR.FormatID0085(brand, model));

    /// <summary>
    /// Resolves the device definition corresponding to the specified brand and model.
    /// </summary>
    /// <param name="brand">The device brand.</param>
    /// <param name="model">The device model.</param>
    /// <param name="definition">The device definition.</param>
    /// <returns>
    /// <see langword="true"/> if the device definition corresponding to the
    /// specified brand and model was found; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentException">The model is null or empty or the brand is not valid.</exception>
    public static bool TryGetDeviceDefinitionByModel(OpenNettyBrand brand,
        string model, [NotNullWhen(true)] out OpenNettyDeviceDefinition? definition)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);

        if (!Enum.IsDefined(brand))
        {
            throw new InvalidEnumArgumentException(nameof(brand), (int) brand, typeof(OpenNettyBrand));
        }

        foreach (var device in _devices.Value)
        {
            foreach (var identity in device.Identities)
            {
                if (identity.Brand == brand && string.Equals(identity.Model, model, StringComparison.OrdinalIgnoreCase))
                {
                    definition = device;
                    return true;
                }
            }
        }

        definition = null;
        return false;
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
        List<OpenNettyDeviceIdentity> identities = [];
        Dictionary<OpenNettySetting, string> settings = [];
        List<OpenNettyUnitDefinition> units = [];

        foreach (var identity in node.Elements("Identity"))
        {
            Dictionary<CultureInfo, string> descriptions = [];

            foreach (var description in identity.Elements("Description"))
            {
                var culture = CultureInfo.GetCultureInfo((string?) description.Attribute("Culture")
                    ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066)));
                descriptions.Add(culture, (string?) description.Attribute("Value")
                    ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066)));
            }

            identities.Add(new OpenNettyDeviceIdentity
            {
                Brand = Enum.Parse<OpenNettyBrand>((string?) identity.Attribute("Brand")
                    ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066))),
                Collection = (string?) identity.Attribute("Collection"),
                Descriptions = descriptions.ToImmutableDictionary(),
                Model = (string?) identity.Attribute("Model")
                    ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066))
            });
        }

        foreach (var setting in node.Elements("Setting"))
        {
            settings.Add(new OpenNettySetting(
                (string?) setting.Attribute("Name")  ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066))),
                (string?) setting.Attribute("Value") ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066)));
        }

        var definition = new OpenNettyDeviceDefinition
        {
            Id = (Guid?) node.Attribute("Id") ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066)),
            Identities = [.. identities],
            Medium = Enum.Parse<OpenNettyMedium>((string?) node.Attribute("Medium")
                ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066))),
            Protocol = Enum.Parse<OpenNettyProtocol>((string?) node.Attribute("Protocol")
                ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066))),
            Series = (string?) node.Attribute("Series")
                ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066)),
            Settings = settings.ToImmutableDictionary()
        };

        foreach (var unit in node.Elements("Unit"))
        {
            units.Add(CreateUnitDefinition(definition, unit));
        }

        if (units.Count is not >= 1)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0066));
        }

        definition.Units = [.. units];

        // Mark the device as read-only to prevent further modifications.
        definition.MakeReadOnly();

        return definition;
    }

    private static OpenNettyUnitDefinition CreateUnitDefinition(OpenNettyDeviceDefinition device, XElement node)
    {
        HashSet<OpenNettyCapability> capabilities = [];
        Dictionary<CultureInfo, string> descriptions = [];
        Dictionary<OpenNettySetting, string> settings = [];

        foreach (var capability in node.Elements("Capability"))
        {
            capabilities.Add(new OpenNettyCapability((string?) capability.Attribute("Name")
                ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066))));
        }

        foreach (var description in node.Elements("Description"))
        {
            var culture = CultureInfo.GetCultureInfo((string?) description.Attribute("Culture")
                ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066)));
            descriptions[culture] = (string?) description.Attribute("Value")
                ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066));
        }

        foreach (var setting in node.Elements("Setting"))
        {
            settings.Add(new OpenNettySetting(
                (string?) setting.Attribute("Name")  ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066))),
                (string?) setting.Attribute("Value") ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066)));
        }

        var definition = new OpenNettyUnitDefinition
        {
            AssociatedUnitId = (byte?) (uint?) node.Attribute("AssociatedUnitId"),
            Capabilities = [.. capabilities],
            Descriptions = descriptions.ToImmutableDictionary(),
            Device = device,
            Id = (byte?) (uint?) node.Attribute("Id") ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0066)),
            Settings = settings.ToImmutableDictionary()
        };

        // Mark the device as read-only to prevent further modifications.
        definition.MakeReadOnly();

        return definition;
    }
}
