using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Appointments.Api.Auth;

public static class Roles
{
    public const string Staff = "Staff";
    public const string Admin = "Admin";
}

public static class Policies
{
    public const string Staff = "staff";
    public const string Admin = "admin";
}

public static class AuthSetup
{
    /// <summary>
    /// Production: Microsoft Entra ID access tokens (JWT), app roles from the <c>roles</c> claim.
    /// Development and tests: <see cref="DevAuthHandler"/>, which trusts two request headers and refuses to start anywhere else.
    /// </summary>
    public static IServiceCollection AddAppointmentsAuth(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var mode = config["Auth:Mode"] ?? "Entra";

        if (string.Equals(mode, "Dev", StringComparison.OrdinalIgnoreCase))
        {
            if (!env.IsDevelopment() && !env.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException("Auth:Mode=Dev is only allowed in the Development and Testing environments.");
            }

            services.AddAuthentication(DevAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, _ => { });
        }
        else
        {
            var tenantId = Required(config, "Auth:TenantId");
            var audience = Required(config, "Auth:Audience");

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
                    options.Audience = audience;
                    // Keep Entra's claim names (oid, roles, name) instead of the legacy SOAP-style URIs.
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters.NameClaimType = "name";
                    options.TokenValidationParameters.RoleClaimType = "roles";
                });
        }

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Staff, p => p.RequireRole(Roles.Staff, Roles.Admin))
            .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin));

        return services;
    }

    /// <summary>The caller's stable identity: Entra object ID, falling back to the subject.</summary>
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirst("oid")?.Value
        ?? user.FindFirst("sub")?.Value
        ?? throw new InvalidOperationException("The access token has no oid or sub claim.");

    public static bool IsStaff(this ClaimsPrincipal user) => user.IsInRole(Roles.Staff) || user.IsInRole(Roles.Admin);

    private static string Required(IConfiguration config, string key) =>
        config[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"{key} is not configured.");
}
