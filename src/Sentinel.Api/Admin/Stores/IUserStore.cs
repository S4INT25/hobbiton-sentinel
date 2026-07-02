using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public interface IUserStore
{
    Task<AdminUser?> GetByUsernameAsync(string username);
    Task<AdminUser?> GetByIdAsync(string id);
    Task<List<AdminUser>> GetAllAsync();
    Task SaveAsync(AdminUser user);
    Task DeleteAsync(string id);
    Task UpdateLastLoginAsync(string id);
    Task<AdminUser?> GetByEmailAsync(string email);
    Task<AdminUser?> GetByResetTokenAsync(string token);
}