using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasl.Domain;

namespace Wasl.Infrastructure.Persistence;

internal class TenantConfig : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.Code).HasMaxLength(40);
        b.Property(x => x.IntakeKey).HasMaxLength(64);
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.IntakeKey).IsUnique();
    }
}

internal class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FullName).HasMaxLength(150);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.PasswordHash).HasMaxLength(500);
        b.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class CompanyConfig : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.Industry).HasMaxLength(100);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.Website).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        b.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class ContactConfig : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FullName).HasMaxLength(150);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.PhoneKey).HasMaxLength(20);
        b.Property(x => x.JobTitle).HasMaxLength(100);
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        // Duplicate detection looks these up on every new lead or contact.
        b.HasIndex(x => new { x.TenantId, x.Email });
        b.HasIndex(x => new { x.TenantId, x.PhoneKey });
    }
}

internal class LeadConfig : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FullName).HasMaxLength(150);
        b.Property(x => x.CompanyName).HasMaxLength(150);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.PhoneKey).HasMaxLength(20);
        b.Property(x => x.Interest).HasMaxLength(500);
        b.Property(x => x.DisqualifyReason).HasMaxLength(300);
        b.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.Status, x.Score });
        b.HasIndex(x => new { x.TenantId, x.OwnerId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.Email });
        b.HasIndex(x => new { x.TenantId, x.PhoneKey });
    }
}

internal class StageConfig : IEntityTypeConfiguration<Stage>
{
    public void Configure(EntityTypeBuilder<Stage> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(60);
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal class DealConfig : IEntityTypeConfiguration<Deal>
{
    public void Configure(EntityTypeBuilder<Deal> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.LostReason).HasMaxLength(300);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Stage).WithMany().HasForeignKey(x => x.StageId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Contact).WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.Status, x.StageId });
        b.HasIndex(x => new { x.TenantId, x.OwnerId, x.Status });
        // The cold-deal job scans open deals by last activity.
        b.HasIndex(x => new { x.Status, x.LastActivityAt });
    }
}

internal class DealStageChangeConfig : IEntityTypeConfiguration<DealStageChange>
{
    public void Configure(EntityTypeBuilder<DealStageChange> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasOne<Deal>().WithMany().HasForeignKey(x => x.DealId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.DealId, x.At });
    }
}

internal class ActivityConfig : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Subject).HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Deal).WithMany().HasForeignKey(x => x.DealId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Contact).WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.OwnerId, x.Type, x.CompletedAt, x.DueAt });
        b.HasIndex(x => x.DealId);
        b.HasIndex(x => x.LeadId);
        b.HasIndex(x => x.ContactId);
        b.HasIndex(x => x.CompanyId);
    }
}

internal class NotificationConfig : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Title).HasMaxLength(300);
        b.Property(x => x.Link).HasMaxLength(200);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });
    }
}
