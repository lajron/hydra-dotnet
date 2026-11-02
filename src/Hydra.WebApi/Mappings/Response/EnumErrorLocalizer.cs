namespace Hydra.WebApi.Mappings.Response;

public static class EnumErrorLocalizer
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly ConcurrentDictionary<Type, ResourceManager> Managers = new();

    public static string GetMessage(Enum code, IReadOnlyList<object?> parameters, string? originalMessage = null)
    {
        var type = code.GetType();
        var manager = Managers.GetOrAdd(type, static type => new ResourceManager(
            $"{type.Namespace}.Resources.{type.Name}", type.Assembly));
        var key = code.ToString();
        var template = GetString(manager, key, CultureInfo.CurrentUICulture)
            ?? GetString(manager, key, English)
            ?? originalMessage
            ?? string.Empty;

        return parameters.Count == 0 ? template : string.Format(CultureInfo.CurrentCulture, template, [.. parameters]);
    }

    private static string? GetString(ResourceManager manager, string key, CultureInfo culture)
    {
        try
        {
            return manager.GetString(key, culture);
        }
        catch (MissingManifestResourceException)
        {
            return null;
        }
        catch (MissingSatelliteAssemblyException)
        {
            return null;
        }
    }
}
