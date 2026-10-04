using Microsoft.EntityFrameworkCore;
using Wasl.Application.Activities;
using Wasl.Application.Leads;
using Wasl.Domain;

namespace Wasl.Application.Insights;

public record ForecastMonth(int Year, int Month, decimal Won, decimal Weighted, decimal BestCase);

public record FunnelStep(string Stage, int Reached, double Rate);

public record RepRow(PersonRef Rep, decimal WonValue, int WonCount, decimal Pipeline, int Activities, double? WinRate);

public record SourceRow(LeadSource Source, int Leads, int Converted, double Rate);

public record ReasonRow(string Reason, int Count);

public record ReportDto(
    decimal WonThisQuarter,
    double? WinRate,
    decimal AvgDealSize,
    double? AvgCycleDays,
    IReadOnlyList<ForecastMonth> Forecast,
    IReadOnlyList<FunnelStep> Funnel,
    IReadOnlyList<RepRow> Reps,
    IReadOnlyList<SourceRow> Sources,
    IReadOnlyList<ReasonRow> LostReasons);

public record StaleDeal(Guid Id, string Title, decimal Value, string? CompanyName, int DaysSilent);

public record HomeDto(
    string FirstName,
    int OpenDeals,
    decimal OpenValue,
    decimal Weighted,
    decimal WonThisMonth,
    int OverdueCount,
    IReadOnlyList<ActivityDto> Agenda,
    IReadOnlyList<StaleDeal> StaleDeals,
    IReadOnlyList<LeadListItem> HotLeads);

public record SearchHit(string Kind, Guid Id, string Title, string? Subtitle);

public class ReportService(IAppDbContext db, ICurrentUser me)
{
    /// <summary>
    /// Builds every report from the deals the user may see. Reps get their own numbers;
    /// managers get the team. Data volumes per workspace are small, so the shaping is done in memory
    /// after one query per source.
    /// </summary>
    public async Task<ReportDto> GetAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var quarterStart = new DateTime(now.Year, (now.Month - 1) / 3 * 3 + 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var since = now.AddDays(-365);

        var stages = await db.Stages.AsNoTracking().OrderBy(s => s.Order).ToListAsync(ct);
        var deals = await db.Deals.AsNoTracking().VisibleTo(me)
            .Where(d => d.Status == DealStatus.Open || d.ClosedAt >= since || d.CreatedAt >= since)
            .Select(d => new { d.Id, d.Value, d.Status, d.StageId, d.OwnerId, d.ExpectedClose, d.CreatedAt, d.ClosedAt, d.LostReason, Probability = d.Stage.Probability })
            .ToListAsync(ct);

        // Forecast: two months back (actual wins) and four ahead (open deals by expected close).
        var forecast = Enumerable.Range(-2, 6).Select(i => monthStart.AddMonths(i)).Select(m =>
        {
            var next = m.AddMonths(1);
            var open = deals.Where(d => d.Status == DealStatus.Open && d.ExpectedClose >= m && d.ExpectedClose < next).ToList();
            return new ForecastMonth(m.Year, m.Month,
                deals.Where(d => d.Status == DealStatus.Won && d.ClosedAt >= m && d.ClosedAt < next).Sum(d => d.Value),
                Math.Round(open.Sum(d => d.Value * d.Probability / 100m), 0),
                open.Sum(d => d.Value));
        }).ToList();

        var closed90 = deals.Where(d => d.ClosedAt >= now.AddDays(-90)).ToList();
        var won90 = closed90.Count(d => d.Status == DealStatus.Won);
        var wonAll = deals.Where(d => d.Status == DealStatus.Won).ToList();

        // Funnel: how far each deal got. The furthest open stage it ever entered counts; a won deal passed them all.
        var openStages = stages.Where(s => s.Kind == StageKind.Open).ToList();
        var order = stages.ToDictionary(s => s.Id, s => s.Order);
        var recentIds = deals.Where(d => d.CreatedAt >= now.AddDays(-180)).Select(d => d.Id).ToList();
        var furthest = await db.DealStageChanges.AsNoTracking()
            .Where(c => recentIds.Contains(c.DealId))
            .Select(c => new { c.DealId, c.ToStageId })
            .ToListAsync(ct);
        var maxOrder = recentIds.ToDictionary(id => id, id =>
        {
            var deal = deals.First(d => d.Id == id);
            if (deal.Status == DealStatus.Won) return int.MaxValue;
            return furthest.Where(f => f.DealId == id && openStages.Any(s => s.Id == f.ToStageId))
                .Select(f => order[f.ToStageId]).DefaultIfEmpty(order[deal.StageId]).Max();
        });
        var started = Math.Max(1, recentIds.Count);
        var funnel = openStages.Select(s =>
        {
            var reached = maxOrder.Values.Count(o => o >= s.Order);
            return new FunnelStep(s.Name, reached, Math.Round(reached * 100.0 / started, 1));
        }).ToList();
        funnel.Add(new FunnelStep(stages.First(s => s.Kind == StageKind.Won).Name,
            maxOrder.Values.Count(o => o == int.MaxValue),
            Math.Round(maxOrder.Values.Count(o => o == int.MaxValue) * 100.0 / started, 1)));

        // Leaderboard (managers see everyone; reps see only their row).
        // Everyone who sells: reps, plus any manager who owns deals themselves.
        var people = (await db.Users.AsNoTracking().Where(u => u.IsActive && (me.IsManager() || u.Id == me.UserId))
                .Select(u => new { u.Id, u.FullName, u.Role }).ToListAsync(ct))
            .Where(u => u.Role == UserRole.Rep || deals.Any(d => d.OwnerId == u.Id))
            .ToList();
        var activityCounts = await db.Activities.AsNoTracking()
            .Where(a => a.CreatedAt >= now.AddDays(-30) && a.Type != ActivityType.System)
            .GroupBy(a => a.OwnerId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var reps = people.Select(p =>
        {
            var mine = deals.Where(d => d.OwnerId == p.Id).ToList();
            var wonQ = mine.Where(d => d.Status == DealStatus.Won && d.ClosedAt >= quarterStart).ToList();
            var closed = mine.Count(d => d.ClosedAt >= now.AddDays(-90));
            var won = mine.Count(d => d.Status == DealStatus.Won && d.ClosedAt >= now.AddDays(-90));
            return new RepRow(new PersonRef(p.Id, p.FullName), wonQ.Sum(d => d.Value), wonQ.Count,
                Math.Round(mine.Where(d => d.Status == DealStatus.Open).Sum(d => d.Value * d.Probability / 100m), 0),
                activityCounts.FirstOrDefault(a => a.Key == p.Id)?.Count ?? 0,
                closed == 0 ? null : Math.Round(won * 100.0 / closed, 0));
        }).OrderByDescending(r => r.WonValue).ThenByDescending(r => r.Pipeline).ToList();

        var leads = await db.Leads.AsNoTracking().VisibleTo(me).Where(l => l.CreatedAt >= now.AddDays(-180))
            .GroupBy(l => l.Source)
            .Select(g => new { g.Key, Total = g.Count(), Converted = g.Count(l => l.Status == LeadStatus.Converted) })
            .ToListAsync(ct);
        var sources = leads.Select(l => new SourceRow(l.Key, l.Total, l.Converted, Math.Round(l.Converted * 100.0 / l.Total, 0)))
            .OrderByDescending(s => s.Leads).ToList();

        var reasons = deals.Where(d => d.Status == DealStatus.Lost && d.LostReason != null)
            .GroupBy(d => d.LostReason!).Select(g => new ReasonRow(g.Key, g.Count()))
            .OrderByDescending(r => r.Count).Take(6).ToList();

        return new ReportDto(
            deals.Where(d => d.Status == DealStatus.Won && d.ClosedAt >= quarterStart).Sum(d => d.Value),
            closed90.Count == 0 ? null : Math.Round(won90 * 100.0 / closed90.Count, 0),
            wonAll.Count == 0 ? 0 : Math.Round(wonAll.Average(d => d.Value), 0),
            wonAll.Count == 0 ? null : Math.Round(wonAll.Average(d => (d.ClosedAt!.Value - d.CreatedAt).TotalDays), 0),
            forecast, funnel, reps, sources, reasons);
    }
}

public class HomeService(IAppDbContext db, ICurrentUser me)
{
    public async Task<HomeDto> GetAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endOfToday = now.Date.AddDays(1);
        var staleDays = await db.Tenants.Where(t => t.Id == me.TenantId).Select(t => t.StaleAfterDays).FirstAsync(ct);
        var staleBefore = now.AddDays(-staleDays);

        // Personal view: my deals even for managers, so the home page is "my day".
        var mine = db.Deals.AsNoTracking().Where(d => d.OwnerId == me.UserId);
        var open = await mine.Where(d => d.Status == DealStatus.Open)
            .Select(d => new { d.Value, d.Stage.Probability }).ToListAsync(ct);

        var tasks = db.Activities.Where(a => a.OwnerId == me.UserId && a.Type == ActivityType.Task && a.CompletedAt == null);
        var agenda = await ActivityService.Project(tasks.Where(a => a.DueAt < endOfToday).OrderBy(a => a.DueAt).Take(12), now).ToListAsync(ct);

        var stale = (await mine.Where(d => d.Status == DealStatus.Open && d.LastActivityAt < staleBefore)
                .OrderBy(d => d.LastActivityAt).Take(5)
                .Select(d => new { d.Id, d.Title, d.Value, Company = d.Company != null ? d.Company.Name : null, d.LastActivityAt })
                .ToListAsync(ct))
            .Select(d => new StaleDeal(d.Id, d.Title, d.Value, d.Company, (int)(now - d.LastActivityAt).TotalDays))
            .ToList();

        var hot = await db.Leads.AsNoTracking()
            .Where(l => l.OwnerId == me.UserId && (l.Status == LeadStatus.New || l.Status == LeadStatus.Contacted || l.Status == LeadStatus.Qualified))
            .OrderByDescending(l => l.Score).Take(5)
            .Select(l => new LeadListItem(l.Id, l.FullName, l.CompanyName, l.Email, l.Phone, l.Source, l.Status, l.Score,
                null, l.EstimatedValue, l.CreatedAt, l.LastContactedAt))
            .ToListAsync(ct);

        var name = await db.Users.Where(u => u.Id == me.UserId).Select(u => u.FullName).FirstAsync(ct);

        return new HomeDto(
            name.Split(' ')[0],
            open.Count,
            open.Sum(d => d.Value),
            Math.Round(open.Sum(d => d.Value * d.Probability / 100m), 0),
            await mine.Where(d => d.Status == DealStatus.Won && d.ClosedAt >= monthStart).SumAsync(d => (decimal?)d.Value, ct) ?? 0,
            await tasks.CountAsync(a => a.DueAt < now.Date, ct),
            agenda, stale, hot);
    }
}

public class SearchService(IAppDbContext db, ICurrentUser me)
{
    public async Task<List<SearchHit>> SearchAsync(string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return [];
        var s = q.Trim();
        var phone = Text.NormalizePhone(s);

        var deals = await db.Deals.AsNoTracking().VisibleTo(me)
            .Where(d => d.Title.Contains(s) || (d.Company != null && d.Company.Name.Contains(s)))
            .OrderByDescending(d => d.Status == DealStatus.Open).Take(5)
            .Select(d => new SearchHit("deal", d.Id, d.Title, d.Company != null ? d.Company.Name : d.Stage.Name))
            .ToListAsync(ct);

        var companies = await db.Companies.AsNoTracking().Where(c => c.Name.Contains(s)).Take(4)
            .Select(c => new SearchHit("company", c.Id, c.Name, c.City)).ToListAsync(ct);

        var contacts = await db.Contacts.AsNoTracking()
            .Where(c => c.FullName.Contains(s) || (c.Email != null && c.Email.Contains(s)) || (phone != null && phone.Length >= 4 && c.PhoneKey!.Contains(phone)))
            .Take(4)
            .Select(c => new SearchHit("contact", c.Id, c.FullName, c.Company != null ? c.Company.Name : c.Email))
            .ToListAsync(ct);

        var leads = await db.Leads.AsNoTracking().VisibleTo(me)
            .Where(l => l.FullName.Contains(s) || (l.CompanyName != null && l.CompanyName.Contains(s)) || (l.Email != null && l.Email.Contains(s)))
            .Take(4)
            .Select(l => new SearchHit("lead", l.Id, l.FullName, l.CompanyName))
            .ToListAsync(ct);

        return [.. deals, .. companies, .. contacts, .. leads];
    }
}
