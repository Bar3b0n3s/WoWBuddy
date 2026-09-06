using System.Text;
using System.Text.RegularExpressions;
using WoWBuddy.Core.Execution;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Offsets;
using WoWBuddy.Core.Tests.Fakes;
using Xunit;

namespace WoWBuddy.Core.Tests;

/// <summary>
/// Exercises the Lua round trip against a client that pretends to have a Lua interpreter.
/// </summary>
/// <remarks>
/// <para>
/// The stand-in understands only what the bridge actually emits: an assignment of
/// <c>tostring(expression)</c> to a known global, and a read of that global by name. That is
/// enough to check the parts that are ours — the script the bridge builds, where it puts the
/// strings, how it chains the two calls, and how it decides whether the round trip can be
/// trusted.
/// </para>
/// <para>
/// It cannot check that the real client's functions behave this way. That is what the
/// self-test does against a live game, and why the gate exists at all.
/// </para>
/// </remarks>
public sealed class LuaBridgeTests : IDisposable
{
    private const nint D3DBase = 0x6D000000;
    private const nint EndSceneAddress = D3DBase + 0x12340;

    private readonly SimulatedClient _client = new();
    private readonly FakeModuleResolver _modules =
        new FakeModuleResolver().Add("d3d9.dll", D3DBase, 0x100000);

    private readonly Dictionary<string, string> _luaGlobals = [];
    private CancellationTokenSource? _renderLoop;
    private Task? _renderTask;
    private nint _resultStorage;

    /// <summary>When set, GetLocalizedText returns this instead of the real global.</summary>
    private string? _forcedResult;

    /// <summary>When true, GetLocalizedText returns null, as a wrong address would.</summary>
    private bool _breakResultReads;

    public LuaBridgeTests()
    {
        nint deviceOuter = _client.Allocate(0x8000);
        nint device = _client.Allocate(0x100);
        nint vtable = _client.Allocate(0x200);

        _client.WritePointer(
            Offsets335a.Rebase(Offsets335a.Execution.D3DDevicePointer1, _client.ModuleBase), deviceOuter);
        _client.WritePointer(deviceOuter + (nint)Offsets335a.Execution.D3DDevicePointer2, device);
        _client.WritePointer(device, vtable);
        _client.WritePointer(vtable + (nint)Offsets335a.Execution.D3DEndSceneVTableOffset, EndSceneAddress);

        _resultStorage = _client.Allocate(1024);
    }

    private (GameThreadExecutor Executor, LuaBridge Lua) Start()
    {
        var executor = new GameThreadExecutor(_client, _modules);
        Assert.True(executor.TryInstall(out string failure), failure);

        nint execute = Offsets335a.Rebase(Offsets335a.Execution.FrameScriptExecute, _client.ModuleBase);
        nint getPlayer = Offsets335a.Rebase(Offsets335a.Execution.GetActivePlayerObject, _client.ModuleBase);
        nint getText = Offsets335a.Rebase(Offsets335a.Execution.GetLocalizedText, _client.ModuleBase);

        _renderLoop = new CancellationTokenSource();
        CancellationToken token = _renderLoop.Token;

        _renderTask = Task.Run(
            () =>
            {
                var interpreter = new X86Interpreter(_client, (function, args, _) =>
                {
                    if (function == execute)
                    {
                        RunScript(args[0]);
                        return new CallOutcome();
                    }

                    if (function == getPlayer)
                    {
                        return new CallOutcome(Eax: 0x0BADF00D);
                    }

                    if (function == getText)
                    {
                        return new CallOutcome(Eax: (uint)ReadGlobal(args[0]), CalleeCleansStack: true);
                    }

                    return new CallOutcome();
                });

                while (!token.IsCancellationRequested)
                {
                    interpreter.Run(executor.StubAddress);
                    Thread.Sleep(1);
                }
            },
            token);

        return (executor, new LuaBridge(executor, _client));
    }

    /// <summary>Stands in for FrameScript_Execute: handles <c>name = tostring(expr);</c>.</summary>
    private void RunScript(uint scriptAddress)
    {
        if (!_client.TryReadCString((nint)scriptAddress, out string script, maxLength: 512))
        {
            return;
        }

        Match match = Regex.Match(script, @"^(\w+)\s*=\s*tostring\((.*)\);$", RegexOptions.Singleline);
        if (!match.Success)
        {
            return;
        }

        _luaGlobals[match.Groups[1].Value] = Evaluate(match.Groups[2].Value);
    }

    /// <summary>A very small subset of Lua: string literals, concatenation and integer sums.</summary>
    private static string Evaluate(string expression)
    {
        expression = expression.Trim();

        Match concat = Regex.Match(expression, @"^""([^""]*)""\s*\.\.\s*\((\d+)\s*\+\s*(\d+)\)$");
        if (concat.Success)
        {
            int sum = int.Parse(concat.Groups[2].Value) + int.Parse(concat.Groups[3].Value);
            return concat.Groups[1].Value + sum;
        }

        Match literal = Regex.Match(expression, @"^""([^""]*)""$");
        if (literal.Success)
        {
            return literal.Groups[1].Value;
        }

        Match sumOnly = Regex.Match(expression, @"^(\d+)\s*\+\s*(\d+)$");
        if (sumOnly.Success)
        {
            return (int.Parse(sumOnly.Groups[1].Value) + int.Parse(sumOnly.Groups[2].Value))
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return expression;
    }

    /// <summary>Stands in for GetLocalizedText: returns a pointer to the named global's value.</summary>
    private nint ReadGlobal(uint nameAddress)
    {
        if (_breakResultReads)
        {
            return 0;
        }

        if (!_client.TryReadCString((nint)nameAddress, out string name))
        {
            return 0;
        }

        string? value = _forcedResult ?? (_luaGlobals.TryGetValue(name, out string? v) ? v : null);
        if (value is null)
        {
            return 0;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        _client.TryWriteBytes(_resultStorage, bytes);
        _client.WriteByte(_resultStorage + bytes.Length, 0);
        return _resultStorage;
    }

    [Fact]
    public void SelfTestPassesWhenTheWholeChainWorks()
    {
        (GameThreadExecutor executor, LuaBridge lua) = Start();
        using (executor)
        {
            Assert.True(lua.SelfTest(out string detail), detail);
            Assert.True(lua.CanReadResults);
            Assert.Contains("confirmed", detail, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SelfTestFailsWhenNothingComesBack()
    {
        // What a wrong GetLocalizedText or GetActivePlayerObject address looks like.
        _breakResultReads = true;

        (GameThreadExecutor executor, LuaBridge lua) = Start();
        using (executor)
        {
            Assert.False(lua.SelfTest(out string detail));
            Assert.False(lua.CanReadResults);
            Assert.Contains("produced nothing", detail, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SelfTestFailsWhenTheWrongValueComesBack()
    {
        // An address that reads some other string cannot produce the answer to a sum the
        // bot picked at random, which is the point of computing one.
        _forcedResult = "something else entirely";

        (GameThreadExecutor executor, LuaBridge lua) = Start();
        using (executor)
        {
            Assert.False(lua.SelfTest(out string detail));
            Assert.False(lua.CanReadResults);
            Assert.Contains("something else entirely", detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ResultsAreRefusedUntilTheSelfTestHasPassed()
    {
        (GameThreadExecutor executor, LuaBridge lua) = Start();
        using (executor)
        {
            Assert.Null(lua.Evaluate("\"hello\""));

            Assert.True(lua.SelfTest(out _));

            Assert.Equal("hello", lua.Evaluate("\"hello\""));
        }
    }

    [Fact]
    public void ExecuteWorksWithoutTheResultGateBecauseItNeedsOnlyTheCorroboratedEntryPoint()
    {
        (GameThreadExecutor executor, LuaBridge lua) = Start();
        using (executor)
        {
            Assert.False(lua.CanReadResults);
            Assert.True(lua.Execute("__probe = tostring(\"ran\");"));
            Assert.Equal("ran", _luaGlobals["__probe"]);
        }
    }

    [Fact]
    public void TypedEvaluationParsesWhatTheClientReturns()
    {
        (GameThreadExecutor executor, LuaBridge lua) = Start();
        using (executor)
        {
            Assert.True(lua.SelfTest(out _));

            Assert.Equal(80, lua.EvaluateInt("40 + 40"));
            Assert.Equal(80d, lua.EvaluateDouble("40 + 40"));
            Assert.True(lua.EvaluateBool("\"true\""));
            Assert.False(lua.EvaluateBool("\"false\""));
            Assert.False(lua.EvaluateBool("\"nil\""));
        }
    }

    [Fact]
    public void TypedEvaluationReturnsNullRatherThanZeroForUnreadableValues()
    {
        // A caller must be able to tell "the client said zero" from "nothing came back".
        (GameThreadExecutor executor, LuaBridge lua) = Start();
        using (executor)
        {
            Assert.True(lua.SelfTest(out _));
            _breakResultReads = true;

            Assert.Null(lua.EvaluateInt("1 + 1"));
            Assert.Null(lua.EvaluateDouble("1 + 1"));
        }
    }

    [Fact]
    public void TheResultGlobalIsNamespacedSoItCannotCollideWithAnAddon()
    {
        Assert.StartsWith("__wowbuddy", LuaBridge.ResultGlobal, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _renderLoop?.Cancel();
        try
        {
            _renderTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Torn down with the test.
        }

        _renderLoop?.Dispose();
    }
}
