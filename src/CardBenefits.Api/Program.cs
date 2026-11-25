using CardBenefits.Api;
using CardBenefits.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTracker(builder.Configuration);
builder.Services.AddHostedService<DailyWorker>();
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

var app = builder.Build();

await app.Services.MigrateTrackerDatabaseAsync();

// The web app is served from the same origin as the API, so there is no CORS to configure.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapHealthChecks("/health");
app.MapTrackerApi();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
