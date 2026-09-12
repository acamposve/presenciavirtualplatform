using System.Data;

namespace PresenciaVirtual.Modules.Core.Persistence;

/// <summary>
/// Opens a database connection scoped to the current tenant (ADR 0002): the connection is
/// configured so PostgreSQL Row-Level Security policies enforce tenant isolation as a
/// defense-in-depth layer, in addition to explicit tenant filtering in application queries.
/// </summary>
public interface ITenantDbConnectionFactory
{
    Task<IDbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
