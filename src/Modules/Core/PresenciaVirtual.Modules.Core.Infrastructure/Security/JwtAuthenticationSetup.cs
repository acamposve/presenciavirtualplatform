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

                // A correctly signed token is not necessarily a usable one: it must also carry
                // the claims CreateOrder's security requirements depend on. Rejecting it here
                // fails authentication itself (401), so HttpContextCurrentUserContext can safely
                // assume both claims are present and parsable — rather than throwing past
                // authorization and surfacing as a 500.
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var principal = context.Principal;
                        var tenantId = principal?.FindFirst(TenantIdClaimType)?.Value;
                        var userId = principal?.FindFirst("sub")?.Value;

                        if (!Guid.TryParse(tenantId, out _) || !Guid.TryParse(userId, out _))
                        {
                            context.Fail($"The token must carry a valid '{TenantIdClaimType}' claim and a valid 'sub' claim, both parsable as GUIDs.");
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }
}
