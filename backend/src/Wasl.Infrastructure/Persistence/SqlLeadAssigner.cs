using Microsoft.EntityFrameworkCore;
using Wasl.Application;
using Wasl.Domain;

namespace Wasl.Infrastructure.Persistence;

/// <summary>
/// Fair lead routing: the next lead goes to whoever has the fewest open leads; ties go to
/// whoever has waited longest since their last one.
///
/// Without a lock, two leads arriving together would both read the same counts and land on the
/// same person. An application lock (sp_getapplock) per workspace, held until the transaction
/// commits, makes the read-decide-write sequence one at a time without locking any table.
/// </summary>
public class SqlLeadAssigner(AppDbContext db) : ILeadAssigner
{
    public async Task<User?> PickOwnerAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Lead assignment must run inside a transaction.");

        var resource = $"lead-assign:{tenantId}";
        await db.Database.ExecuteSqlAsync($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @result < 0 THROW 50001, 'Could not acquire the lead assignment lock.', 1;
            """, ct);

        var candidates = await db.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.IsActive && u.ReceivesLeads)
            .Select(u => new
            {
                User = u,
                Open = db.Leads.IgnoreQueryFilters().Count(l => l.OwnerId == u.Id
                    && (l.Status == LeadStatus.New || l.Status == LeadStatus.Contacted || l.Status == LeadStatus.Qualified))
            })
            .ToListAsync(ct);

        return candidates
            .OrderBy(c => c.Open)
            .ThenBy(c => c.User.LastLeadAssignedAt ?? DateTime.MinValue)
            .Select(c => c.User)
            .FirstOrDefault();
    }
}
