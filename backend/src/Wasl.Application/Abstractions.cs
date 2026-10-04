using Microsoft.EntityFrameworkCore;
using Wasl.Domain;

namespace Wasl.Application;

public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<User> Users { get; }
    DbSet<Company> Companies { get; }
    DbSet<Contact> Contacts { get; }
    DbSet<Lead> Leads { get; }
    DbSet<Stage> Stages { get; }
    DbSet<Deal> Deals { get; }
    DbSet<DealStageChange> DealStageChanges { get; }
    DbSet<Activity> Activities { get; }
    DbSet<Notification> Notifications { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Runs the work in one database transaction.</summary>
    Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default);
}

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    Guid TenantId { get; }
    UserRole Role { get; }
}

/// <summary>Picks who receives the next lead. Must be called inside a transaction.</summary>
public interface ILeadAssigner
{
    Task<User?> PickOwnerAsync(Guid tenantId, CancellationToken ct = default);
}

public interface IPasswordHasher
{
    string Hash(User user, string password);
    bool Verify(User user, string password);
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) Issue(User user);
}

public class NotFoundException(string message = "العنصر غير موجود.") : Exception(message);

public class ForbiddenException(string message = "لا تملك صلاحية لهذا الإجراء.") : Exception(message);

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public record PersonRef(Guid Id, string FullName);

public record Ref(Guid Id, string Name);

public static class QueryExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<T>(items, total, page, pageSize);
    }

    /// <summary>Reps work their own pipeline; managers and admins see the whole team.</summary>
    public static IQueryable<Deal> VisibleTo(this IQueryable<Deal> deals, ICurrentUser me) =>
        me.Role == UserRole.Rep ? deals.Where(d => d.OwnerId == me.UserId) : deals;

    /// <summary>Reps see their own leads plus the unassigned pool.</summary>
    public static IQueryable<Lead> VisibleTo(this IQueryable<Lead> leads, ICurrentUser me) =>
        me.Role == UserRole.Rep ? leads.Where(l => l.OwnerId == me.UserId || l.OwnerId == null) : leads;

    public static bool IsManager(this ICurrentUser me) => me.Role is UserRole.Manager or UserRole.Admin;
}
