using System.Globalization;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Json;

namespace Atlas.Observability;

/// <summary>
/// Emits the BrutalSystems house JSON log schema to stdout for LOG_FORMAT=json (k8s):
///   { "ts", "level", "logger", "trace_id", "span_id", "msg" }
/// matching the Python services byte-for-byte on field names, so Grafana/Alloy treat both
/// stacks identically. In particular Alloy's `loki.process` keys on a top-level `level`
/// field (CLEF's `@l` would not match, and CLEF omits it for Information entirely).
///
/// Levels are mapped to Python's logging vocabulary (Information->INFO, Fatal->CRITICAL).
/// service.name (from the resource enricher) and the exception text are appended when present,
/// followed by any remaining structured properties so they stay queryable in Loki.
/// </summary>
public sealed class HouseJsonFormatter : ITextFormatter
{
    static readonly JsonValueFormatter Values = new(typeTagName: null);

    public void Format(LogEvent e, TextWriter o)
    {
        o.Write("{\"ts\":\"");
        o.Write(e.Timestamp.ToString("O", CultureInfo.InvariantCulture));   // ISO-8601 (Alloy ignores it; Loki uses scrape time)
        o.Write("\",\"level\":\"");
        o.Write(LevelName(e.Level));
        o.Write("\",\"logger\":");
        WriteString(ScalarString(e, "SourceContext") ?? "", o);
        o.Write(",\"trace_id\":");
        WriteString(ScalarString(e, "trace_id") ?? "0", o);
        o.Write(",\"span_id\":");
        WriteString(ScalarString(e, "span_id") ?? "0", o);
        o.Write(",\"msg\":");
        WriteString(e.RenderMessage(CultureInfo.InvariantCulture), o);

        var service = ScalarString(e, "service.name");
        if (service is not null) { o.Write(",\"service.name\":"); WriteString(service, o); }

        if (e.Exception is not null) { o.Write(",\"exception\":"); WriteString(e.Exception.ToString(), o); }

        foreach (var p in e.Properties)
        {
            if (p.Key is "SourceContext" or "trace_id" or "span_id" or "service.name") continue;
            o.Write(',');
            JsonValueFormatter.WriteQuotedJsonString(p.Key, o);
            o.Write(':');
            Values.Format(p.Value, o);
        }

        o.Write('}');
        o.WriteLine();
    }

    static void WriteString(string s, TextWriter o) => JsonValueFormatter.WriteQuotedJsonString(s, o);

    static string? ScalarString(LogEvent e, string key) =>
        e.Properties.TryGetValue(key, out var v) && v is ScalarValue { Value: { } val }
            ? val as string ?? val.ToString()
            : null;

    // Serilog level -> Python logging vocabulary, so the `level` field/label is identical across stacks.
    static string LevelName(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => "DEBUG",
        LogEventLevel.Debug => "DEBUG",
        LogEventLevel.Information => "INFO",
        LogEventLevel.Warning => "WARNING",
        LogEventLevel.Error => "ERROR",
        LogEventLevel.Fatal => "CRITICAL",
        _ => level.ToString().ToUpperInvariant(),
    };
}
