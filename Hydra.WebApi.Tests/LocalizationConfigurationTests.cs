using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Hydra.WebApi.Tests;

public sealed class LocalizationConfigurationTests
{
    [Fact]
    public void Registers_localization_services_and_request_culture_options()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Assert.Same(services, services.AddLocalizationConfiguration());

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IStringLocalizerFactory>());

        var options = provider.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
        Assert.Equal("en", options.DefaultRequestCulture.Culture.Name);
        Assert.Equal("en", options.DefaultRequestCulture.UICulture.Name);
        Assert.Equal(["en", "sr-Latn"], options.SupportedCultures!.Select(culture => culture.Name));
        Assert.Equal(["en", "sr-Latn"], options.SupportedUICultures!.Select(culture => culture.Name));
        Assert.IsType<AcceptLanguageHeaderRequestCultureProvider>(Assert.Single(options.RequestCultureProviders));
    }
}
