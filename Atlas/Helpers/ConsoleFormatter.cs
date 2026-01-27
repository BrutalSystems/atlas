using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Helpers;

// playing around with simplier log output 


public class ConsoleFormatter : Microsoft.Extensions.Logging.Console.ConsoleFormatter
{
    Dictionary<LogLevel, string> loglevels = new Dictionary<LogLevel, string>();

    public ConsoleFormatter() : base("Atlas Log Formatter")
    {
        loglevels.Add(LogLevel.Critical, "crit");
        loglevels.Add(LogLevel.Debug, "dbug");
        loglevels.Add(LogLevel.Error, "fail");
        loglevels.Add(LogLevel.Information, "info");
        loglevels.Add(LogLevel.None, "none");
        loglevels.Add(LogLevel.Trace, "trce");
        loglevels.Add(LogLevel.Warning, "warn");
    }

    public ConsoleFormatter(string name) : base(name)
    {
    }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        string? message =
            logEntry.Formatter?.Invoke(
                logEntry.State, logEntry.Exception);
        var t = typeof(TState);

        if (message is null)
        {
            return;
        }

        var maxCatLength = 30;
        var cat = logEntry.Category.Split('.').Last();
        cat = cat.PadRight(maxCatLength);
        var startSubstring = Math.Max(cat.Length - maxCatLength, 0);
        var isMultiline = message.Contains('\n');
        var sep = isMultiline ? '\n' : '\t';
        message = isMultiline ? "\t\t" + String.Join("\n\t\t", message.Split('\n')) : message;
        message = $"{loglevels[logEntry.LogLevel]}:   {cat.Substring(startSubstring)}{sep}{message}";
        message = message.Replace("\t", "    ");
        textWriter.WriteLine(message);
    }
}