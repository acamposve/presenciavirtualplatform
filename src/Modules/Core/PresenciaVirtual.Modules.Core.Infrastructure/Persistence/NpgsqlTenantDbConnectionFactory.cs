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
/// </summary>
public sealed class NpgsqlTenantDbConnectionFactory : ITenantDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly ICurrentUserContext _currentUserContext;

    public NpgsqlTenantDbConnectionFactory(IConfiguration configuration, ICurrentUserContext currentUserContext)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
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
