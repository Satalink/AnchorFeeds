using System.Text;
using System.Security;

namespace AnchorFeeds.Services;

public static class DiagnosticLog
{
    private static readonly object Sync = new();
    private static string? _logPath;

    public static bool IsEnabled { get; private set; }

    public static event Action<string>? LoggingFailed;

    public static string? LogPath
    {
        get
        {
            lock (Sync)
            {
                return _logPath;
            }
        }
    }

    public static void Configure(bool enabled)
    {
        string? failure = null;
        lock (Sync)
        {
            if (!enabled)
            {
                if (IsEnabled)
                {
                    try
                    {
                        WriteLocked("INFO", "Logging", "Verbose debug logging disabled.");
                    }
                    catch (Exception ex) when (IsLogWriteException(ex))
                    {
                        failure = DisableAfterFailure(ex);
                    }
                }

                IsEnabled = false;
                _logPath = null;
            }
            else
            {
                var executableDirectory = Path.GetDirectoryName(Environment.ProcessPath)
                    ?? AppContext.BaseDirectory;
                var logPath = Path.Combine(executableDirectory, "AnchorFeeds-debug.log");
                _logPath = logPath;
                IsEnabled = true;
                try
                {
                    Directory.CreateDirectory(executableDirectory);
                    WriteLocked("INFO", "Logging", $"Verbose debug logging enabled. Log file: {logPath}");
                }
                catch (Exception ex) when (IsLogWriteException(ex))
                {
                    failure = DisableAfterFailure(ex);
                }
            }
        }

        NotifyFailure(failure);
    }

    public static void Write(string category, string message)
    {
        string? failure = null;
        lock (Sync)
        {
            if (IsEnabled)
            {
                try
                {
                    WriteLocked("DEBUG", category, message);
                }
                catch (Exception ex) when (IsLogWriteException(ex))
                {
                    failure = DisableAfterFailure(ex);
                }
            }
        }

        NotifyFailure(failure);
    }

    public static void Exception(string category, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string? failure = null;
        lock (Sync)
        {
            if (IsEnabled)
            {
                try
                {
                    WriteLocked("ERROR", category, exception.ToString());
                }
                catch (Exception ex) when (IsLogWriteException(ex))
                {
                    failure = DisableAfterFailure(ex);
                }
            }
        }

        NotifyFailure(failure);
    }

    private static bool IsLogWriteException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;

    private static string DisableAfterFailure(Exception exception)
    {
        IsEnabled = false;
        var failedPath = _logPath;
        _logPath = null;
        return $"Verbose debug logging was disabled because the log file could not be written" +
               $"{(failedPath is null ? string.Empty : $" ({failedPath})")}: {exception.Message}";
    }

    private static void NotifyFailure(string? failure)
    {
        if (failure is not null)
        {
            LoggingFailed?.Invoke(failure);
        }
    }

    private static void WriteLocked(string level, string category, string message)
    {
        if (_logPath is null)
        {
            return;
        }

        var entry = $"{DateTimeOffset.Now:O} [{Environment.CurrentManagedThreadId}] {level} [{category}] {message}{Environment.NewLine}";
        File.AppendAllText(_logPath, entry, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
