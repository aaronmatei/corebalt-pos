using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pos.Domain.Catalog;

namespace Pos.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("categories");
        b.HasKey(c => c.Id);

        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(c => c.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        b.Property(c => c.ParentId).HasColumnName("parent_id");
        b.Property(c => c.DisplayOrder).HasColumnName("display_order");
        b.Property(c => c.IsActive).HasColumnName("is_active");

        // Unique name per (tenant, parent). NULLS NOT DISTINCT (PG 15+) so two ROOTS (parent_id NULL)
        // can't share a name either — without it Postgres treats NULL parents as distinct and the
        // root-name guard would slip through. App-level checks return a clean 409; this is the backstop.
        b.HasIndex(c => new { c.TenantId, c.ParentId, c.Name })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ux_categories_tenant_parent_name");

        b.Ignore(c => c.DomainEvents);
    }
}
