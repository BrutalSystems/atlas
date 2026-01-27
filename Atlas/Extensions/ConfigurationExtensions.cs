namespace Atlas.Extensions;

public static class ConfigurationExtensions
{
    public static TSettings GetSettings<TSettings>(this IConfiguration configuration) where TSettings : new()
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));
        var s = typeof(TSettings).Name;
        return configuration.GetSection(typeof(TSettings).Name).Get<TSettings>() ?? new TSettings();
    }
}