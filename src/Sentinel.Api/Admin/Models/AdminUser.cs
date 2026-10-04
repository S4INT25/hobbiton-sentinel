namespace Sentinel.Admin.Models;

public class AdminUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "analyst"; // admin, analyst, developer
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Set for Google sign-ins with no existing account; cleared when an admin activates the user.
    public bool PendingApproval { get; set; }
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiry { get; set; }

    // Legacy self-signup accounts may be unverified; Google sign-in marks them verified.
    public bool EmailVerified { get; set; } = true;

    public bool TwoFactorEnabled { get; set; }
    public string? TwoFactorSecret { get; set; }
}