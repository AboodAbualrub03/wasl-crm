using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wasl.Domain;
using Wasl.Infrastructure.Persistence;

namespace Wasl.Infrastructure.Jobs;

/// <summary>
/// Keeps reps honest, every few minutes, across all workspaces:
/// 1. open deals with no activity for the workspace's "stale after" days → one warning to the owner;
/// 2. tasks past their due time → one reminder to the assignee.
/// Each warning is sent once (StaleNotifiedAt / ReminderSentAt) and re-armed by new activity.
/// </summary>
public class FollowUpJob(IServiceScopeFactory scopes, ILogger<FollowUpJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private const int Batch = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var (stale, overdue) = await RunOnceAsync(stoppingToken);
                if (stale + overdue > 0)
                    logger.LogInformation("Follow-up job: {Stale} stale deals, {Overdue} overdue tasks", stale, overdue);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Follow-up job failed; retrying on the next tick");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<(int Stale, int Overdue)> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var tenants = await db.Tenants.AsNoTracking().Select(t => new { t.Id, t.StaleAfterDays }).ToListAsync(ct);
        var staleCount = 0;

        foreach (var tenant in tenants)
        {
            var cutoff = now.AddDays(-tenant.StaleAfterDays);
            var deals = await db.Deals.IgnoreQueryFilters()
                .Where(d => d.TenantId == tenant.Id && d.Status == DealStatus.Open && d.LastActivityAt < cutoff && d.StaleNotifiedAt == null)
                .Take(Batch).ToListAsync(ct);

            foreach (var deal in deals)
            {
                var days = (int)(now - deal.LastActivityAt).TotalDays;
                db.Notifications.Add(new Notification(deal.OwnerId, $"صفقة تبرد: «{deal.Title}» بلا أي نشاط منذ {days} يومًا", $"/deals/{deal.Id}")
                    { TenantId = deal.TenantId });
                deal.MarkStaleNotified();
            }
            staleCount += deals.Count;
        }

        var tasks = await db.Activities.IgnoreQueryFilters()
            .Where(a => a.Type == ActivityType.Task && a.CompletedAt == null && a.DueAt < now && a.ReminderSentAt == null)
            .OrderBy(a => a.DueAt).Take(Batch).ToListAsync(ct);

        foreach (var task in tasks)
        {
            var link = task.DealId is { } d ? $"/deals/{d}" : task.LeadId is { } l ? $"/leads/{l}" : "/tasks";
            db.Notifications.Add(new Notification(task.OwnerId, $"فات موعد المهمة: {task.Subject}", link) { TenantId = task.TenantId });
            task.MarkReminderSent();
        }

        if (staleCount + tasks.Count > 0) await db.SaveChangesAsync(ct);
        return (staleCount, tasks.Count);
    }
}
