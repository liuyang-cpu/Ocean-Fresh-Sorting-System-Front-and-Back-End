using System.Security.Cryptography;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Application;

public sealed class AuthenticationService(IUserRepository userRepository)
{
    private const string AdminUserName = "admin";
    private const string AdminDisplayName = "管理员";
    private const string DefaultAdminPassword = "admin123";
    private const int MinimumPasswordLength = 6;

    public async Task EnsureDefaultAdminAsync(CancellationToken cancellationToken)
    {
        var admin = await userRepository.GetByUserNameAsync(AdminUserName, cancellationToken);
        if (admin is not null && !string.IsNullOrWhiteSpace(admin.PasswordHash))
        {
            return;
        }

        await userRepository.UpsertAsync((admin ?? new UserAccount(
                Guid.NewGuid(),
                AdminUserName,
                AdminDisplayName,
                UserRole.Administrator,
                true,
                string.Empty,
                null)) with
            {
                PasswordHash = PasswordHasher.Hash(DefaultAdminPassword),
                IsEnabled = true
            },
            cancellationToken);
    }

    public Task<LoginResultDto> LoginOperatorAsync(CancellationToken cancellationToken)
    {
        var loginAt = DateTimeOffset.Now;
        return Task.FromResult(new LoginResultDto("operator", "操作员", UserRole.Operator, loginAt));
    }

    public async Task<LoginResultDto> LoginAdminAsync(string password, CancellationToken cancellationToken)
    {
        await EnsureDefaultAdminAsync(cancellationToken);
        var admin = await userRepository.GetByUserNameAsync(AdminUserName, cancellationToken)
            ?? throw new InvalidOperationException("未找到管理员账号。");

        if (!admin.IsEnabled)
        {
            throw new InvalidOperationException("管理员账号已停用。");
        }

        if (!PasswordHasher.Verify(password, admin.PasswordHash))
        {
            throw new InvalidOperationException("管理员密码错误。");
        }

        var loginAt = DateTimeOffset.Now;
        await userRepository.UpsertAsync(admin with { LastLoginAt = loginAt }, cancellationToken);
        return new LoginResultDto(admin.UserName, admin.DisplayName, admin.Role, loginAt);
    }

    public async Task ChangeAdminPasswordAsync(ChangeAdminPasswordRequest request, CancellationToken cancellationToken)
    {
        await EnsureDefaultAdminAsync(cancellationToken);
        var admin = await userRepository.GetByUserNameAsync(AdminUserName, cancellationToken)
            ?? throw new InvalidOperationException("未找到管理员账号。");

        if (!PasswordHasher.Verify(request.CurrentPassword, admin.PasswordHash))
        {
            throw new InvalidOperationException("原管理员密码错误。");
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < MinimumPasswordLength)
        {
            throw new InvalidOperationException($"新密码至少需要 {MinimumPasswordLength} 位。");
        }

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("两次输入的新密码不一致。");
        }

        await userRepository.UpsertAsync(admin with
        {
            PasswordHash = PasswordHasher.Hash(request.NewPassword)
        }, cancellationToken);
    }
}

internal static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string passwordHash)
    {
        var parts = passwordHash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expectedHash = Convert.FromBase64String(parts[3]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
