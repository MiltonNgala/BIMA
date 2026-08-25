using System.Security.Cryptography;
using System.Text;
using Bima.Api.Data;
using Bima.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Bima.Api.Application;

public sealed record PasswordResetRequest(string TenantId, string Email);
public sealed record PasswordResetConfirmation(string Token, string NewPassword);

public sealed class PasswordRecoveryService(BimaDbContext db, AuditService auditService, IEmailSender emailSender)
{
    private readonly PasswordHasher<User> passwordHasher = new();

    public async Task<string?> RequestResetAsync(PasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = request.TenantId.Trim();
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.Email == email && candidate.IsActive, cancellationToken);
        if (user is null)
            return null;

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(), UserId = user.Id, TenantId = user.TenantId,
            TokenHash = HashToken(rawToken), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30), CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(user.Id.ToString(), "auth.password_reset_requested", "User", user.Id.ToString(), cancellationToken: cancellationToken);
        await emailSender.SendPasswordResetAsync(user.Email, rawToken, cancellationToken);
        return rawToken;
    }

    public async Task ResetPasswordAsync(PasswordResetConfirmation request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 12)
            throw new ArgumentException("Password must be at least 12 characters.");

        var token = await db.PasswordResetTokens.SingleOrDefaultAsync(candidate => candidate.TokenHash == HashToken(request.Token), cancellationToken);
        if (token is null || token.UsedAt is not null || token.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("Invalid or expired password reset token.");

        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == token.UserId && candidate.TenantId == token.TenantId && candidate.IsActive, cancellationToken)
            ?? throw new UnauthorizedAccessException("The user account is unavailable.");
        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        token.UsedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(user.Id.ToString(), "auth.password_reset_completed", "User", user.Id.ToString(), cancellationToken: cancellationToken);
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
