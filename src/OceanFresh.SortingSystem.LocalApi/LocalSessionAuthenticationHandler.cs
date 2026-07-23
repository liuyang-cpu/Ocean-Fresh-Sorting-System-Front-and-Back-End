using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.LocalApi;

public sealed class LocalSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    LocalSessionManager sessionManager)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "LocalSession";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = authorization["Bearer ".Length..].Trim();
        if (!sessionManager.TryGet(token, out var session) || session is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("登录会话无效或已过期。"));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, session.UserName),
            new Claim(ClaimTypes.Name, session.UserName),
            new Claim("display_name", session.DisplayName),
            new Claim(ClaimTypes.Role, session.Role.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName)));
    }
}

internal static class AuditPrincipalExtensions
{
    public static OperationAuditActorDto ToAuditActor(this ClaimsPrincipal principal)
    {
        var userName = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? principal.Identity?.Name
                       ?? "unknown";
        var displayName = principal.FindFirstValue("display_name") ?? userName;
        var role = Enum.TryParse<UserRole>(principal.FindFirstValue(ClaimTypes.Role), out var parsedRole)
            ? parsedRole
            : UserRole.Operator;
        return new OperationAuditActorDto(userName, displayName, role);
    }
}
