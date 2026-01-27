using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Atlas.Helpers;

public class Env
{
    private const string DevelopmentEnvironmentName = "development";
    private const string TestEnvironmentName = "test";

    public static string EnvironmentName { get; }
    public static bool IsDevelopment { get; }
    public static bool IsTest { get; }
    private static IConfigurationRoot? _configurationRoot = null;

    static Env()
    {
        EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? DevelopmentEnvironmentName;
        IsDevelopment = EnvironmentName.ToLower() == DevelopmentEnvironmentName;
        IsTest = EnvironmentName.ToLower() == TestEnvironmentName;
        
        var entryAssem = Assembly.GetEntryAssembly();
        var fullName = entryAssem?.FullName ?? "(unknown assembly)";
        var entryVersion = entryAssem?.GetName().Version?.ToString() ?? "(unknown version)";

        Console.WriteLine($"\n*** {fullName} - {EnvironmentName}\n", fullName, entryVersion, EnvironmentName);
    }

    public static void Initialize(string[]? args = null)
    {
        var builtConfig = GetConfiguration(args);
    }

    public static IConfigurationRoot GetConfiguration(string[]? args = null)
    {
        if (Env._configurationRoot != null)
            return _configurationRoot;

        var envName = Env.EnvironmentName.ToLower();
        var configurationBuilder = new ConfigurationBuilder();
        if (File.Exists("appsettings.json"))
            configurationBuilder.AddJsonFile("appsettings.json");

        var isDevOrTest = envName is "development" or "test";
        if (isDevOrTest && File.Exists("appsettings.Development.json"))
            configurationBuilder.AddJsonFile("appsettings.Development.json");

        if (envName == "test" && File.Exists("appsettings.Test.json"))
            configurationBuilder.AddJsonFile("appsettings.Test.json");

        configurationBuilder.AddEnvironmentVariables();

        if (args is { Length: > 0 })
        {
            configurationBuilder.AddCommandLine(args);
            Console.WriteLine("Configuration built with command line args.");
        }

        _configurationRoot = configurationBuilder.Build();
        return _configurationRoot;
    }
}