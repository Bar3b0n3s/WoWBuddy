using System.Text;

namespace WoWBuddy.Common.Configuration;

/// <summary>
/// Identifies one character's settings bucket: realm plus character name.
/// </summary>
/// <remarks>
/// Account name is deliberately not part of the key. It is not needed to disambiguate
/// (realm plus name already is unique) and keeping it out means the settings folder
/// layout never leaks an account identifier onto disk in plain text.
/// </remarks>
public readonly record struct CharacterKey(string Realm, string Name)
{
    /// <summary>
    /// A filesystem-safe folder name for this character. Any character that is not a
    /// letter, digit, dash or underscore is replaced, so realm names with punctuation
    /// or non-Latin scripts cannot produce an invalid path.
    /// </summary>
    public string ToFolderName()
    {
        var builder = new StringBuilder(Realm.Length + Name.Length + 1);
        AppendSanitised(builder, Realm);
        builder.Append('_');
        AppendSanitised(builder, Name);
        return builder.ToString();
    }

    private static void AppendSanitised(StringBuilder builder, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            builder.Append("unknown");
            return;
        }

        foreach (char c in value)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-');
        }
    }

    public override string ToString() => $"{Name}-{Realm}";
}
