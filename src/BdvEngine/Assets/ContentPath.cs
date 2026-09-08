using System;
using System.IO;

namespace BdvEngine;

/// <summary>
/// Resolves relative content paths to absolute ones, so a game finds its files no matter what the
/// process working directory happens to be.
///
/// <para><b>Why this exists.</b> <c>File.Exists("assets/tile/x.png")</c> resolves against the
/// process CWD. That is the project folder when you run <c>dotnet run</c> from inside it, the repo
/// root when you run <c>dotnet run --project path/to/Game.csproj</c>, and something else again for
/// a double-clicked binary or an IDE launch. Same build, same files on disk, and the game silently
/// loses all of its art in two of those three cases — every load is a soft failure, so it starts up
/// and renders nothing rather than telling you what went wrong.</para>
///
/// <para>The fix is to resolve against <see cref="AppRoot"/> — the directory the binary lives in,
/// which is where the <c>.csproj</c>'s <c>&lt;Content Include="assets\**"&gt;</c> rule copies
/// everything. CWD is still probed as a fallback so running from a source tree with assets that
/// were never copied keeps working.</para>
///
/// <code>
/// if (File.Exists(ContentPath.Resolve("assets/biomes.png"))) { }   // works from anywhere
/// if (ContentPath.Exists("assets/biomes.png")) { }                 // same thing, shorter
/// </code>
/// </summary>
public static class ContentPath
{
    /// <summary>Directory the executable lives in — the primary place content is looked up.
    /// Settable for tests and for tools that stage content elsewhere.</summary>
    public static string AppRoot { get; set; } = AppContext.BaseDirectory;

    /// <summary>Find <paramref name="relativePath"/> on disk, probing <see cref="AppRoot"/> then the
    /// working directory. Returns false (with <paramref name="fullPath"/> set to the canonical
    /// AppRoot location) when it is in neither, so callers can report where they looked.</summary>
    public static bool TryResolve(string relativePath, out string fullPath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            fullPath = relativePath ?? string.Empty;
            return false;
        }

        if (Path.IsPathRooted(relativePath))
        {
            fullPath = relativePath;
            return File.Exists(fullPath) || Directory.Exists(fullPath);
        }

        fullPath = Path.Combine(AppRoot, relativePath);
        if (File.Exists(fullPath) || Directory.Exists(fullPath)) return true;

        var cwd = Path.Combine(Environment.CurrentDirectory, relativePath);
        if (File.Exists(cwd) || Directory.Exists(cwd))
        {
            fullPath = cwd;
            return true;
        }

        // Neither: hand back the AppRoot form, which is the location an error message should name
        // because it is where the build is supposed to have put the file.
        return false;
    }

    /// <summary>Absolute path for <paramref name="relativePath"/>. When the file is in neither probe
    /// location this returns the <see cref="AppRoot"/> form rather than throwing — callers that
    /// treat a missing asset as a soft failure keep doing so, and get a useful path to log.</summary>
    public static string Resolve(string relativePath)
    {
        TryResolve(relativePath, out var full);
        return full;
    }

    /// <summary>True when the path exists in either probe location.</summary>
    public static bool Exists(string relativePath) => TryResolve(relativePath, out _);

    /// <summary>Where a lookup searched, for "not found" messages.</summary>
    public static string DescribeSearch(string relativePath)
        => Path.IsPathRooted(relativePath)
            ? relativePath
            : $"{Path.Combine(AppRoot, relativePath)} or {Path.Combine(Environment.CurrentDirectory, relativePath)}";
}
