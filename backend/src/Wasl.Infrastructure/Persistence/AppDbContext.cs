using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Wasl.Application;
using Wasl.Domain;

namespace Wasl.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser) : DbContext(options), IAppDbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Stage> Stages => Set<Stage>();
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<DealStageChange> DealStageChanges => Set<DealStageChange>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<Notification> Notifications => Set<Notification>();

    /// <summary>Evaluated per query, per context instance.</summary>
    private Guid CurrentTenantId => currentUser.IsAuthenticated ? currentUser.TenantId : Guid.Empty;

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        // Everything is stored in UTC; reading it back as UTC makes the JSON carry the "Z".
        b.Properties<DateTime>().HaveConversion<UtcConverter>();
        b.Properties<DateTime?>().HaveConversion<NullableUtcConverter>();
        b.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Tenant isolation for every workspace-owned table. Paths that cross tenants on purpose
        // (login, public intake, background jobs) call IgnoreQueryFilters() explicitly.
        b.Entity<User>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        b.Entity<Company>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        b.Entity<Contact>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        b.Entity<Lead>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        b.Entity<Stage>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        b.Entity<Deal>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        b.Entity<DealStageChange>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        b.Entity<Activity>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        b.Entity<Notification>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
            {
                if (!currentUser.IsAuthenticated)
                    throw new InvalidOperationException($"{entry.Entity.GetType().Name} has no tenant.");
                entry.Entity.TenantId = currentUser.TenantId;
            }
            else if (entry.State == EntityState.Modified && entry.Property(e => e.TenantId).IsModified)
            {
                throw new InvalidOperationException("TenantId cannot change.");
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
        if (Database.CurrentTransaction is not null) return await work();

        var strategy = Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await Database.BeginTransactionAsync(ct);
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        });
    }

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue && v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
