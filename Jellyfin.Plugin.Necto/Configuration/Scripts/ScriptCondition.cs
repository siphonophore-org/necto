using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.Scripts;

/// <summary>
/// One test a script applies to an event before running.
/// </summary>
public class ScriptCondition
{
    /// <summary>Gets or sets the event value compared.</summary>
    public ConditionField Field { get; set; }

    /// <summary>Gets or sets how the value is compared.</summary>
    public ConditionOperator Operator { get; set; }

    /// <summary>Gets or sets the values compared against. One matching is enough for a positive operator.</summary>
    public List<string> Values { get; set; } = new();
}
