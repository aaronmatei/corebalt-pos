using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Identity;

namespace Pos.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);

        b.Property(u => u.Id).HasColumnName("id");
        b.Property(u => u.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(u => u.StoreId).HasColumnName("store_id").IsRequired();
        b.Property(u => u.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        b.Property(u => u.Username).HasColumnName("username").HasMaxLength(64).IsRequired();
        b.Property(u => u.StaffCode).HasColumnName("staff_code").HasMaxLength(32).IsRequired();
        b.Property(u => u.PinHash).HasColumnName("pin_hash").HasMaxLength(256);
        b.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(256);
        b.Property(u => u.Role).HasColumnName("role").HasConversion<int>();
        b.Property(u => u.IsActive).HasColumnName("is_active");
        b.Property(u => u.MustChangePassword).HasColumnName("must_change_password");
        b.Property(u => u.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");

        // Usernames are unique per tenant.
        b.HasIndex(u => new { u.TenantId, u.Username }).IsUnique().HasDatabaseName("ux_users_tenant_username");

        b.Ignore(u => u.DomainEvents);
    }
}
