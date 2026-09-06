using System.Reflection;
using System.Text;

namespace WoWBuddy.Core.Offsets;

/// <summary>One catalogued offset and everything known about where it came from.</summary>
/// <param name="Path">Dotted path, for example <c>ObjectManager.CurMgr</c>.</param>
/// <param name="Value">The value, rendered as hex where it is numeric.</param>
/// <param name="Confidence">How far it can be trusted.</param>
/// <param name="Source">Where it came from.</param>
/// <param name="HowToVerify">How to independently re-derive or check it.</param>
/// <param name="Conflicts">Conflicting values in other sources, if any.</param>
public readonly record struct CataloguedOffset(
    string Path,
    string Value,
    OffsetConfidence Confidence,
    string Source,
    string HowToVerify,
    string Conflicts);

/// <summary>
/// Enumerates the offset tables and their provenance.
/// </summary>
/// <remarks>
/// <para>
/// Reflection rather than a hand-maintained second list, so the documentation and the values
/// the bot actually uses cannot drift apart. A unit test walks this to assert that every
/// constant carries provenance, which is what stops an undocumented value from ever being
/// added to the table.
/// </para>
/// </remarks>
public static class OffsetCatalogue
{
    /// <summary>The types whose constants are catalogued.</summary>
    public static IReadOnlyList<Type> Tables { get; } = [typeof(Offsets335a), typeof(UpdateFields335a)];

    /// <summary>
    /// Every constant in the offset tables, in declaration order, with its provenance.
    /// </summary>
    /// <remarks>
    /// Fields without an <see cref="OffsetInfoAttribute"/> are still returned, with
    /// <see cref="OffsetConfidence.Unverified"/> and an empty source. That is what lets the
    /// guard test detect them rather than silently skipping them.
    /// </remarks>
    public static IReadOnlyList<CataloguedOffset> Enumerate()
    {
        var results = new List<CataloguedOffset>();

        foreach (Type table in Tables)
        {
            Collect(table, table.Name, results);
        }

        return results;
    }

    private static void Collect(Type type, string prefix, List<CataloguedOffset> results)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            // Only numeric constants and the readonly candidate arrays describe the client.
            // Anything else on these types is a helper and is not a claim about memory.
            if (!IsCataloguable(field))
            {
                continue;
            }

            OffsetInfoAttribute? info = field.GetCustomAttribute<OffsetInfoAttribute>();
            results.Add(new CataloguedOffset(
                Path: $"{prefix}.{field.Name}",
                Value: FormatValue(field),
                Confidence: info?.Confidence ?? OffsetConfidence.Unverified,
                Source: info?.Source ?? string.Empty,
                HowToVerify: info?.HowToVerify ?? string.Empty,
                Conflicts: info?.Conflicts ?? string.Empty));
        }

        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public))
        {
            // Record structs declared alongside the tables describe a shape, not an address.
            if (nested.IsValueType && !nested.IsEnum)
            {
                continue;
            }

            Collect(nested, $"{prefix}.{nested.Name}", results);
        }
    }

    private static bool IsCataloguable(FieldInfo field)
    {
        if (field.IsLiteral)
        {
            return field.FieldType == typeof(uint)
                || field.FieldType == typeof(int)
                || field.FieldType == typeof(ulong);
        }

        // The position-layout candidate array is a claim about memory too.
        return field.IsInitOnly && field.FieldType.IsArray;
    }

    private static string FormatValue(FieldInfo field)
    {
        object? raw = field.GetValue(null);

        return raw switch
        {
            // Offsets are declared as uint and read best in hex; the handful of int
            // constants are counts and build numbers, which read best in decimal.
            uint u => $"0x{u:X}",
            int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ulong ul => $"0x{ul:X}",
            Offsets335a.PositionLayout[] layouts => string.Join(" | ", layouts.Select(l => l.ToString())),
            null => "(null)",
            _ => raw.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    /// Renders the catalogue as the Markdown table that lives in <c>docs/offsets.md</c>.
    /// </summary>
    /// <remarks>
    /// Generated rather than written by hand so the document cannot describe values the code
    /// no longer holds. The inspector tool can emit it on demand.
    /// </remarks>
    public static string ToMarkdown()
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Offset | Value | Confidence | Source | How to verify |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");

        foreach (CataloguedOffset entry in Enumerate())
        {
            string source = string.IsNullOrWhiteSpace(entry.Conflicts)
                ? entry.Source
                : $"{entry.Source} **Conflicts:** {entry.Conflicts}";

            builder
                .Append("| `").Append(entry.Path).Append("` ")
                .Append("| `").Append(entry.Value).Append("` ")
                .Append("| ").Append(entry.Confidence).Append(' ')
                .Append("| ").Append(Escape(source)).Append(' ')
                .Append("| ").Append(Escape(entry.HowToVerify)).AppendLine(" |");
        }

        return builder.ToString();
    }

    /// <summary>Every catalogued offset that is not yet backed by a source.</summary>
    public static IReadOnlyList<CataloguedOffset> Undocumented() =>
        Enumerate().Where(e => string.IsNullOrWhiteSpace(e.Source)).ToList();

    /// <summary>Every catalogued offset that still needs first-hand verification.</summary>
    public static IReadOnlyList<CataloguedOffset> NeedingVerification() =>
        Enumerate()
            .Where(e => e.Confidence is OffsetConfidence.Unverified or OffsetConfidence.Conflicted)
            .ToList();

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
