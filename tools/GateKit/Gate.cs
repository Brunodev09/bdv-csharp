using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GateKit;

/// <summary>
/// Test plumbing for the gates in <c>tools/</c>: run a sketch, read numbers out of what it printed,
/// record pass/fail lines, and turn the tally into an exit code.
///
/// <para>A gate is a single-file C# program:</para>
/// <code>
/// var run = Gate.RunSketch("sketches/lod_test.cs", "--shot", "/tmp/a.png", "--frames", "40");
/// Gate.Check("vertices fall", Gate.Int(run, @"VERTS=(\d+)") &lt; 200_000, "...");
/// return Gate.Report("LOD PASS", "LOD FAIL");
/// </code>
/// </summary>
public static class Gate
{
    private static readonly List<(string Name, bool Ok, string Detail)> _checks = new();

    /// <summary>Repository root, found by walking up from the executing assembly until the
    /// directory holding <c>sketches/</c> turns up. Gates then work from any working directory —
    /// the same problem <c>ContentPath</c> solves for the engine's own assets.</summary>
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "sketches"))) dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    /// <summary>How long a single sketch run may take before the gate gives up on it.</summary>
    public static int TimeoutSeconds { get; set; } = 180;

    /// <summary>Run a sketch to completion and return everything it wrote to stdout (stderr
    /// appended, so a crash is visible rather than silently producing no matches).
    ///
    /// <para>Always pass <c>--shot</c>: the engine exits after capturing, and a run without it
    /// never terminates.</para></summary>
    public static string RunSketch(string sketch, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add(sketch);
        psi.ArgumentList.Add("--");
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;

        // Read both pipes concurrently. Draining one to EOF before touching the other deadlocks
        // as soon as the child fills the other pipe's buffer and blocks writing to it -- classic,
        // silent, and it looks exactly like the child hanging.
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(TimeoutSeconds * 1000))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
            Console.Error.WriteLine(
                $"  gate: '{sketch}' did not exit within {TimeoutSeconds}s and was killed.\n" +
                "  A sketch only exits when it has been given --shot: the engine's capture path is\n" +
                "  what ends the run. A gate that omits it hangs forever.");
            Environment.Exit(1);
        }
        return stdout.Result + stderr.Result;
    }

    // ── reading values back out of a run ────────────────────────────────────

    public static string? Text(string output, string pattern, int group = 1)
    {
        var m = Regex.Match(output, pattern);
        return m.Success ? m.Groups[group].Value : null;
    }

    /// <summary>Parse an int out of the output, or fail the gate loudly. A missing number means the
    /// sketch crashed or changed its wording, and silently returning 0 would turn that into a
    /// confusing assertion failure three lines later.</summary>
    public static int Int(string output, string pattern, int group = 1)
    {
        var t = Text(output, pattern, group);
        if (t == null || !int.TryParse(t, out var v)) return Missing(output, pattern);
        return v;
    }

    public static float Float(string output, string pattern, int group = 1)
    {
        var t = Text(output, pattern, group);
        if (t == null || !float.TryParse(t, System.Globalization.NumberStyles.Float,
                                         System.Globalization.CultureInfo.InvariantCulture, out var v))
            return Missing(output, pattern);
        return v;
    }

    public static bool Has(string output, string needle) => output.Contains(needle, StringComparison.Ordinal);

    private static int Missing(string output, string pattern)
    {
        Console.Error.WriteLine($"  gate: no match for /{pattern}/ in the sketch output:");
        Console.Error.WriteLine(Indent(output));
        Environment.Exit(1);
        return 0;
    }

    private static string Indent(string s)
        => string.Join('\n', s.Split('\n').Take(40).Select(l => "    " + l));

    // ── tallying ────────────────────────────────────────────────────────────

    public static void Check(string name, bool ok, string detail)
    {
        _checks.Add((name, ok, detail));
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {name,-34} {detail}");
    }

    public static void Info(string line) => Console.WriteLine($"  {line}");

    public static void Blank() => Console.WriteLine();

    /// <summary>Print the verdict and return the process exit code: 0 when everything passed.</summary>
    public static int Report(string passMessage, string failMessage)
    {
        int failed = _checks.Count(c => !c.Ok);
        Console.WriteLine();
        Console.WriteLine(failed == 0 ? passMessage : $"{failMessage} — {failed} of {_checks.Count} checks failed");
        return failed == 0 ? 0 : 1;
    }
}
