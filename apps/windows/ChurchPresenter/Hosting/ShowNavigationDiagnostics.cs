using System.Diagnostics;
using System.Text;

namespace ChurchPresenter.Hosting;

/// <summary>
/// Trace logging for Show frame navigation lifecycle. Writes to <see cref="Debug"/> and
/// <c>.agent-logs/show-navigation-trace.log</c> under the repo when present.
/// </summary>
internal static class ShowNavigationDiagnostics
{
    private static readonly object Gate = new();
    private static string? _logPath;
    private static int _showInstanceSequence;
    private static int _navigationOpSequence;

    public static int RegisterShowInstance() => Interlocked.Increment(ref _showInstanceSequence);

    static ShowNavigationDiagnostics()
    {
        Log("BisectFlags", ShowRenderBisect.DescribeActiveFlags());
    }

    public static int BeginNavigationOp(string detail)
    {
        int op = Interlocked.Increment(ref _navigationOpSequence);
        Log("NavOp.Begin", $"op={op} {detail}");
        return op;
    }

    public static void EndNavigationOp(int op, string detail) =>
        Log("NavOp.End", $"op={op} {detail}");

    public static void LogCrash(string source, Exception exception)
    {
        string hresult = exception is System.Runtime.InteropServices.COMException com
            ? $" hresult=0x{com.HResult:X8}"
            : string.Empty;
        Log("Crash", $"{source} type={exception.GetType().Name} msg={exception.Message}{hresult}");
    }

    public static void Log(string phase, string detail, int? instanceId = null)
    {
        string stamp = DateTime.UtcNow.ToString("O");
        string id = instanceId is int i ? $" instance={i}" : string.Empty;
        string line = $"[{stamp}] ShowNav {phase}{id}: {detail}";
        Debug.WriteLine(line);

        try
        {
            lock (Gate)
            {
                _logPath ??= ResolveLogPath();
                if (_logPath is not null)
                    File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never affect navigation.
        }
    }

    private static string? ResolveLogPath()
    {
        string? dir = TryFindAgentLogsDir(Environment.CurrentDirectory)
            ?? TryFindAgentLogsDir(AppContext.BaseDirectory);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "show-navigation-trace.log");
        }

        string fallbackDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChurchPresenter",
            "agent-logs");
        Directory.CreateDirectory(fallbackDir);
        return Path.Combine(fallbackDir, "show-navigation-trace.log");
    }

    private static string? TryFindAgentLogsDir(string start)
    {
        try
        {
            DirectoryInfo? current = new(Path.GetFullPath(start));
            for (int depth = 0; depth < 8 && current is not null; depth++)
            {
                string candidate = Path.Combine(current.FullName, ".agent-logs");
                if (Directory.Exists(candidate))
                    return candidate;

                current = current.Parent;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }
}
