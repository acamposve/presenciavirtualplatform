using Microsoft.AspNetCore.Http;
using PresenciaVirtual.Modules.Core.Security;

namespace PresenciaVirtual.Modules.Core.Infrastructure.Security;

/// <summary>
/// Resolves the current tenant/user/permissions from the authenticated request's claims.
/// The tenant is always read from the validated token — never from client-supplied input
/// (ADR 0002 rule 3).
/// </summary>
public sealed class HttpContextCurrentUserContext : ICurrentUserContext
{
    private readonly HashSet<string> _permissions;

    public HttpContextCurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HTTP context is available to resolve the current user.");

        var tenantIdClaim = user.FindFirst(JwtAuthenticationSetup.TenantIdClaimType)
            ?? throw new InvalidOperationException($"The authenticated token is missing the '{JwtAuthenticationSetup.TenantIdClaimType}' claim.");
        var userIdClaim = user.FindFirst("sub")
            ?? throw new InvalidOperationException("The authenticated token is missing the 'sub' claim.");

        TenantId = Guid.Parse(tenantIdClaim.Value);
        UserId = Guid.Parse(userIdClaim.Value);
        _permissions = user.FindAll(JwtAuthenticationSetup.PermissionClaimType)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    public Guid TenantId { get; }

    public Guid UserId { get; }

    public bool HasPermission(string permission) => _permissions.Contains(permission);
}
