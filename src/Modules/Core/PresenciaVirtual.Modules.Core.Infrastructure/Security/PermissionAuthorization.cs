using Microsoft.AspNetCore.Authorization;

namespace PresenciaVirtual.Modules.Core.Infrastructure.Security;

/// <summary>
/// Registers one claims-based authorization policy per permission string (per ADR 0005 and
/// the permission naming convention in architecture.md §20, e.g. "restaurant.orders.create").
/// There is no persisted role/permission catalog yet — permissions are asserted directly as
/// claims on the token.
/// </summary>
public static class PermissionAuthorization
{
    public static AuthorizationOptions AddPermissionPolicies(this AuthorizationOptions options, IEnumerable<string> permissions)
    {
        foreach (var permission in permissions)
        {
            options.AddPolicy(permission, policy =>
                policy.RequireClaim(JwtAuthenticationSetup.PermissionClaimType, permission));
        }

        return options;
    }
}
