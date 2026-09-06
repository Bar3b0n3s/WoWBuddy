namespace WoWBuddy.Core.Memory;

/// <summary>
/// Thrown when the bot cannot establish or keep access to the game process.
/// </summary>
/// <remarks>
/// Reserved for setup failures that the user can act on (wrong privileges, client exited).
/// Ordinary failed reads during normal operation do not throw; they return false.
/// </remarks>
public sealed class MemoryAccessException : Exception
{
    public MemoryAccessException(string message)
        : base(message)
    {
    }

    public MemoryAccessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
