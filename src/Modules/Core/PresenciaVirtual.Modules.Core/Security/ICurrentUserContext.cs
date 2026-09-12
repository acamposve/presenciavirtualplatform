namespace PresenciaVirtual.Modules.Core.Security;

/// <summary>
/// Resolves the authenticated caller's tenant, identity, and granted permissions.
/// Implemented in infrastructure from the validated authentication context (ADR 0002 rule 2:
/// the tenant is resolved from the authenticated context, never from client-supplied input).
/// </summary>
public interface ICurrentUserContext
{
    Guid TenantId { get; }

    Guid UserId { get; }

    bool HasPermission(string permission);
}
