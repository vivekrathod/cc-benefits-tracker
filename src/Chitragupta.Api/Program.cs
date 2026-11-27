using Chitragupta.Api;
using Chitragupta.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Lets "dotnet run" serve the web app in any environment. A published build carries
// the files in wwwroot and does not need it.
builder.WebHost.UseStaticWebAssets();

builder.Services.AddTracker(builder.Configuration);
builder.Services.AddHostedService<DailyWorker>();
builder.Services.AddHostedService<BalanceCheckWorker>();
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
