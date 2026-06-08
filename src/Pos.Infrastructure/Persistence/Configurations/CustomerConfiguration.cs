using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Customers;

namespace Pos.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers");
        b.HasKey(c => c.Id);

        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(c => c.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        b.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(32);
        b.Property(c => c.Email).HasColumnName("email").HasMaxLength(160);
        b.Property(c => c.KraPin).HasColumnName("kra_pin").HasMaxLength(16);
        b.Property(c => c.NationalId).HasColumnName("national_id").HasMaxLength(16);
        b.Property(c => c.LoyaltyPoints).HasColumnName("loyalty_points");
        b.Property(c => c.IsActive).HasColumnName("is_active");
        b.Property(c => c.CreatedAtUtc).HasColumnName("created_at_utc");

        // Phone is the till's lookup key — unique per tenant when present (filtered: many walk-ins have none).
        b.HasIndex(c => new { c.TenantId, c.Phone })
            .IsUnique()
            .HasFilter("phone IS NOT NULL")
            .HasDatabaseName("ux_customers_tenant_phone");

        b.Ignore(c => c.DomainEvents);
    }
}
