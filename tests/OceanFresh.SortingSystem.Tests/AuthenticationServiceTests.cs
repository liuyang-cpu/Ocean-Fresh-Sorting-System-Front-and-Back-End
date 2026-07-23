using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Tests;

public sealed class AuthenticationServiceTests
{
    [Fact]
    public async Task LoginAdminAsync_AllowsDefaultAdminPassword()
    {
        var repository = new InMemoryUserRepository();
        var service = new AuthenticationService(repository);

        var result = await service.LoginAdminAsync("admin123", CancellationToken.None);

        Assert.Equal("admin", result.UserName);
        Assert.Equal(UserRole.Administrator, result.Role);
        var admin = await repository.GetByUserNameAsync("admin", CancellationToken.None);
        Assert.NotNull(admin);
        Assert.NotEqual("admin123", admin.PasswordHash);
        Assert.NotNull(admin.LastLoginAt);
    }

    [Fact]
    public async Task LoginAdminAsync_RejectsWrongPassword()
    {
        var repository = new InMemoryUserRepository();
        var service = new AuthenticationService(repository);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LoginAdminAsync("wrong-password", CancellationToken.None));

        Assert.Equal("管理员密码错误。", ex.Message);
    }

    [Fact]
    public async Task ChangeAdminPasswordAsync_ReplacesDefaultPassword()
    {
        var repository = new InMemoryUserRepository();
        var service = new AuthenticationService(repository);

        await service.ChangeAdminPasswordAsync(
            new ChangeAdminPasswordRequest("admin123", "newpass1", "newpass1"),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LoginAdminAsync("admin123", CancellationToken.None));

        var result = await service.LoginAdminAsync("newpass1", CancellationToken.None);
        Assert.Equal(UserRole.Administrator, result.Role);
    }

    [Fact]
    public async Task ChangeAdminPasswordAsync_RejectsMismatchedConfirmation()
    {
        var repository = new InMemoryUserRepository();
        var service = new AuthenticationService(repository);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChangeAdminPasswordAsync(
                new ChangeAdminPasswordRequest("admin123", "newpass1", "newpass2"),
                CancellationToken.None));

        Assert.Equal("两次输入的新密码不一致。", ex.Message);
    }

    [Fact]
    public async Task LoginOperatorAsync_DoesNotRequirePassword()
    {
        var service = new AuthenticationService(new InMemoryUserRepository());

        var result = await service.LoginOperatorAsync(CancellationToken.None);

        Assert.Equal("operator", result.UserName);
        Assert.Equal(UserRole.Operator, result.Role);
    }

    private sealed class InMemoryUserRepository : IUserRepository
    {
        private readonly List<UserAccount> _users = [];

        public Task<IReadOnlyList<UserAccount>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserAccount>>(_users);

        public Task<UserAccount?> GetByUserNameAsync(string userName, CancellationToken cancellationToken) =>
            Task.FromResult(_users.FirstOrDefault(x =>
                string.Equals(x.UserName, userName, StringComparison.OrdinalIgnoreCase)));

        public Task<UserAccount> UpsertAsync(UserAccount userAccount, CancellationToken cancellationToken)
        {
            var index = _users.FindIndex(x => x.Id == userAccount.Id);
            if (index >= 0)
            {
                _users[index] = userAccount;
            }
            else
            {
                _users.Add(userAccount);
            }

            return Task.FromResult(userAccount);
        }
    }
}
