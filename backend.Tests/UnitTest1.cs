namespace backend.Tests;

public class UnitTest1
{
    [Fact]
    public void Viewer_cannot_write()
    {
        var context = new Bima.Api.Application.AccessContext { Role = "viewer" };
        Assert.False(context.CanWrite);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("underwriter")]
    public void Claims_managers_can_manage_claims(string role)
    {
        var context = new Bima.Api.Application.AccessContext { Role = role };
        Assert.True(context.CanManageClaims);
    }

    [Fact]
    public void Agent_cannot_manage_claim_outcomes()
    {
        var context = new Bima.Api.Application.AccessContext { Role = "agent" };
        Assert.False(context.CanManageClaims);
    }

    [Fact]
    public void Require_administrator_rejects_non_admin()
    {
        var context = new Bima.Api.Application.AccessContext { Role = "underwriter" };
        Assert.Throws<UnauthorizedAccessException>(() => Bima.Api.Application.AccessControl.RequireAdministrator(context));
    }
}