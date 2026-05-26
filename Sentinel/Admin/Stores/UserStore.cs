using System.Text.Json;
using Sentinel.Admin.Models;
using StackExchange.Redis;

namespace Sentinel.Admin.Stores;

public class UserStore(IConnectionMultiplexer redis) : IUserStore
{
    private readonly IDatabase _db = redis.GetDatabase();
    private const string SetKey = "sentinel:users";
    private const string Prefix = "sentinel:user:";
    private const string UsernameIndex = "sentinel:user:username:";

    public async Task<List<AdminUser>> GetAllAsync()
    {
        var ids = await _db.SetMembersAsync(SetKey);
        var users = new List<AdminUser>();

        foreach (var id in ids)
        {
            var json = await _db.StringGetAsync($"{Prefix}{id}");
            if (json.IsNullOrEmpty) continue;
            var user = JsonSerializer.Deserialize<AdminUser>((string)json!);
            if (user != null) users.Add(user);
        }

        return users.OrderBy(u => u.Username).ToList();
    }

    public async Task<AdminUser?> GetByIdAsync(string id)
    {
        var json = await _db.StringGetAsync($"{Prefix}{id}");
        return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<AdminUser>((string)json!);
    }

    public async Task<AdminUser?> GetByUsernameAsync(string username)
    {
        var id = await _db.StringGetAsync($"{UsernameIndex}{username.ToLowerInvariant()}");
        return id.IsNullOrEmpty ? null : await GetByIdAsync((string)id!);
    }

    public async Task SaveAsync(AdminUser user)
    {
        var json = JsonSerializer.Serialize(user);
        await _db.StringSetAsync($"{Prefix}{user.Id}", json);
        await _db.SetAddAsync(SetKey, user.Id);
        await _db.StringSetAsync($"{UsernameIndex}{user.Username.ToLowerInvariant()}", user.Id);
    }

    public async Task DeleteAsync(string id)
    {
        var user = await GetByIdAsync(id);
        if (user == null) return;
        await _db.KeyDeleteAsync($"{Prefix}{id}");
        await _db.SetRemoveAsync(SetKey, id);
        await _db.KeyDeleteAsync($"{UsernameIndex}{user.Username.ToLowerInvariant()}");
    }

    public async Task UpdateLastLoginAsync(string id)
    {
        var user = await GetByIdAsync(id);
        if (user == null) return;
        user.LastLoginAt = DateTime.UtcNow;
        await SaveAsync(user);
    }
}
