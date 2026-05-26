using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class InMemoryUserStore : IUserStore
{
    private readonly List<AdminUser> _users = [];

    public Task<List<AdminUser>> GetAllAsync() =>
        Task.FromResult(_users.OrderBy(u => u.Username).ToList());

    public Task<AdminUser?> GetByIdAsync(string id) =>
        Task.FromResult(_users.FirstOrDefault(u => u.Id == id));

    public Task<AdminUser?> GetByUsernameAsync(string username) =>
        Task.FromResult(_users.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)));

    public Task SaveAsync(AdminUser user)
    {
        _users.RemoveAll(u => u.Id == user.Id);
        _users.Add(user);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id) { _users.RemoveAll(u => u.Id == id); return Task.CompletedTask; }

    public Task UpdateLastLoginAsync(string id)
    {
        var user = _users.FirstOrDefault(u => u.Id == id);
        if (user != null) user.LastLoginAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }
}
