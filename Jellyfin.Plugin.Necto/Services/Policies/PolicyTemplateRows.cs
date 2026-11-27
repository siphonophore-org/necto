using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Necto.Configuration.Policies;

namespace Jellyfin.Plugin.Necto.Services.Policies;

/// <summary>
/// The rows of a template. A row is one Manage switch and the values it guards, such as ManageLibraryAccess with
/// EnableAllFolders and EnabledFolders. A row whose values are not named here guards the value of its own name.
/// </summary>
public static class PolicyTemplateRows
{
    private const string Prefix = "Manage";

    private static readonly Dictionary<string, string[]> AccessValues = new(StringComparer.Ordinal)
    {
        ["ManageLibraryAccess"] = new[] { nameof(AccessTemplate.EnableAllFolders), nameof(AccessTemplate.EnabledFolders) },
        ["ManageChannelAccess"] = new[] { nameof(AccessTemplate.EnableAllChannels), nameof(AccessTemplate.EnabledChannels) },
        ["ManageDeviceAccess"] = new[] { nameof(AccessTemplate.EnableAllDevices), nameof(AccessTemplate.EnabledDevices) },
        ["ManageEnableContentDeletion"] = new[] { nameof(AccessTemplate.EnableContentDeletion), nameof(AccessTemplate.EnableContentDeletionFromFolders) },
        ["ManageMaxParentalRating"] = new[] { nameof(AccessTemplate.MaxParentalRating), nameof(AccessTemplate.MaxParentalSubRating) }
    };

    /// <summary>
    /// Lists the rows of a template part, each as its switch and the values it guards.
    /// </summary>
    /// <param name="partType">AccessTemplate or PreferencesTemplate.</param>
    /// <returns>Each row's switch and values.</returns>
    public static IReadOnlyList<(PropertyInfo Switch, PropertyInfo[] Values)> Of(Type partType)
    {
        ArgumentNullException.ThrowIfNull(partType);
        var properties = partType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        return properties
            .Where(p => p.PropertyType == typeof(bool) && p.Name.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(p =>
            {
                var names = AccessValues.TryGetValue(p.Name, out var listed) ? listed : new[] { p.Name[Prefix.Length..] };
                return (p, names.Select(n => partType.GetProperty(n) ?? throw new InvalidOperationException(n)).ToArray());
            })
            .ToList();
    }

    /// <summary>
    /// Builds a template that holds only the rows a template sets, with its preferences mode. A template that is off
    /// sets no row.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <returns>A new template with every row the template sets.</returns>
    public static PolicyTemplate CopySetRows(PolicyTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var on = template.Enabled;
        var copy = new PolicyTemplate { PreferencesMode = on ? template.PreferencesMode : PreferencesMode.Maintain };
        if (on)
        {
            CopyPart(template.Access, copy.Access);
            CopyPart(template.Preferences, copy.Preferences);
        }

        return copy;
    }

    /// <summary>
    /// Works out whether a template sets any row.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <returns><c>true</c> when a row of Access or Preferences is set.</returns>
    public static bool SetsAnything(PolicyTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return template.Enabled && (Sets(template.Access) || Sets(template.Preferences));
    }

    /// <summary>
    /// Turns every row of a template off, so each is left to the member. The values stay.
    /// </summary>
    /// <param name="template">The template, changed in place.</param>
    public static void Clear(PolicyTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        foreach (var part in new object?[] { template.Access, template.Preferences })
        {
            if (part is null)
            {
                continue;
            }

            foreach (var (on, _) in Of(part.GetType()))
            {
                on.SetValue(part, false);
            }
        }
    }

    private static bool Sets(object? part)
        => part is not null && Of(part.GetType()).Any(row => (bool)row.Switch.GetValue(part)!);

    private static void CopyPart(object? source, object target)
    {
        if (source is null)
        {
            return;
        }

        foreach (var (on, values) in Of(target.GetType()))
        {
            if (!(bool)on.GetValue(source)!)
            {
                continue;
            }

            on.SetValue(target, true);
            foreach (var value in values)
            {
                value.SetValue(target, value.GetValue(source));
            }
        }
    }
}
