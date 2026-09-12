using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using PresenciaVirtual.Modules.Core.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

/// <summary>
/// Boots the real Api host (including its own startup migrations) against an ephemeral
/// PostgreSQL container, so integration tests exercise the actual HTTP pipeline, database
/// schema, and Row-Level Security policies rather than a mocked stand-in.
///
/// Configuration is passed via environment variables, set before the host is first built,
/// rather than WebApplicationFactory's ConfigureAppConfiguration hook: Program.cs reads
/// configuration synchronously at startup (before authentication options are needed), so the
/// override must be visible from the very first configuration source, exactly like it would
/// be in a real deployment.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AppRolePassword = "integration-test-app-role-password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("presenciavirtual_test")
        .WithUsername("presenciavirtual")
        .WithPassword("presenciavirtual")
        .Build();

    /// <summary>Admin/superuser connection — bypasses RLS. Use only for test setup (seeding), never to exercise the behavior under test.</summary>
    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>The same least-privilege role the application itself connects as, for tests that need to prove RLS independently of application-level filtering.</summary>
    public string AppRoleConnectionString => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
    {
        Username = NpgsqlTenantDbConnectionFactory.AppRoleUsername,
        Password = AppRolePassword,
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Authentication__Jwt__SigningKey", TestJwtTokenFactory.SigningKey);
        Environment.SetEnvironmentVariable("Authentication__Jwt__Issuer", TestJwtTokenFactory.Issuer);
        Environment.SetEnvironmentVariable("Database__AppRole__Password", AppRolePassword);

        // WebApplicationFactory builds the host lazily on first use. Force it here so startup
        // migrations have already run before any test seeds data directly via SQL.
        _ = Server;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
