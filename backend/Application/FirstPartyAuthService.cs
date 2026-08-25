using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Data;
using System.Text;
using Bima.Api.Data;
using Bima.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using JwtClaim = System.Security.Claims.Claim;

namespace Bima.Api.Application;

public sealed record RegisterRequest(string TenantId, string Email, string DisplayName, string Password);
public sealed record CreateUserRequest(string Email, string DisplayName, string Password, string Role);
public sealed record LoginRequest(string TenantId, string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record AuthResponse(string AccessToken, string RefreshToken, string UserId, string TenantId, string Role, int ExpiresIn);
public sealed record SessionSummary(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt);

public sealed class FirstPartyAuthService(BimaDbContext db, IConfiguration configuration, AuditService auditService)
{
    private readonly PasswordHasher<User> passwordHasher = new();

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        ValidateCredentials(request.Email, request.Password, request.TenantId, request.DisplayName);
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(user => user.TenantId == request.TenantId && user.Email == email, cancellationToken))
            throw new InvalidOperationException("A user with this email already exists for this tenant.");

        var user = new User
        {
            Id = Guid.NewGuid(), TenantId = request.TenantId.Trim(), Email = email,
            DisplayName = request.DisplayName.Trim(), PasswordHash = string.Empty,
            Role = await db.Users.AnyAsync(candidate => candidate.TenantId == request.TenantId.Trim(), cancellationToken) ? "viewer" : "admin",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(user.Id.ToString(), "user.registered", "User", user.Id.ToString(), cancellationToken: cancellationToken);
        return await CreateResponseAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.TenantId == request.TenantId.Trim() && candidate.Email == email, cancellationToken);
        if (user is null || !user.IsActive || user.LockedUntil > DateTimeOffset.UtcNow || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            if (user is not null)
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= 5)
                    user.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(15);
                await db.SaveChangesAsync(cancellationToken);
                await auditService.RecordAsync(user.Id.ToString(), "auth.login_failed", "User", user.Id.ToString(), new { user.FailedLoginAttempts }, cancellationToken);
            }
            throw new UnauthorizedAccessException("Invalid email, password, or tenant.");
        }
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(user.Id.ToString(), "auth.login_succeeded", "User", user.Id.ToString(), cancellationToken: cancellationToken);
        return await CreateResponseAsync(user, cancellationToken);
    }

    public async Task<UserSummary> CreateUserAsync(CreateUserRequest request, string tenantId, string createdBy, CancellationToken cancellationToken = default)
    {
        ValidateCredentials(request.Email, request.Password, tenantId, request.DisplayName);
        var role = request.Role.Trim().ToLowerInvariant();
        if (role is not ("admin" or "underwriter" or "agent" or "viewer")) throw new ArgumentException("Role must be admin, underwriter, agent, or viewer.");
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(user => user.TenantId == tenantId && user.Email == email, cancellationToken)) throw new InvalidOperationException("A user with this email already exists for this tenant.");
        var user = new User { Id = Guid.NewGuid(), TenantId = tenantId, Email = email, DisplayName = request.DisplayName.Trim(), PasswordHash = string.Empty, Role = role, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(createdBy, "user.created", "User", user.Id.ToString(), new { user.Email, user.Role }, cancellationToken: cancellationToken);
        return new UserSummary(user.Id, user.Email, user.DisplayName, user.Role, user.IsActive);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            throw new UnauthorizedAccessException("Invalid refresh token.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var tokenHash = HashToken(request.RefreshToken);
        var storedToken = await db.RefreshTokens.SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
        if (storedToken is null || storedToken.RevokedAt is not null || storedToken.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == storedToken.UserId && candidate.TenantId == storedToken.TenantId && candidate.IsActive, cancellationToken);
        if (user is null)
            throw new UnauthorizedAccessException("The user account is unavailable.");

        storedToken.RevokedAt = DateTimeOffset.UtcNow;
        var response = await CreateResponseAsync(user, cancellationToken);
        storedToken.ReplacedByTokenId = await db.RefreshTokens.Where(token => token.TokenHash == HashToken(response.RefreshToken)).Select(token => (Guid?)token.Id).SingleAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task RevokeAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        var token = await db.RefreshTokens.SingleOrDefaultAsync(candidate => candidate.TokenHash == HashToken(request.RefreshToken), cancellationToken);
        if (token is not null && token.RevokedAt is null)
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<SessionSummary>> GetSessionsAsync(string userId, string tenantId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(userId, out var parsedUserId))
            return [];
        return await db.RefreshTokens.AsNoTracking()
            .Where(token => token.UserId == parsedUserId && token.TenantId == tenantId)
            .OrderByDescending(token => token.CreatedAt)
            .Select(token => new SessionSummary(token.Id, token.CreatedAt, token.ExpiresAt, token.RevokedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task RevokeSessionAsync(Guid sessionId, string userId, string tenantId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(userId, out var parsedUserId))
            throw new UnauthorizedAccessException("Invalid user identity.");
        var token = await db.RefreshTokens.SingleOrDefaultAsync(candidate => candidate.Id == sessionId && candidate.UserId == parsedUserId && candidate.TenantId == tenantId, cancellationToken);
        if (token is null)
            throw new KeyNotFoundException("Session was not found.");
        token.RevokedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthResponse> CreateResponseAsync(User user, CancellationToken cancellationToken)
    {
        var jwt = configuration.GetSection("Jwt");
        var key = jwt["SigningKey"] ?? throw new InvalidOperationException("JWT signing key is not configured.");
        var claims = new[]
        {
            new JwtClaim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new JwtClaim(ClaimTypes.Email, user.Email),
            new JwtClaim(ClaimTypes.Role, user.Role),
            new JwtClaim("tenant", user.TenantId)
        };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(jwt["Issuer"], jwt["Audience"], claims, expires: DateTime.UtcNow.AddMinutes(15), signingCredentials: credentials);
        var rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = user.Id, TenantId = user.TenantId,
            TokenHash = HashToken(rawRefreshToken), ExpiresAt = DateTimeOffset.UtcNow.AddDays(30), CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        return new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), rawRefreshToken, user.Id.ToString(), user.TenantId, user.Role, 900);
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static void ValidateCredentials(string email, string password, string tenantId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 64) throw new ArgumentException("Tenant ID is required and must be 64 characters or fewer.");
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 200) throw new ArgumentException("Display name is required and must be 200 characters or fewer.");
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > 200) throw new ArgumentException("A valid email is required.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12) throw new ArgumentException("Password must be at least 12 characters.");
    }
}
