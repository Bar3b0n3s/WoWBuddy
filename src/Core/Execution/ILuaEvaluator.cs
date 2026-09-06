namespace WoWBuddy.Core.Execution;

/// <summary>
/// Asking the client's own Lua interpreter a question.
/// </summary>
/// <remarks>
/// <para>
/// An interface over <see cref="LuaBridge"/> so that everything built on Lua can be exercised
/// without a game running. That matters more here than usual: a great deal of what the bot
/// needs — the quest log, the party, bag contents, the battleground queue — has no memory
/// offset this project has verified, so it comes through Lua, and the code that decides what to
/// do when a call is not available is exactly the code worth testing.
/// </para>
/// <para>
/// <see cref="CanReadResults"/> is the gate. Reading a result depends on three
/// single-sourced addresses, and until the self-test has proved the whole round trip against
/// the attached client, every read refuses and says why rather than returning a plausible
/// nothing.
/// </para>
/// </remarks>
public interface ILuaEvaluator
{
    /// <summary>True once the round trip has been proved against this client.</summary>
    bool CanReadResults { get; }

    /// <summary>Why results cannot be read, when they cannot.</summary>
    string ResultsUnavailableReason { get; }

    /// <summary>Runs a script for its effect, ignoring anything it produces.</summary>
    bool Execute(string script);

    /// <summary>Evaluates an expression, or returns null when it could not be read.</summary>
    string? Evaluate(string expression);

    /// <summary>Evaluates and parses as an integer, or returns null.</summary>
    int? EvaluateInt(string expression);

    /// <summary>Evaluates and parses as a decimal, or returns null.</summary>
    double? EvaluateDouble(string expression);

    /// <summary>Evaluates an expression that yields a boolean, treating nothing as false.</summary>
    bool EvaluateBool(string expression);
}
