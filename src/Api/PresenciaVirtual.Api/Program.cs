var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Liveness: the process is running. No dependency checks.
app.MapHealthChecks("/health/live");

// Readiness: the application is ready to serve traffic. Dependency checks
// (e.g. PostgreSQL) will be registered here as they are introduced.
app.MapHealthChecks("/health/ready");

// Overall health, combining the checks above.
app.MapHealthChecks("/health");

app.Run();
