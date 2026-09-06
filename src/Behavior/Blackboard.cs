namespace WoWBuddy.Behavior;

/// <summary>
/// Loosely typed state shared across a tree.
/// </summary>
/// <remarks>
/// <para>
/// Most of what a tree needs is on its context object, where the compiler can check it. This
/// is for the rest: state that a bot base, a combat routine and a plugin all need to see but
/// that none of them owns, and that cannot be baked into a context type shared by parts of
/// the codebase which do not know about each other.
/// </para>
/// <para>
/// Keep it small. Every value here is one the compiler cannot check, and a bot whose state
/// lives mostly in string-keyed slots is one where a typo silently changes behaviour.
/// </para>
/// </remarks>
public sealed class Blackboard
{
    private readonly Dictionary<string, object?> _values = [];

    /// <summary>Stores a value.</summary>
    public void Set<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _values[key] = value;
    }

    /// <summary>Reads a value, or the default when it is absent or of another type.</summary>
    public T? Get<T>(string key, T? fallback = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _values.TryGetValue(key, out object? value) && value is T typed ? typed : fallback;
    }

    /// <summary>True when a value of the expected type is present.</summary>
    public bool Has<T>(string key) => _values.TryGetValue(key, out object? value) && value is T;

    /// <summary>Removes a value.</summary>
    public bool Remove(string key) => _values.Remove(key);

    /// <summary>Removes everything.</summary>
    public void Clear() => _values.Clear();

    /// <summary>The keys currently set, for the dev tools.</summary>
    public IReadOnlyCollection<string> Keys => _values.Keys;
}
