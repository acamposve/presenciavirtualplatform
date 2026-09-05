using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PresenciaVirtual.Modules.Core.Infrastructure.Security;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

/// <summary>
/// Mints JWTs directly for tests, per ADR 0005: there is no login endpoint yet, so tests
/// exercise the real signature/claims validation path without going through an issuance flow.
/// </summary>
public static class TestJwtTokenFactory
{
    public const string SigningKey = "integration-test-signing-key-do-not-use-in-production-0123";
    public const string Issuer = "presenciavirtual-platform-tests";

    public static string CreateToken(Guid tenantId, Guid userId, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtAuthenticationSetup.TenantIdClaimType, tenantId.ToString()),
            new("sub", userId.ToString()),
        };
        claims.AddRange(permissions.Select(p => new Claim(JwtAuthenticationSetup.PermissionClaimType, p)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
