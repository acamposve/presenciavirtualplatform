using PresenciaVirtual.Api.Endpoints.Restaurant;
using PresenciaVirtual.Modules.Core.Infrastructure.Migrations;
using PresenciaVirtual.Modules.Core.Infrastructure.Persistence;
using PresenciaVirtual.Modules.Core.Infrastructure.Security;
using PresenciaVirtual.Modules.Core.Persistence;
using PresenciaVirtual.Modules.Core.Security;
using PresenciaVirtual.Modules.Restaurant.Infrastructure.Menu;
using PresenciaVirtual.Modules.Restaurant.Infrastructure.Ordering;
using PresenciaVirtual.Modules.Restaurant.Infrastructure.Settings;
using PresenciaVirtual.Modules.Restaurant.Infrastructure.Tables;
using PresenciaVirtual.Modules.Restaurant.Menu;
using PresenciaVirtual.Modules.Restaurant.Menu.CreateMenuItem;
using PresenciaVirtual.Modules.Restaurant.Ordering;
using PresenciaVirtual.Modules.Restaurant.Ordering.AddItem;
using PresenciaVirtual.Modules.Restaurant.Ordering.CloseOrder;
using PresenciaVirtual.Modules.Restaurant.Ordering.CreateOrder;
using PresenciaVirtual.Modules.Restaurant.Ordering.GetOrder;
using PresenciaVirtual.Modules.Restaurant.Settings;
using PresenciaVirtual.Modules.Restaurant.Tables;
using PresenciaVirtual.Modules.Restaurant.Tables.CreateTable;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

builder.Services.AddHttpContextAccessor();
builder.Services.AddPresenciaVirtualAuthentication(builder.Configuration);
builder.Services.AddAuthorization(options => options.AddPermissionPolicies(["restaurant.orders.create", "restaurant.tables.create", "restaurant.orders.additem", "restaurant.orders.read", "restaurant.orders.close", "restaurant.menuitems.create"]));

builder.Services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>();
builder.Services.AddScoped<ITenantDbConnectionFactory, NpgsqlTenantDbConnectionFactory>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<ITableRepository, TableRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IIdempotencyStore, IdempotencyStore>();
builder.Services.AddScoped<IMenuItemRepository, MenuItemRepository>();
builder.Services.AddScoped<IOrderItemRepository, OrderItemRepository>();
builder.Services.AddScoped<IRestaurantSettingsRepository, RestaurantSettingsRepository>();
builder.Services.AddScoped<CreateOrderHandler>();
builder.Services.AddScoped<CreateTableHandler>();
builder.Services.AddScoped<AddItemHandler>();
builder.Services.AddScoped<GetOrderHandler>();
builder.Services.AddScoped<CloseOrderHandler>();
builder.Services.AddScoped<CreateMenuItemHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Runs in every environment, not just Development: a production instance starting against a
// fresh database must not be left without its schema (see Program.cs history / PR review).
// A dedicated migration step in CI/CD, run before the application starts, is the natural next
// evolution once there is a real multi-instance deployment — not needed yet.
{
    var connectionString = app.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
    var appRolePassword = app.Configuration["Database:AppRole:Password"]
        ?? throw new InvalidOperationException("Configuration value 'Database:AppRole:Password' is required.");

    SqlMigrationRunner.Run(
        connectionString,
        new Dictionary<string, string> { ["AppRolePassword"] = appRolePassword },
        typeof(SqlMigrationRunner).Assembly,
        typeof(TableRepository).Assembly);
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
app.MapRestaurantTableEndpoints();
app.MapRestaurantMenuItemEndpoints();

app.Run();

public partial class Program;
