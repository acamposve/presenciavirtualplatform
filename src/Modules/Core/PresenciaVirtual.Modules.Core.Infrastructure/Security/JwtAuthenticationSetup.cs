using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace PresenciaVirtual.Modules.Core.Infrastructure.Security;

/// <summary>
/// Minimal JWT Bearer authentication, per ADR 0005. There is no token issuance endpoint yet;
/// this only validates tokens already issued (by a future Identity capability, or minted
/// directly for local development and tests).
/// </summary>
public static class JwtAuthenticationSetup
{
    public const string TenantIdClaimType = "tenant_id";
    public const string PermissionClaimType = "permission";

    public static IServiceCollection AddPresenciaVirtualAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var signingKey = configuration["Authentication:Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Configuration value 'Authentication:Jwt:SigningKey' is required.");
        var issuer = configuration["Authentication:Jwt:Issuer"] ?? "presenciavirtual-platform";

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // JwtSecurityTokenHandler otherwise silently renames well-known claims (e.g.
                // "sub" -> ClaimTypes.NameIdentifier) on validation. Disabling that keeps claim
                // types exactly as issued, matching TenantIdClaimType/PermissionClaimType/"sub".
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                };
            });

        return services;
    }
}
