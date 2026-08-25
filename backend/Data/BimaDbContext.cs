using Bima.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bima.Api.Data;

public sealed class BimaDbContext(DbContextOptions<BimaDbContext> options) : DbContext(options)
{
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ClaimAttachment> ClaimAttachments => Set<ClaimAttachment>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Policy>(entity =>
        {
            entity.HasKey(policy => policy.Id);
            entity.HasIndex(policy => new { policy.TenantId, policy.Number }).IsUnique();
            entity.HasOne(policy => policy.CustomerEntity).WithMany().HasForeignKey(policy => policy.CustomerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(policy => new { policy.TenantId, policy.CustomerId });
            entity.Property(policy => policy.Premium).HasPrecision(18, 2);
            entity.Property(policy => policy.Number).HasMaxLength(32).IsRequired();
            entity.Property(policy => policy.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(policy => policy.Customer).HasMaxLength(200).IsRequired();
            entity.Property(policy => policy.Product).HasMaxLength(120).IsRequired();
            entity.Property(policy => policy.Status).HasMaxLength(40).IsRequired();
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(customer => customer.Id);
            entity.HasIndex(customer => new { customer.TenantId, customer.Email }).IsUnique();
            entity.Property(customer => customer.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(customer => customer.Name).HasMaxLength(200).IsRequired();
            entity.Property(customer => customer.Email).HasMaxLength(200).IsRequired();
            entity.Property(customer => customer.Phone).HasMaxLength(40).IsRequired();
            entity.Property(customer => customer.CustomerType).HasMaxLength(20).IsRequired();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.HasIndex(user => new { user.TenantId, user.Email }).IsUnique();
            entity.Property(user => user.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(user => user.Email).HasMaxLength(200).IsRequired();
            entity.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(user => user.PasswordHash).IsRequired();
            entity.Property(user => user.Role).HasMaxLength(30).IsRequired();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(token => token.Id);
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => new { token.UserId, token.TenantId });
            entity.Property(token => token.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(audit => audit.Id);
            entity.HasIndex(audit => new { audit.TenantId, audit.CreatedAt });
            entity.Property(audit => audit.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(audit => audit.UserId).HasMaxLength(128).IsRequired();
            entity.Property(audit => audit.Action).HasMaxLength(80).IsRequired();
            entity.Property(audit => audit.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(audit => audit.EntityId).HasMaxLength(128);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(token => token.Id);
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => new { token.UserId, token.TenantId });
            entity.Property(token => token.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<Claim>(entity =>
        {
            entity.HasKey(claim => claim.Id);
            entity.HasIndex(claim => new { claim.TenantId, claim.ClaimNumber }).IsUnique();
            entity.HasIndex(claim => new { claim.TenantId, claim.PolicyNumber });
            entity.Property(claim => claim.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(claim => claim.ClaimNumber).HasMaxLength(32).IsRequired();
            entity.Property(claim => claim.PolicyNumber).HasMaxLength(32).IsRequired();
            entity.Property(claim => claim.Customer).HasMaxLength(200).IsRequired();
            entity.Property(claim => claim.Description).HasMaxLength(2000).IsRequired();
            entity.Property(claim => claim.Status).HasMaxLength(30).IsRequired();
            entity.Property(claim => claim.ReserveAmount).HasPrecision(18, 2);
            entity.Property(claim => claim.PaidAmount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(invoice => invoice.Id);
            entity.HasIndex(invoice => new { invoice.TenantId, invoice.InvoiceNumber }).IsUnique();
            entity.HasIndex(invoice => new { invoice.TenantId, invoice.PolicyNumber });
            entity.Property(invoice => invoice.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(invoice => invoice.InvoiceNumber).HasMaxLength(32).IsRequired();
            entity.Property(invoice => invoice.PolicyNumber).HasMaxLength(32).IsRequired();
            entity.Property(invoice => invoice.Amount).HasPrecision(18, 2);
            entity.Property(invoice => invoice.PaidAmount).HasPrecision(18, 2);
            entity.Property(invoice => invoice.Status).HasMaxLength(30).IsRequired();
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(payment => payment.Id);
            entity.HasOne<Invoice>().WithMany().HasForeignKey(payment => payment.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(payment => new { payment.TenantId, payment.Reference }).IsUnique();
            entity.Property(payment => payment.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(payment => payment.Reference).HasMaxLength(100).IsRequired();
            entity.Property(payment => payment.Amount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<ClaimAttachment>(entity =>
        {
            entity.HasKey(attachment => attachment.Id);
            entity.HasOne<Claim>().WithMany().HasForeignKey(attachment => attachment.ClaimId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(attachment => new { attachment.TenantId, attachment.ClaimId });
            entity.Property(attachment => attachment.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(attachment => attachment.FileName).HasMaxLength(255).IsRequired();
            entity.Property(attachment => attachment.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(attachment => attachment.StoragePath).HasMaxLength(500).IsRequired();
        });

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.HasKey(organization => organization.TenantId);
            entity.Property(organization => organization.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(organization => organization.Name).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<UserPermission>(entity =>
        {
            entity.HasKey(permission => new { permission.TenantId, permission.UserId, permission.Permission });
            entity.Property(permission => permission.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(permission => permission.Permission).HasMaxLength(80).IsRequired();
            entity.HasIndex(permission => new { permission.TenantId, permission.UserId });
        });
    }
}
