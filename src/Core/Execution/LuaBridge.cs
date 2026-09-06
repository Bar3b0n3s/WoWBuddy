using System.Globalization;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.Core.Execution;

/// <summary>
/// Runs Lua in the client and reads values back out.
/// </summary>
/// <remarks>
/// <para>
/// Lua is how the bot asks the client things that are awkward to read from memory: spell
/// cooldowns, quest text, bag contents, whether a unit is friendly.
/// <para>
/// It is also, on this client, how the bot casts. Blizzard's protection refuses the
/// interesting verbs when they come from <em>addon</em> code, which is tainted; a script run
/// straight from the render-loop hook belongs to no addon and carries no taint. That is a
/// property of this build rather than a guarantee, so <c>SpellCaster</c> checks it against
/// the client before relying on it. Where a verified native address exists, the native route
/// is preferred, since it does not depend on the question at all.
/// </para>
/// </para>
/// <para>
/// Return values work by asking Lua to assign a global and then reading the global back
/// through the client's own accessor. That accessor is single-sourced, so nothing here is
/// used until <see cref="SelfTest"/> has proved the whole round trip with a value only a
/// working chain could produce.
/// </para>
/// </remarks>
public sealed class LuaBridge
{
    /// <summary>
    /// Name of the global the bridge assigns results to.
    /// </summary>
    /// <remarks>
    /// Prefixed so it cannot collide with a variable belonging to the player's own addons.
    /// Whatever the bot writes here it also immediately reads, so a collision would corrupt
    /// somebody's UI rather than merely confuse the bot.
    /// </remarks>
    public const string ResultGlobal = "__wowbuddy_result";

    private readonly GameThreadExecutor _executor;
    private readonly IMemoryReader _memory;

    public LuaBridge(GameThreadExecutor executor, IMemoryReader memory)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    /// <summary>
    /// True once <see cref="SelfTest"/> has confirmed values can be read back.
    /// </summary>
    /// <remarks>
    /// When false, <see cref="Execute"/> still works: running a script needs only the
    /// well-corroborated entry point. It is reading results that depends on the parts that
    /// are single-sourced, so that is what gets gated.
    /// </remarks>
    public bool CanReadResults { get; private set; }

    /// <summary>Why result reading is unavailable, or an empty string.</summary>
    public string ResultsUnavailableReason { get; private set; } = "The Lua self-test has not run yet.";

    /// <summary>
    /// Runs a Lua script in the client, discarding any result.
    /// </summary>
    /// <remarks>
    /// The script runs with the same privileges as an addon, so protected functions will
    /// refuse it. That refusal is silent on the client's side, which is why the bot never
    /// routes actions through here.
    /// </remarks>
    public bool Execute(string script)
    {
        ArgumentException.ThrowIfNullOrEmpty(script);

        _executor.ResetScratch();

        nint scriptAddress = _executor.WriteScratchString(script);
        if (scriptAddress == 0)
        {
            Log.For<LuaBridge>().Error("Could not place a {Length}-character script in the client", script.Length);
            return false;
        }

        // FrameScript_Execute(code, source, unused). The source argument only appears in
        // error messages, so reusing the code pointer is harmless and saves a copy.
        var call = new RemoteCall(
            Offsets335a.Rebase(Offsets335a.Execution.FrameScriptExecute, _memory.ModuleBase),
            [(uint)scriptAddress, (uint)scriptAddress, 0u],
            CallingConvention.Cdecl,
            ReturnKind.None);

        ExecutionResult result = _executor.Execute(call);
        if (!result.Success)
        {
            Log.For<LuaBridge>().Error("Lua execution failed: {Error}", result.Error);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Evaluates a Lua expression and returns its value as a string.
    /// </summary>
    /// <param name="expression">
    /// A Lua expression, not a statement. It is wrapped in an assignment and a
    /// <c>tostring</c>, so <c>UnitLevel("target")</c> works and <c>x = 1</c> does not.
    /// </param>
    /// <returns>The value, or null when it could not be read.</returns>
    public string? Evaluate(string expression)
    {
        ArgumentException.ThrowIfNullOrEmpty(expression);

        if (!CanReadResults)
        {
            Log.For<LuaBridge>().Warning(
                "Refusing to read a Lua result: {Reason}", ResultsUnavailableReason);
            return null;
        }

        return EvaluateUnchecked(expression);
    }

    /// <summary>Evaluates and parses as an integer, or returns null.</summary>
    public int? EvaluateInt(string expression)
    {
        string? text = Evaluate(expression);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    /// <summary>Evaluates and parses as a double, or returns null.</summary>
    public double? EvaluateDouble(string expression)
    {
        string? text = Evaluate(expression);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }

    /// <summary>
    /// Evaluates a Lua expression that yields a boolean.
    /// </summary>
    /// <remarks>
    /// Lua's <c>tostring</c> renders booleans as "true" and "false", and nil as "nil", which
    /// is treated as false: an expression that produced nothing did not produce truth.
    /// </remarks>
    public bool EvaluateBool(string expression) =>
        string.Equals(Evaluate(expression), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Proves the whole Lua round trip against the attached client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The test asks the client to compute something the bot did not tell it: a fixed prefix
    /// concatenated with the result of an arithmetic expression. Getting the expected string
    /// back means the script reached a working Lua interpreter, the interpreter ran it, the
    /// global was assigned, the accessor found it, and the pointer it returned was readable —
    /// which is every link in the chain, including all three single-sourced addresses.
    /// </para>
    /// <para>
    /// A wrong address cannot pass this by accident. It returns nothing, returns an
    /// unreadable pointer, or returns some other string; none of those is the answer to a
    /// sum the bot chose at random.
    /// </para>
    /// </remarks>
    public bool SelfTest(out string detail)
    {
        int left = Random.Shared.Next(100, 999);
        int right = Random.Shared.Next(100, 999);
        string expected = $"wowbuddy{left + right}";

        // Deliberately makes the client do the arithmetic.
        string? actual = EvaluateUnchecked($"\"wowbuddy\" .. ({left} + {right})");

        if (actual is null)
        {
            CanReadResults = false;
            ResultsUnavailableReason =
                "Reading a value back from Lua produced nothing. Either FrameScript_Execute, " +
                "GetActivePlayerObject or GetLocalizedText is wrong for this client.";
            detail = ResultsUnavailableReason;
            return false;
        }

        if (actual != expected)
        {
            CanReadResults = false;
            ResultsUnavailableReason =
                $"The Lua round trip returned \"{actual}\" but the client was asked to compute " +
                $"\"{expected}\". The result accessor is reading something other than the global " +
                "that was just assigned.";
            detail = ResultsUnavailableReason;
            return false;
        }

        CanReadResults = true;
        ResultsUnavailableReason = string.Empty;
        detail = $"Lua round trip confirmed: the client computed \"{expected}\".";
        return true;
    }

    /// <summary>
    /// The result round trip, without the <see cref="CanReadResults"/> gate.
    /// </summary>
    /// <remarks>
    /// Private because the gate exists to stop the rest of the bot acting on a value read
    /// through unverified addresses. <see cref="SelfTest"/> is the one caller allowed to run
    /// before the gate opens, because opening it is its whole job.
    /// </remarks>
    private string? EvaluateUnchecked(string expression)
    {
        _executor.ResetScratch();

        // tostring() so numbers, booleans and nil all come back as readable text rather than
        // as a type the string accessor would decline to return.
        string script = $"{ResultGlobal} = tostring({expression});";

        nint scriptAddress = _executor.WriteScratchString(script);
        nint nameAddress = _executor.WriteScratchString(ResultGlobal);

        if (scriptAddress == 0 || nameAddress == 0)
        {
            return null;
        }

        var execute = new RemoteCall(
            Offsets335a.Rebase(Offsets335a.Execution.FrameScriptExecute, _memory.ModuleBase),
            [(uint)scriptAddress, (uint)scriptAddress, 0u],
            CallingConvention.Cdecl,
            ReturnKind.None);

        if (!_executor.Execute(execute).Success)
        {
            return null;
        }

        // GetLocalizedText is a member of the active player object, so the object has to be
        // fetched first and handed over as `this`. Doing both in one thunk keeps it to a
        // single frame and avoids the object going stale in between.
        var read = new RemoteCall(
            Offsets335a.Rebase(Offsets335a.Execution.GetLocalizedText, _memory.ModuleBase),
            [(uint)nameAddress, unchecked((uint)-1)],
            CallingConvention.ThisCall,
            ReturnKind.Int32,
            Prelude: RemoteCall.To(
                Offsets335a.Rebase(Offsets335a.Execution.GetActivePlayerObject, _memory.ModuleBase)));

        ExecutionResult result = _executor.Execute(read);
        if (!result.Success || result.Pointer == 0)
        {
            return null;
        }

        return _memory.TryReadCString(result.Pointer, out string value, maxLength: 512) ? value : null;
    }
}
