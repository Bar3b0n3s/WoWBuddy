using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using WoWBuddy.Common.Geometry;

namespace WoWBuddy.Profiles;

/// <summary>
/// Reads attributes off one element, collecting complaints instead of throwing.
/// </summary>
/// <remarks>
/// A profile with a typo in it should produce a list of everything wrong with it, not the
/// first exception thrown. Every accessor here records an issue and falls back to a default
/// so that the rest of the element still gets read.
/// </remarks>
internal sealed class ProfileXml(XElement element, List<ProfileIssue> issues)
{
    private readonly HashSet<string> _read = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The element being read.</summary>
    public XElement Element { get; } = element;

    /// <summary>Its name.</summary>
    public string Name => Element.Name.LocalName;

    /// <summary>The line it was written on, or 0 when the document carried no line info.</summary>
    public int Line => Element is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;

    /// <summary>Records a problem with this element.</summary>
    public void Report(ProfileIssueSeverity severity, string message) =>
        issues.Add(new ProfileIssue(severity, message, Line, Name));

    /// <summary>Records a problem that stops the profile running.</summary>
    public void Error(string message) => Report(ProfileIssueSeverity.Error, message);

    /// <summary>Records a problem worth mentioning.</summary>
    public void Warn(string message) => Report(ProfileIssueSeverity.Warning, message);

    /// <summary>True when the attribute is present.</summary>
    public bool Has(string name)
    {
        _read.Add(name);
        return Find(name) is not null;
    }

    /// <summary>Reads a text attribute.</summary>
    public string Text(string name, string fallback = "")
    {
        _read.Add(name);
        return Find(name)?.Value.Trim() ?? fallback;
    }

    /// <summary>Reads a whole-number attribute, complaining when it is not one.</summary>
    public int Integer(string name, int fallback = 0)
    {
        _read.Add(name);

        XAttribute? attribute = Find(name);
        if (attribute is null)
        {
            return fallback;
        }

        if (int.TryParse(attribute.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        Error($"{name}='{attribute.Value}' is not a whole number.");
        return fallback;
    }

    /// <summary>Reads an id attribute, complaining when it is not one.</summary>
    public uint Id(string name, uint fallback = 0)
    {
        _read.Add(name);

        XAttribute? attribute = Find(name);
        if (attribute is null)
        {
            return fallback;
        }

        if (uint.TryParse(attribute.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value))
        {
            return value;
        }

        Error($"{name}='{attribute.Value}' is not an id.");
        return fallback;
    }

    /// <summary>Reads a decimal attribute, complaining when it is not one.</summary>
    public float Number(string name, float fallback = 0f)
    {
        _read.Add(name);

        XAttribute? attribute = Find(name);
        if (attribute is null)
        {
            return fallback;
        }

        // Invariant culture only: a profile written on a machine using commas for decimal
        // points must still load the same way everywhere.
        if (float.TryParse(attribute.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            && float.IsFinite(value))
        {
            return value;
        }

        Error($"{name}='{attribute.Value}' is not a number.");
        return fallback;
    }

    /// <summary>Reads a true/false attribute, complaining when it is neither.</summary>
    public bool Flag(string name, bool fallback = false)
    {
        _read.Add(name);

        XAttribute? attribute = Find(name);
        if (attribute is null)
        {
            return fallback;
        }

        string text = attribute.Value.Trim();

        if (bool.TryParse(text, out bool value))
        {
            return value;
        }

        if (text is "1")
        {
            return true;
        }

        if (text is "0")
        {
            return false;
        }

        Error($"{name}='{attribute.Value}' is not true or false.");
        return fallback;
    }

    /// <summary>
    /// Reads an X/Y/Z triple.
    /// </summary>
    /// <param name="required">
    /// When true, a missing or implausible position is an error; when false it is silently
    /// left unset, because plenty of steps do not need one.
    /// </param>
    public Vector3 Position(bool required)
    {
        bool present = Has("X") | Has("Y") | Has("Z");

        Vector3 position = new(Number("X"), Number("Y"), Number("Z"));

        if (!present)
        {
            if (required)
            {
                Error("needs a position (X, Y and Z).");
            }

            return Vector3.Zero;
        }

        if (!WorldBounds.IsPlausible(position))
        {
            Error($"has position {position}, which is not somewhere in the world.");
            return Vector3.Zero;
        }

        return position;
    }

    /// <summary>Reads the conditions on this element.</summary>
    public IReadOnlyList<ProfileCondition> Conditions(string name = "Condition")
    {
        string text = Text(name);

        if (text.Length == 0)
        {
            return [];
        }

        if (ProfileConditionParser.TryParseAll(text, out IReadOnlyList<ProfileCondition> conditions, out string error))
        {
            return conditions;
        }

        Error(error);
        return [];
    }

    /// <summary>
    /// Warns about attributes nothing asked for, which are almost always typos.
    /// </summary>
    /// <remarks>
    /// Call this last: it reports whatever the accessors above did not touch.
    /// </remarks>
    public void WarnAboutUnreadAttributes()
    {
        foreach (XAttribute attribute in Element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || _read.Contains(attribute.Name.LocalName))
            {
                continue;
            }

            Warn($"has an attribute '{attribute.Name.LocalName}' that means nothing here; it was ignored.");
        }
    }

    /// <summary>Marks attributes as read without looking at them.</summary>
    public void Ignore(params string[] names)
    {
        foreach (string name in names)
        {
            _read.Add(name);
        }
    }

    /// <summary>Every attribute on the element, for elements that pass them through.</summary>
    public IReadOnlyDictionary<string, string> AllAttributes(params string[] except)
    {
        HashSet<string> skip = new(except, StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string> attributes = new(StringComparer.OrdinalIgnoreCase);

        foreach (XAttribute attribute in Element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || skip.Contains(attribute.Name.LocalName))
            {
                continue;
            }

            attributes[attribute.Name.LocalName] = attribute.Value;
            _read.Add(attribute.Name.LocalName);
        }

        return attributes;
    }

    private XAttribute? Find(string name) =>
        Element.Attributes()
            .FirstOrDefault(attribute =>
                string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
}
