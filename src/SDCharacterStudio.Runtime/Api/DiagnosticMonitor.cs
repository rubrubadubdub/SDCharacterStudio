using StardewModdingAPI;
using StardewModdingAPI.Framework.Logging;

namespace SDCharacterStudio.Runtime.Api;

/// <summary>Forwards normal runtime logging and offers warnings/errors to authoring clients.</summary>
public sealed class DiagnosticMonitor(IMonitor inner) : IMonitor
{
    private readonly HashSet<string> once = new();
    public event EventHandler<RuntimeDiagnosticEventArgs>? Diagnostic;
    public bool IsVerbose => inner.IsVerbose;
    public void Log(string message, LogLevel level = LogLevel.Trace)
    {
        // An engine embedded in another mod stays out of the player's console: routine news goes to the log file only.
        if (level == LogLevel.Info && SDCharacterStudio.Core.EngineNames.IsEmbedded) level = LogLevel.Trace;
        inner.Log(message, level);
        if (level is not (LogLevel.Warn or LogLevel.Error or LogLevel.Alert)) return;
        if (Diagnostic is { } listeners)
            foreach (EventHandler<RuntimeDiagnosticEventArgs> listener in listeners.GetInvocationList())
                try { listener(this, new(level.ToString(), message)); } catch { /* A client cannot break runtime logging. */ }
    }
    public void LogOnce(string message, LogLevel level = LogLevel.Trace) { if (once.Add(message)) Log(message, level); }
    public void VerboseLog(string message) => inner.VerboseLog(message);
    public void VerboseLog(ref VerboseLogStringHandler message) => inner.VerboseLog(ref message);
}
