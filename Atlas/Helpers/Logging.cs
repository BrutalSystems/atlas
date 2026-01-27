using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Atlas.Helpers;

public static class Logging
{
    private static IConfigurationSection? _loggingConfiguration;

    private static IConfigurationSection? GetLoggingConfiguration(IConfiguration? configuration)
    {
        if (configuration == null)
            return null;

        if (_loggingConfiguration != null)
            return _loggingConfiguration;

        _loggingConfiguration = configuration?.GetSection("Logging");
        if (_loggingConfiguration == null)
        {
            string path = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string folder = System.IO.Path.GetDirectoryName(path) ?? "";
            string configFile = System.IO.Path.Combine(folder, "appsettings.json");
            if (File.Exists(configFile))
            {
                IConfigurationBuilder builder = new ConfigurationBuilder().AddJsonFile($"{configFile}", false, true);
                IConfigurationRoot root = builder.Build();
                _loggingConfiguration = root.GetSection("Logging");
            }
        }
        return _loggingConfiguration;
    }

    private static readonly object _sharedFactoryLock = new();
    private static ILoggerFactory? _sharedLoggerFactory;
    private static bool _processExitHandlerRegistered;

    private static void ConfigureBuilder(ILoggingBuilder builder)
    {
        builder.AddConsoleFormatter<ConsoleFormatter, Microsoft.Extensions.Logging.Console.ConsoleFormatterOptions>()
            .AddConsole(options =>
            {
                options.FormatterName = "Atlas Log Formatter";
            });
    }

    private static ILoggerFactory CreateNewLoggerFactory(IConfiguration? configuration)
    {
        var loggingConfiguration = GetLoggingConfiguration(configuration);
        ILoggerFactory loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            if (loggingConfiguration != null)
            {
                builder.AddConfiguration(loggingConfiguration);
            }
            ConfigureBuilder(builder);
        });
        return loggerFactory;
    }

    private static void EnsureProcessExitDisposesSharedFactory()
    {
        if (_processExitHandlerRegistered)
            return;

        _processExitHandlerRegistered = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                DisposeSharedLoggerFactory();
            }
            catch
            {
                // Best-effort only.
            }
        };
    }

    public static void DisposeSharedLoggerFactory()
    {
        lock (_sharedFactoryLock)
        {
            _sharedLoggerFactory?.Dispose();
            _sharedLoggerFactory = null;
        }
    }

    public static ILoggerFactory GetSharedLoggerFactory(IConfiguration? configuration = null)
    {
        configuration ??= Env.GetConfiguration();

        lock (_sharedFactoryLock)
        {
            if (_sharedLoggerFactory == null)
            {
                _sharedLoggerFactory = CreateNewLoggerFactory(configuration);
                EnsureProcessExitDisposesSharedFactory();
            }

            return _sharedLoggerFactory;
        }
    }

    public static ILoggerFactory CreateLoggerFactory(IConfiguration? configuration = null)
    {
        // Backwards-compatible entrypoint; now returns a shared factory to avoid allocating
        // a new logger provider graph per call.
        return GetSharedLoggerFactory(configuration);
    }

    public static ILogger CreateLogger(string? name = null, IConfiguration? configuration = null)
    {
        configuration ??= Env.GetConfiguration();
        ILoggerFactory loggerFactory = GetSharedLoggerFactory(configuration);
        name ??= Assembly.GetExecutingAssembly()?.GetName().FullName.Split(".")[0] ?? nameof(Env);

        // Get the type of the calling method's owner
        // var stackTrace = new System.Diagnostics.StackTrace();
        // var callingMethod = stackTrace.GetFrame(1)?.GetMethod();
        // var callingType = callingMethod?.DeclaringType;
        // var name = callingType?.FullName ?? Assembly.GetExecutingAssembly()?.GetName().FullName.Split(".")[0] ?? nameof(Env);

        var logger = loggerFactory.CreateLogger(name);
        return logger;
    }

    public static ILogger<T> CreateLogger<T>(IConfiguration? configuration = null)
    {
        configuration ??= Env.GetConfiguration();
        ILoggerFactory loggerFactory = GetSharedLoggerFactory(configuration);
        var logger = loggerFactory.CreateLogger<T>();
        return logger;
    }

    public static ILogger CreateLogger(Type type, IConfiguration? configuration = null)
    {
        configuration ??= Env.GetConfiguration();
        ILoggerFactory loggerFactory = GetSharedLoggerFactory(configuration);
        var logger = loggerFactory.CreateLogger(type);
        return logger;
    }
}