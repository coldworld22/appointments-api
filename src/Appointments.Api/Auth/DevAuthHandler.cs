using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Auth;

/// <summary>
/// Local development and integration tests only (see <see cref="AuthSetup"/>):
/// <c>X-Dev-User: citizen-1</c> signs in as that user, <c>X-Dev-Roles: Staff,Admin</c> adds roles.
/// It produces the same claim shapes as an Entra token, so the rest of the app can't tell the difference.
/// </summary>
public sealed class DevAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Dev";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers["X-Dev-User"].ToString();
        if (string.IsNullOrWhiteSpace(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("oid", user), new("name", user) };
        var roles = Request.Headers["X-Dev-Roles"].ToString();
        foreach (var role in roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim("roles", role));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, nameType: "name", roleType: "roles");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
