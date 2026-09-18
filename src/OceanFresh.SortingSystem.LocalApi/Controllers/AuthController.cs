using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;
using OceanFresh.SortingSystem.LocalApi;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AuthenticationService authenticationService,
    LocalSessionManager sessionManager,
    OperationAuditService auditService) : ControllerBase
{
    [HttpPost("operator-login")]
    public async Task<LoginResultDto> LoginOperator(CancellationToken cancellationToken)
    {
        var login = await authenticationService.LoginOperatorAsync(cancellationToken);
        return await CompleteLoginAsync(login, cancellationToken);
    }

    [HttpPost("admin-login")]
    public async Task<ActionResult<LoginResultDto>> LoginAdmin(
        [FromBody] AdminLoginRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var login = await authenticationService.LoginAdminAsync(request.Password, cancellationToken);
            return await CompleteLoginAsync(login, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("admin-password")]
    [Authorize(Roles = nameof(UserRole.Administrator))]
    public async Task<IActionResult> ChangeAdminPassword(
        [FromBody] ChangeAdminPasswordRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await authenticationService.ChangeAdminPasswordAsync(request, cancellationToken);
            await auditService.RecordAsync(
                User.ToAuditActor(),
                new OperationAuditWriteRequest(
                    OperationAuditCategory.Management,
                    "Management.AdminPasswordChanged",
                    "修改管理员登录密码",
                    "UserAccount",
                    User.Identity?.Name ?? "admin",
                    "管理员账号",
                    []),
                cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private async Task<LoginResultDto> CompleteLoginAsync(LoginResultDto login, CancellationToken cancellationToken)
    {
        var session = sessionManager.Issue(login);
        await auditService.RecordAsync(
            new OperationAuditActorDto(login.UserName, login.DisplayName, login.Role),
            new OperationAuditWriteRequest(
                OperationAuditCategory.Login,
                "Login.Success",
                "登录系统",
                "UserAccount",
                login.UserName,
                login.DisplayName,
                []),
            cancellationToken);
        return login with { SessionToken = session.Token, SessionExpiresAt = session.ExpiresAt };
    }
}
