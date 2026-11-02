namespace Hydra.WebApi.Configuration;

public static class LocalizationConfiguration
{
    public static IServiceCollection AddLocalizationServices(this IServiceCollection services)
    {
        services.AddLocalization();
        services.Configure<RequestLocalizationOptions>(options =>
        {
            var cultures = new[] { "en", "sr-Latn" }.Select(CultureInfo.GetCultureInfo).ToArray();
            options.DefaultRequestCulture = new RequestCulture("en");
            options.SupportedCultures = cultures;
            options.SupportedUICultures = cultures;
            options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
        });
        return services;
    }
}
