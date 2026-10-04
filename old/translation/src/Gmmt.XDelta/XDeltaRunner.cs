using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Gmmt.XDelta;

public sealed class XDeltaException : Exception
{
    public int ExitCode { get; }
    public string StdErr { get; }

    public XDeltaException(string message, int exitCode, string stdErr)
        : base(message)
    {
        ExitCode = exitCode;
        StdErr = stdErr;
    }
}

public sealed class XDeltaResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
    public string? OutputPath { get; init; }
}

public static class XDeltaRunner
{
    /// <summary>
    /// Applies an xdelta patch to a source file, producing an output file.
    /// </summary>
    /// <param name="sourcePath">Path to the original/vanilla file</param>
    /// <param name="patchPath">Path to the xdelta patch file</param>
    /// <param name="outputPath">Path where the patched file will be written</param>
    /// <param name="timeoutMs">Timeout in milliseconds (default 60 seconds)</param>
    /// <returns>XDeltaResult with success status and output</returns>
    public static XDeltaResult Apply(string sourcePath, string patchPath, string outputPath, int timeoutMs = 60000)
    {
        var binary = FindXDeltaBinary();
        if (binary is null)
        {
            return new XDeltaResult
            {
                Success = false,
                ExitCode = -1,
                StdErr = "xdelta3 binary not found. Install xdelta3 or place it in PATH."
            };
        }

        // xdelta3 -d -s <source> <patch> <output>
        var args = $"-d -s \"{sourcePath}\" \"{patchPath}\" \"{outputPath}\"";

        var psi = new ProcessStartInfo(binary, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return new XDeltaResult
                {
                    Success = false,
                    ExitCode = -1,
                    StdErr = "Failed to start xdelta3 process"
                };
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();

            if (!proc.WaitForExit(timeoutMs))
            {
                proc.Kill();
                return new XDeltaResult
                {
                    Success = false,
                    ExitCode = -1,
                    StdOut = stdout,
                    StdErr = "xdelta3 process timed out"
                };
            }

            return new XDeltaResult
            {
                Success = proc.ExitCode == 0,
                ExitCode = proc.ExitCode,
                StdOut = stdout,
                StdErr = stderr,
                OutputPath = proc.ExitCode == 0 ? outputPath : null
            };
        }
        catch (Exception ex)
        {
            return new XDeltaResult
            {
                Success = false,
                ExitCode = -1,
                StdErr = $"Failed to execute xdelta3: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Applies an xdelta patch, throwing on failure.
    /// </summary>
    public static void ApplyOrThrow(string sourcePath, string patchPath, string outputPath, int timeoutMs = 60000)
    {
        var result = Apply(sourcePath, patchPath, outputPath, timeoutMs);
        if (!result.Success)
        {
            throw new XDeltaException(
                $"xdelta3 failed (exit {result.ExitCode}): {result.StdErr}",
                result.ExitCode,
                result.StdErr);
        }
    }

    /// <summary>
    /// Checks if xdelta3 is available.
    /// </summary>
    public static bool IsAvailable()
    {
        return FindXDeltaBinary() is not null;
    }

    /// <summary>
    /// Gets the path to the xdelta3 binary, or null if not found.
    /// </summary>
    public static string? GetBinaryPath()
    {
        return FindXDeltaBinary();
    }

    private static string? FindXDeltaBinary()
    {
        // 1. Check for bundled binary next to this assembly
        var assemblyDir = Path.GetDirectoryName(typeof(XDeltaRunner).Assembly.Location);
        if (assemblyDir is not null)
        {
            var bundledName = GetPlatformBinaryName();
            var bundledPath = Path.Combine(assemblyDir, bundledName);
            if (File.Exists(bundledPath))
                return bundledPath;

            // Check in runtimes subfolder
            var rid = GetRuntimeId();
            var runtimePath = Path.Combine(assemblyDir, "runtimes", rid, "native", bundledName);
            if (File.Exists(runtimePath))
                return runtimePath;
        }

        // 2. Check PATH
        var pathBinary = FindInPath(GetPlatformBinaryName());
        if (pathBinary is not null)
            return pathBinary;

        // 3. Check common locations
        var commonPaths = GetCommonPaths();
        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static string GetPlatformBinaryName()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "xdelta3.exe"
            : "xdelta3";
    }

    private static string GetRuntimeId()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RuntimeInformation.OSArchitecture == Architecture.X64 ? "win-x64" : "win-x86";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return RuntimeInformation.OSArchitecture == Architecture.X64 ? "linux-x64" : "linux-arm64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        return "unknown";
    }

    private static string? FindInPath(string binaryName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv is null)
            return null;

        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        var paths = pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in paths)
        {
            var fullPath = Path.Combine(dir, binaryName);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    private static IEnumerable<string> GetCommonPaths()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return @"C:\Program Files\xdelta3\xdelta3.exe";
            yield return @"C:\xdelta3\xdelta3.exe";
        }
        else
        {
            yield return "/usr/bin/xdelta3";
            yield return "/usr/local/bin/xdelta3";
            yield return "/opt/homebrew/bin/xdelta3";

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, ".local", "bin", "xdelta3");
        }
    }
}
