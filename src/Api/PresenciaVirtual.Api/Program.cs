using PresenciaVirtual.Api.Endpoints.Restaurant;
using PresenciaVirtual.Modules.Core.Infrastructure.Migrations;
using PresenciaVirtual.Modules.Core.Infrastructure.Persistence;
using PresenciaVirtual.Modules.Core.Infrastructure.Security;
using PresenciaVirtual.Modules.Core.Persistence;
using PresenciaVirtual.Modules.Core.Security;
using PresenciaVirtual.Modules.Restaurant.Infrastructure.Ordering;
using PresenciaVirtual.Modules.Restaurant.Infrastructure.Tables;
using PresenciaVirtual.Modules.Restaurant.Ordering;
using PresenciaVirtual.Modules.Restaurant.Ordering.CreateOrder;
using PresenciaVirtual.Modules.Restaurant.Tables;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

builder.Services.AddHttpContextAccessor();
builder.Services.AddPresenciaVirtualAuthentication(builder.Configuration);
builder.Services.AddAuthorization(options => options.AddPermissionPolicies(["restaurant.orders.create"]));

builder.Services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>();
builder.Services.AddScoped<ITenantDbConnectionFactory, NpgsqlTenantDbConnectionFactory>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<ITableRepository, TableRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IIdempotencyStore, IdempotencyStore>();
builder.Services.AddScoped<CreateOrderHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    var connectionString = app.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    SqlMigrationRunner.Run(connectionString, typeof(TableRepository).Assembly);
}

app.UseAuthentication();
app.UseAuthorization();

// Liveness: the process is running. No dependency checks.
app.MapHealthChecks("/health/live");

// Readiness: the application is ready to serve traffic. Dependency checks
// (e.g. PostgreSQL) will be registered here as they are introduced.
app.MapHealthChecks("/health/ready");

// Overall health, combining the checks above.
app.MapHealthChecks("/health");

app.MapRestaurantOrderEndpoints();

app.Run();

public partial class Program;
