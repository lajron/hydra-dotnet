var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddMediatorServices();
builder.Services.AddInfrastructureServices();
builder.Services.AddLocalizationServices();
builder.Services.AddProblemDetails();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseRequestLocalization();
app.UseExceptionHandler();

app.UseAuthorization();

app.MapControllers();

app.Run();
