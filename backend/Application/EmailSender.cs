namespace Bima.Api.Application;

public interface IEmailSender
{
    Task SendPasswordResetAsync(string email, string token, CancellationToken cancellationToken = default);
}

public sealed class DevelopmentEmailSender(ILogger<DevelopmentEmailSender> logger) : IEmailSender
{
    public Task SendPasswordResetAsync(string email, string token, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Development password reset token for {Email}: {Token}", email, token);
        return Task.CompletedTask;
    }
}

public sealed class UnconfiguredEmailSender : IEmailSender
{
    public Task SendPasswordResetAsync(string email, string token, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Password recovery email delivery is not configured.");
}
