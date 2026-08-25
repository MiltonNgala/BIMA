using Microsoft.EntityFrameworkCore;
using Bima.Api.Data;

namespace Bima.Api.HealthChecks;

public sealed class DatabaseHealthCheck(BimaDbContext db) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("BIMA PostgreSQL is reachable.")
                : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("BIMA PostgreSQL is unavailable.");
        }
        catch (Exception exception)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("BIMA PostgreSQL check failed.", exception);
        }
    }
}
