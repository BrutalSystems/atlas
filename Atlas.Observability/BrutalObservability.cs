using System.Diagnostics;
using Atlas.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Atlas.Observability;

/// <summary>
/// Fleet-wide logging + observability bootstrap. Opt-in per app: an app calls
/// <see cref="BootstrapLogger"/> first, then <c>AddBrutalObservability</c> on its host builder
/// (<see cref="WebApplicationBuilder"/> for APIs, <see cref="IHostBuilder"/> for workers/console jobs).
/// Apps that do NOT call these keep Atlas's legacy console formatter (see Atlas.Helpers.Logging) —
/// so the fleet can cut over one app at a time.
///
/// Output: CLEF JSON to stdout by default (LOG_FORMAT=json), human-readable colored console in
/// local dev (LOG_FORMAT=console). Traces + metrics export via OTLP only when
/// OTEL_EXPORTER_OTLP_ENDPOINT is set (i.e. in-cluster), so local dev stays logs-only.
/// </summary>
public static class BrutalObservability
{
    /// <summary>
    /// Minimal logger for the window before configuration is available. Replaced by the
    /// fully-configured logger in AddBrutalObservability. Prevents Serilog's silent default
    /// logger from swallowing very early Log.* calls.
    /// </summary>
    public static void BootstrapLogger()
    {
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateBootstrapLogger();
    }

    /// <summary>ASP.NET Core APIs. Call right after <c>WebApplication.CreateBuilder</c>.</summary>
    public static void AddBrutalObservability(this WebApplicationBuilder builder)
    {
        var serviceName = ServiceName();

        // Build the final pipeline ONCE and assign it as the global logger, before any service
        // registration creates a logger, so host DI and Atlas's static factory bind to the same
        // instance — no bootstrap-logger reload/freeze ambiguity.
        Log.Logger = BuildLogger(builder.Configuration, serviceName);
        builder.Services.AddSerilog(Log.Logger, dispose: false);   // host DI ILogger<T> -> Serilog
        WireStaticLoggersAndFlush();
        AddOtel(builder.Services, serviceName, includeAspNetCore: true);
    }

    /// <summary>Worker services / console jobs (Host.CreateDefaultBuilder). Call before <c>Build()</c>.</summary>
    public static void AddBrutalObservability(this IHostBuilder hostBuilder)
    {
        var serviceName = ServiceName();

        // Read the host's configuration (content-root appsettings.json + env vars) rather than
        // Atlas's Env.GetConfiguration(). UseSerilog sets the global Log.Logger during Build();
        // the worker creates no static logger during registration (migrations run post-Build),
        // so the static-logger hook below binds to the final logger when first used at runtime.
        hostBuilder.UseSerilog((context, lc) => ConfigureLogger(lc, context.Configuration, serviceName));
        WireStaticLoggersAndFlush();
        hostBuilder.ConfigureServices((_, services) => AddOtel(services, serviceName, includeAspNetCore: false));
    }

    // Routes Atlas static-context loggers (Logging.CreateLogger<T>) into the same Serilog pipeline.
    // Setting this delegate flips Atlas's shared factory off its legacy console formatter, for THIS
    // process only — other apps leave it null and stay legacy.
    static void WireStaticLoggersAndFlush()
    {
        Logging.ConfigureProviders = builder => builder.AddSerilog(Log.Logger, dispose: false);

        // Drop any factory Atlas built during early startup (e.g. Env.Initialize) so the next
        // CreateLogger call rebuilds against the final Serilog logger above.
        Logging.DisposeSharedLoggerFactory();

        // We own the logger lifetime (dispose:false above), so flush explicitly on exit.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Log.CloseAndFlush();
    }

    static void AddOtel(IServiceCollection services, string serviceName, bool includeAspNetCore)
    {
        // Only when an OTLP endpoint is configured (in-cluster). Local dev stays logs-only.
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
            return;

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                if (includeAspNetCore) t.AddAspNetCoreInstrumentation();
                t.AddHttpClientInstrumentation().AddOtlpExporter();
            })
            .WithMetrics(m =>
            {
                if (includeAspNetCore) m.AddAspNetCoreInstrumentation();
                m.AddHttpClientInstrumentation().AddRuntimeInstrumentation().AddOtlpExporter();
            });
    }

    static string ServiceName() => Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "unknown-service";

    static Logger BuildLogger(IConfiguration config, string serviceName)
    {
        var lc = new LoggerConfiguration();
        ConfigureLogger(lc, config, serviceName);
        return lc.CreateLogger();
    }

    static void ConfigureLogger(LoggerConfiguration lc, IConfiguration config, string serviceName)
    {
        var console = (Environment.GetEnvironmentVariable("LOG_FORMAT") ?? "json")
            .Equals("console", StringComparison.OrdinalIgnoreCase);

        lc.ReadFrom.Configuration(config)          // honours the "Serilog" config section
            .Enrich.FromLogContext()
            .Enrich.With<TraceContextEnricher>()
            .Enrich.WithProperty("service.name", serviceName);

        if (console)
            lc.WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}  {Message:lj} {Properties:j}{NewLine}{Exception}");
        else
            lc.WriteTo.Console(new CompactJsonFormatter());
    }
}

/// <summary>Stamps the active OpenTelemetry trace_id/span_id onto every log event so Grafana links logs to traces.</summary>
public sealed class TraceContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent e, ILogEventPropertyFactory f)
    {
        var a = Activity.Current;
        if (a is null) return;
        e.AddPropertyIfAbsent(f.CreateProperty("trace_id", a.TraceId.ToString()));
        e.AddPropertyIfAbsent(f.CreateProperty("span_id", a.SpanId.ToString()));
    }
}
