using System.Data;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using PresenciaVirtual.Modules.Core.Persistence;
using PresenciaVirtual.Modules.Core.Security;

namespace PresenciaVirtual.Modules.Core.Infrastructure.Persistence;

/// <summary>
/// Opens a PostgreSQL connection and sets the "app.tenant_id" session variable from the
/// current authenticated tenant, so that Row-Level Security policies can enforce tenant
/// isolation as defense-in-depth (ADR 0002). Application code must still filter every query
/// by tenant explicitly — RLS is the second layer, not a replacement for it.
///
/// RLS does not apply to a PostgreSQL superuser or a role with BYPASSRLS, which is what the
/// official Postgres image's own admin user (used to run migrations) has. This factory
/// therefore connects as the separate, least-privileged "presenciavirtual_app" role created
/// by 0000_app_role.sql, not the admin role, so RLS is actually enforced for application
/// traffic — not just declared.
/// </summary>
public sealed class NpgsqlTenantDbConnectionFactory : ITenantDbConnectionFactory
{
    public const string AppRoleUsername = "presenciavirtual_app";

    private readonly string _connectionString;
    private readonly ICurrentUserContext _currentUserContext;

    public NpgsqlTenantDbConnectionFactory(IConfiguration configuration, ICurrentUserContext currentUserContext)
    {
        var adminConnectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
        var appRolePassword = configuration["Database:AppRole:Password"]
            ?? throw new InvalidOperationException("Configuration value 'Database:AppRole:Password' is required.");

        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Username = AppRoleUsername,
            Password = appRolePassword,
        };
        _connectionString = connectionStringBuilder.ConnectionString;
        _currentUserContext = currentUserContext;
    }

    public async Task<IDbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            "SELECT set_config('app.tenant_id', @tenantId, false);",
            new { tenantId = _currentUserContext.TenantId.ToString() });

        return connection;
    }
}
