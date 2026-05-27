using Sentinel.Admin.Models;
using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Admin.Stores;

public class UserStore(IFusionCache cache) : IUserStore
{
    private const string Key = "sentinel:users";

    private async Task<List<AdminUser>> LoadAsync() =>
        await cache.GetOrDefaultAsync<List<AdminUser>>(Key) ?? [];

    private Task PersistAsync(List<AdminUser> users) =>
        cache.SetAsync(Key, users, o => o.SetDuration(TimeSpan.MaxValue)).AsTask();

    public async Task<List<AdminUser>> GetAllAsync() =>
        (await LoadAsync()).OrderBy(u => u.Username).ToList();

    public async Task<AdminUser?> GetByIdAsync(string id) =>
        (await LoadAsync()).FirstOrDefault(u => u.Id == id);

    public async Task<AdminUser?> GetByUsernameAsync(string username) =>
        (await LoadAsync()).FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

    public async Task SaveAsync(AdminUser user)
    {
        var users = await LoadAsync();
        users.RemoveAll(u => u.Id == user.Id);
        users.Add(user);
        await PersistAsync(users);
    }

    public async Task DeleteAsync(string id)
    {
        var users = await LoadAsync();
        users.RemoveAll(u => u.Id == id);
        await PersistAsync(users);
    }

    public async Task UpdateLastLoginAsync(string id)
    {
        var users = await LoadAsync();
        var user = users.FirstOrDefault(u => u.Id == id);
        if (user != null) user.LastLoginAt = DateTime.UtcNow;
        await PersistAsync(users);
    }
}
