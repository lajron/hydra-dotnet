namespace Hydra.WebApi.Tests;

// Tests inspect the HTTP action produced from an already handled Result.
#pragma warning disable RES001
public sealed class ResultHttpTests
{
    [Fact]
    public void Success_responses_use_204_and_200_unless_overridden()
    {
        Assert.Equal(204, Assert.IsAssignableFrom<StatusCodeResult>(Result.Success().ToHttpResponse()).StatusCode);
        var success = Assert.IsAssignableFrom<ObjectResult>(Result.Success("guest").ToHttpResponse());
        Assert.Equal(200, success.StatusCode);
        Assert.Equal("guest", success.Value);
        Assert.Equal(201, Assert.IsAssignableFrom<ObjectResult>(Result.Success("guest").ToHttpResponse(201)).StatusCode);
        Assert.Equal(202, Assert.IsAssignableFrom<StatusCodeResult>(Result.Success().ToHttpResponse(202)).StatusCode);
    }

    [Theory]
    [InlineData(AppErrorCategory.Validation, 400)]
    [InlineData(AppErrorCategory.Unauthorized, 401)]
    [InlineData(AppErrorCategory.Forbidden, 403)]
    [InlineData(AppErrorCategory.NotFound, 404)]
    [InlineData(AppErrorCategory.Conflict, 409)]
    [InlineData(AppErrorCategory.Internal, 500)]
    public void Every_error_category_maps_to_standard_problem_details(AppErrorCategory category, int status)
    {
        AppError error = category switch
        {
            AppErrorCategory.Validation => AppError.Validation(SampleErrors.EnglishOnly),
            AppErrorCategory.Unauthorized => AppError.Unauthorized(SampleErrors.EnglishOnly),
            AppErrorCategory.Forbidden => AppError.Forbidden(SampleErrors.EnglishOnly),
            AppErrorCategory.NotFound => AppError.NotFound(SampleErrors.EnglishOnly),
            AppErrorCategory.Conflict => AppError.Conflict(SampleErrors.EnglishOnly),
            _ => AppError.Internal(SampleErrors.EnglishOnly),
        };
        var response = Assert.IsAssignableFrom<ObjectResult>(((Result)error).ToHttpResponse());
        var problem = Assert.IsAssignableFrom<ProblemDetails>(response.Value);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(status, problem.Status);
        Assert.Equal("SampleErrors.EnglishOnly", problem.Title);
        Assert.Equal("application/problem+json", Assert.Single(response.ContentTypes));
        Assert.False(problem.Extensions.ContainsKey("code"));
        if (category == AppErrorCategory.Validation)
        {
            Assert.IsType<ValidationProblemDetails>(problem);
        }
        else
        {
            Assert.IsType<ProblemDetails>(problem);
        }
    }

    [Fact]
    public void Validation_groups_multiple_localized_field_errors_without_code_extensions()
    {
        var error = AppError.ValidationWithFields(ValidationErrorCode.InvalidRequest,
        [
            FieldValidationError.Create("Name", SampleErrors.Field, "A"),
            FieldValidationError.Create("Name", SampleErrors.Field, "B"),
            FieldValidationError.Create("Age", ValidationErrorCode.InvalidField, "GreaterThanValidator", "'Age' must be greater than '0'."),
        ]);
        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsAssignableFrom<ObjectResult>(((Result)error).ToHttpResponse()).Value);
        Assert.Equal("ValidationErrorCode.InvalidRequest", problem.Title);
        Assert.Equal("Request validation failed.", problem.Detail);
        Assert.Equal(["Field value A is invalid.", "Field value B is invalid."], problem.Errors["Name"]);
        Assert.Equal(["'Age' must be greater than '0'."], problem.Errors["Age"]);
        Assert.Empty(problem.Extensions);
    }

    [Fact]
    public void Identical_members_from_different_enum_types_have_distinct_titles()
    {
        var guest = Assert.IsType<ProblemDetails>(Assert.IsAssignableFrom<ObjectResult>(
            ((Result)AppError.NotFound(SampleErrors.Missing)).ToHttpResponse()).Value);
        var reservation = Assert.IsType<ProblemDetails>(Assert.IsAssignableFrom<ObjectResult>(
            ((Result)AppError.NotFound(ReservationErrors.Missing)).ToHttpResponse()).Value);

        Assert.Equal("SampleErrors.Missing", guest.Title);
        Assert.Equal("ReservationErrors.Missing", reservation.Title);
        Assert.NotEqual(guest.Title, reservation.Title);
    }

    [Fact]
    public void Enum_resources_come_from_defining_assembly_and_fallback_is_deterministic()
    {
        using (new CultureScope("sr-Latn"))
        {
            Assert.Contains("Gost sa ID 7", EnumErrorLocalizer.GetMessage(SampleErrors.Missing, [7, 1234.5]));
            Assert.Contains("1.234,50", EnumErrorLocalizer.GetMessage(SampleErrors.Missing, [7, 1234.5]));
            Assert.Equal("English fallback.", EnumErrorLocalizer.GetMessage(SampleErrors.EnglishOnly, []));
            Assert.Equal("Original message", EnumErrorLocalizer.GetMessage(SampleErrors.NoTranslation, [], "Original message"));
            Assert.Equal(string.Empty, EnumErrorLocalizer.GetMessage(SampleErrors.NoTranslation, []));
            Assert.Equal("Validacija zahteva nije uspela.", EnumErrorLocalizer.GetMessage(ValidationErrorCode.InvalidRequest, []));
        }
        using (new CultureScope("en"))
        {
            Assert.Contains("Guest with ID 7", EnumErrorLocalizer.GetMessage(SampleErrors.Missing, [7, 1234.5]));
            Assert.Contains("1,234.50", EnumErrorLocalizer.GetMessage(SampleErrors.Missing, [7, 1234.5]));
        }
    }

    [Fact]
    public async Task Accept_language_selects_english_and_serbian_for_real_http_responses()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers();
        builder.Services.AddLocalizationServices();
        await using var app = builder.Build();
        app.UseRequestLocalization();
        app.MapGet("/error", async context =>
        {
            Result result = AppError.NotFound(SampleErrors.Missing, 7, 1234.5);
            var response = result.ToHttpResponse();
            await response.ExecuteResultAsync(new ActionContext(context, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        });
        app.MapGet("/validation", async context =>
        {
            Result result = AppError.ValidationWithFields(ValidationErrorCode.InvalidRequest,
                [FieldValidationError.Create("Name", SampleErrors.Field, "A")]);
            var response = result.ToHttpResponse();
            await response.ExecuteResultAsync(new ActionContext(context, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        });
        var token = TestContext.Current.CancellationToken;
        await app.StartAsync(token);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var english = new HttpRequestMessage(HttpMethod.Get, "/error");
        english.Headers.AcceptLanguage.ParseAdd("en");
        using var englishResponse = await client.SendAsync(english, token);
        Assert.Equal(HttpStatusCode.NotFound, englishResponse.StatusCode);
        var en = await englishResponse.Content.ReadFromJsonAsync<JsonElement>(token);
        Assert.Equal("SampleErrors.Missing", en.GetProperty("title").GetString());
        Assert.Contains("1,234.50", en.GetProperty("detail").GetString());

        using var serbian = new HttpRequestMessage(HttpMethod.Get, "/error");
        serbian.Headers.AcceptLanguage.ParseAdd("sr-Latn");
        using var serbianResponse = await client.SendAsync(serbian, token);
        Assert.Equal(HttpStatusCode.NotFound, serbianResponse.StatusCode);
        var sr = await serbianResponse.Content.ReadFromJsonAsync<JsonElement>(token);
        Assert.Contains("Gost sa ID 7", sr.GetProperty("detail").GetString());
        Assert.Contains("1.234,50", sr.GetProperty("detail").GetString());

        using var regional = new HttpRequestMessage(HttpMethod.Get, "/error");
        regional.Headers.AcceptLanguage.ParseAdd("sr-Latn-RS");
        using var regionalResponse = await client.SendAsync(regional, token);
        var regionalBody = await regionalResponse.Content.ReadFromJsonAsync<JsonElement>(token);
        Assert.Contains("Gost sa ID 7", regionalBody.GetProperty("detail").GetString());

        using var validation = new HttpRequestMessage(HttpMethod.Get, "/validation");
        validation.Headers.AcceptLanguage.ParseAdd("sr-Latn");
        using var validationResponse = await client.SendAsync(validation, token);
        Assert.Equal(HttpStatusCode.BadRequest, validationResponse.StatusCode);
        var validationBody = await validationResponse.Content.ReadFromJsonAsync<JsonElement>(token);
        Assert.Equal("ValidationErrorCode.InvalidRequest", validationBody.GetProperty("title").GetString());
        Assert.Equal("Validacija zahteva nije uspela.", validationBody.GetProperty("detail").GetString());
        Assert.Equal("Vrednost polja A nije ispravna.", validationBody.GetProperty("errors").GetProperty("Name")[0].GetString());
        Assert.False(validationBody.TryGetProperty("errorCodes", out _));
        await app.StopAsync(token);
    }

    [Fact]
    public void FluentValidation_builtin_and_custom_messages_follow_culture()
    {
        var validator = new InlineValidator<ValidationInput>();
        validator.RuleFor(x => x.Name).NotEmpty();
        validator.RuleFor(x => x.Name).MinimumLength(3);
        validator.RuleFor(x => x.Age).Must(x => x > 0).WithErrorCode("PositiveAge")
            .WithMessage(x => CultureInfo.CurrentUICulture.Name == "sr-Latn" ? "Godine moraju biti pozitivne." : "Age must be positive.");

        using (new CultureScope("en"))
        {
            var failures = validator.Validate(new ValidationInput("", 0)).Errors;
            Assert.Equal(3, failures.Count);
            Assert.Equal("NotEmptyValidator", failures[0].ErrorCode);
            Assert.Equal("MinimumLengthValidator", failures[1].ErrorCode);
            Assert.Equal("PositiveAge", failures[2].ErrorCode);
            Assert.Contains("must not be empty", failures[0].ErrorMessage);
            Assert.Equal("Age must be positive.", failures[2].ErrorMessage);
        }
        using (new CultureScope("sr-Latn"))
        {
            var failures = validator.Validate(new ValidationInput("", 0)).Errors;
            Assert.Equal(3, failures.Count);
            Assert.Equal("NotEmptyValidator", failures[0].ErrorCode);
            Assert.Contains("prazno", failures[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Godine moraju biti pozitivne.", failures[2].ErrorMessage);
            Assert.NotEmpty(failures[1].FormattedMessagePlaceholderValues);
        }
    }

    private sealed record ValidationInput(string Name, int Age);


    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;
        private readonly CultureInfo _previousUi = CultureInfo.CurrentUICulture;

        public CultureScope(string culture)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previous;
            CultureInfo.CurrentUICulture = _previousUi;
        }
    }
}
