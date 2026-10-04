using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Wasl.Application.Activities;
using Wasl.Domain;

namespace Wasl.Application.Deals;

public record StageDto(Guid Id, string Name, int Order, int Probability, StageKind Kind);

public record NextTask(string Subject, DateTime DueAt, bool IsOverdue);

public record DealCard(
    Guid Id,
    string Title,
    decimal Value,
    string? CompanyName,
    string? ContactName,
    PersonRef Owner,
    DateTime? ExpectedClose,
    int DaysInStage,
    bool IsStale,
    NextTask? NextTask);

public record BoardColumn(
    Guid Id,
    string Name,
    int Probability,
    StageKind Kind,
    int Count,
    decimal Total,
    decimal Weighted,
    IReadOnlyList<DealCard> Deals);

public record BoardDto(IReadOnlyList<BoardColumn> Columns, decimal OpenTotal, decimal WeightedTotal, int StaleCount, int StaleAfterDays);

public record StageChangeDto(string? From, string To, string By, DateTime At, int? DaysInPrevious);

public record ContactCard(Guid Id, string FullName, string? JobTitle, string? Email, string? Phone);

public record DealDetails(
    Guid Id,
    string Title,
    decimal Value,
    DealStatus Status,
    StageDto Stage,
    IReadOnlyList<StageDto> Stages,
    PersonRef Owner,
    Ref? Company,
    ContactCard? Contact,
    DateTime? ExpectedClose,
    string? LostReason,
    DateTime CreatedAt,
    DateTime? ClosedAt,
    DateTime StageEnteredAt,
    DateTime LastActivityAt,
    bool IsStale,
    decimal WeightedValue,
    IReadOnlyList<StageChangeDto> History,
    IReadOnlyList<ActivityDto> Activities);

public record DealRow(Guid Id, string Title, decimal Value, DealStatus Status, string StageName, PersonRef Owner, DateTime? ExpectedClose);

public record SaveDealRequest(
    [Required, MaxLength(200)] string Title,
    [Range(0, 100_000_000)] decimal Value,
    Guid? CompanyId,
    Guid? ContactId,
    DateTime? ExpectedClose,
    Guid? OwnerId,
    Guid? StageId);

public record MoveDealRequest(Guid StageId, [MaxLength(300)] string? LostReason);

public class DealService(IAppDbContext db, ICurrentUser me)
{
    /// <summary>Won and lost columns only show the last 30 days so the board stays about current work.</summary>
    private const int ClosedWindowDays = 30;

    public async Task<BoardDto> BoardAsync(Guid? ownerId, string? search, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var staleDays = await StaleDaysAsync(ct);
        var stages = await db.Stages.AsNoTracking().OrderBy(s => s.Order).ToListAsync(ct);

        var query = db.Deals.AsNoTracking().VisibleTo(me)
            .Where(d => d.Status == DealStatus.Open || d.ClosedAt >= now.AddDays(-ClosedWindowDays));
        if (ownerId is { } o) query = query.Where(d => d.OwnerId == o);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(d => d.Title.Contains(s) || (d.Company != null && d.Company.Name.Contains(s))
                || (d.Contact != null && d.Contact.FullName.Contains(s)));
        }

        var rows = await query
            .Select(d => new
            {
                d.Id, d.Title, d.Value, d.StageId, d.Status, d.ExpectedClose, d.StageEnteredAt, d.LastActivityAt,
                Company = d.Company != null ? d.Company.Name : null,
                Contact = d.Contact != null ? d.Contact.FullName : null,
                Owner = new PersonRef(d.OwnerId, d.Owner.FullName),
                Next = db.Activities
                    .Where(a => a.DealId == d.Id && a.Type == ActivityType.Task && a.CompletedAt == null)
                    .OrderBy(a => a.DueAt)
                    .Select(a => new { a.Subject, a.DueAt })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var cards = rows.Select(r => (r.StageId, r.Value, Card: new DealCard(
            r.Id, r.Title, r.Value, r.Company, r.Contact, r.Owner, r.ExpectedClose,
            (int)(now - r.StageEnteredAt).TotalDays,
            r.Status == DealStatus.Open && r.LastActivityAt < now.AddDays(-staleDays),
            r.Next is { DueAt: { } due } ? new NextTask(r.Next.Subject, due, due < now) : null))).ToList();

        var columns = stages.Select(s =>
        {
            var inStage = cards.Where(c => c.StageId == s.Id).ToList();
            var total = inStage.Sum(c => c.Value);
            return new BoardColumn(s.Id, s.Name, s.Probability, s.Kind, inStage.Count, total,
                Math.Round(total * s.Probability / 100m, 2),
                inStage.Select(c => c.Card)
                    .OrderByDescending(c => c.IsStale)
                    .ThenBy(c => c.ExpectedClose ?? DateTime.MaxValue)
                    .ToList());
        }).ToList();

        var open = columns.Where(c => c.Kind == StageKind.Open).ToList();
        return new BoardDto(columns, open.Sum(c => c.Total), open.Sum(c => c.Weighted),
            open.Sum(c => c.Deals.Count(d => d.IsStale)), staleDays);
    }

    public async Task<DealDetails> GetAsync(Guid id, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var deal = await db.Deals.AsNoTracking().VisibleTo(me)
            .Include(d => d.Stage).Include(d => d.Owner).Include(d => d.Company).Include(d => d.Contact)
            .FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("الصفقة غير موجودة.");

        var stages = await db.Stages.AsNoTracking().OrderBy(s => s.Order).ToListAsync(ct);
        var names = stages.ToDictionary(s => s.Id, s => s.Name);

        var changes = await db.DealStageChanges.AsNoTracking()
            .Where(c => c.DealId == id).OrderBy(c => c.At)
            .Join(db.Users, c => c.ChangedById, u => u.Id, (c, u) => new { c.FromStageId, c.ToStageId, c.At, u.FullName })
            .ToListAsync(ct);

        var history = changes.Select((c, i) => new StageChangeDto(
            c.FromStageId is { } f ? names.GetValueOrDefault(f) : null,
            names.GetValueOrDefault(c.ToStageId, "؟"),
            c.FullName, c.At,
            i > 0 ? (int)(c.At - changes[i - 1].At).TotalDays : null)).ToList();

        var activities = await ActivityService.TimelineAsync(db.Activities.Where(a => a.DealId == id), ct);
        var staleDays = await StaleDaysAsync(ct);

        return new DealDetails(deal.Id, deal.Title, deal.Value, deal.Status, ToDto(deal.Stage),
            stages.Select(ToDto).ToList(),
            new PersonRef(deal.Owner.Id, deal.Owner.FullName),
            deal.Company is null ? null : new Ref(deal.Company.Id, deal.Company.Name),
            deal.Contact is null ? null : new ContactCard(deal.Contact.Id, deal.Contact.FullName, deal.Contact.JobTitle, deal.Contact.Email, deal.Contact.Phone),
            deal.ExpectedClose, deal.LostReason, deal.CreatedAt, deal.ClosedAt, deal.StageEnteredAt, deal.LastActivityAt,
            deal.IsStale(now, staleDays),
            Math.Round(deal.Value * deal.Stage.Probability / 100m, 2),
            history, activities);
    }

    public async Task<Guid> CreateAsync(SaveDealRequest req, CancellationToken ct)
    {
        var stage = req.StageId is { } sid
            ? await db.Stages.FirstOrDefaultAsync(s => s.Id == sid, ct) ?? throw new DomainException("المرحلة غير موجودة.")
            : await db.Stages.Where(s => s.Kind == StageKind.Open).OrderBy(s => s.Order).FirstAsync(ct);

        var ownerId = await ResolveOwnerAsync(req.OwnerId, ct);
        await ValidateLinksAsync(req.CompanyId, req.ContactId, ct);

        var deal = new Deal(req.Title, req.Value, stage, ownerId, req.CompanyId, req.ContactId, req.ExpectedClose);
        db.Deals.Add(deal);
        db.DealStageChanges.Add(new DealStageChange(deal.Id, null, stage.Id, me.UserId));
        if (ownerId != me.UserId)
            db.Notifications.Add(new Notification(ownerId, $"صفقة جديدة مسندة إليك: {deal.Title}", $"/deals/{deal.Id}"));

        await db.SaveChangesAsync(ct);
        return deal.Id;
    }

    public async Task UpdateAsync(Guid id, SaveDealRequest req, CancellationToken ct)
    {
        var deal = await LoadAsync(id, ct);
        await ValidateLinksAsync(req.CompanyId, req.ContactId, ct);
        deal.SetDetails(req.Title, req.Value, req.CompanyId, req.ContactId, req.ExpectedClose);

        if (req.OwnerId is { } owner && owner != deal.OwnerId)
        {
            deal.Reassign(await ResolveOwnerAsync(owner, ct));
            db.Notifications.Add(new Notification(owner, $"صفقة مسندة إليك: {deal.Title}", $"/deals/{deal.Id}"));
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Drag and drop on the board lands here. The rules live in Deal.MoveTo; the row version
    /// turns two people dropping the same card at once into a 409 instead of a silent overwrite.
    /// </summary>
    public async Task MoveAsync(Guid id, MoveDealRequest req, CancellationToken ct)
    {
        var deal = await LoadAsync(id, ct);
        var target = await db.Stages.FirstOrDefaultAsync(s => s.Id == req.StageId, ct)
            ?? throw new DomainException("المرحلة غير موجودة.");

        db.DealStageChanges.Add(deal.MoveTo(target, me.UserId, req.LostReason));

        var text = target.Kind switch
        {
            StageKind.Won => "ربحنا الصفقة",
            StageKind.Lost => $"خسرنا الصفقة: {req.LostReason}",
            _ => $"انتقلت إلى «{target.Name}»"
        };
        db.Activities.Add(new Activity(ActivityType.System, text, deal.OwnerId, me.UserId)
            .About(deal.Id, null, null, null));

        if (target.Kind == StageKind.Won && deal.OwnerId != me.UserId)
            db.Notifications.Add(new Notification(deal.OwnerId, $"تم تسجيل صفقتك كرابحة: {deal.Title}", $"/deals/{deal.Id}"));

        await db.SaveChangesAsync(ct);
    }

    public async Task<List<StageDto>> StagesAsync(CancellationToken ct) =>
        (await db.Stages.AsNoTracking().OrderBy(s => s.Order).ToListAsync(ct)).Select(ToDto).ToList();

    public static IQueryable<DealRow> Rows(IQueryable<Deal> deals) =>
        deals.OrderBy(d => d.Status).ThenByDescending(d => d.CreatedAt)
            .Select(d => new DealRow(d.Id, d.Title, d.Value, d.Status, d.Stage.Name, new PersonRef(d.OwnerId, d.Owner.FullName), d.ExpectedClose));

    private async Task<Deal> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Deals.VisibleTo(me).Include(d => d.Stage).FirstOrDefaultAsync(d => d.Id == id, ct)
        ?? throw new NotFoundException("الصفقة غير موجودة.");

    private async Task<Guid> ResolveOwnerAsync(Guid? requested, CancellationToken ct)
    {
        if (requested is not { } id || id == me.UserId) return me.UserId;
        if (!me.IsManager()) throw new ForbiddenException("إسناد الصفقات لغيرك من صلاحية المدير.");
        return await db.Users.AnyAsync(u => u.Id == id && u.IsActive, ct) ? id : throw new DomainException("الموظف غير موجود.");
    }

    private async Task ValidateLinksAsync(Guid? companyId, Guid? contactId, CancellationToken ct)
    {
        if (companyId is { } c && !await db.Companies.AnyAsync(x => x.Id == c, ct))
            throw new DomainException("الشركة غير موجودة.");
        if (contactId is { } p)
        {
            var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == p, ct)
                ?? throw new DomainException("جهة الاتصال غير موجودة.");
            if (companyId is { } company && contact.CompanyId is { } contactCompany && contactCompany != company)
                throw new DomainException("جهة الاتصال تتبع شركة أخرى.");
        }
    }

    private async Task<int> StaleDaysAsync(CancellationToken ct) =>
        await db.Tenants.Where(t => t.Id == me.TenantId).Select(t => t.StaleAfterDays).FirstAsync(ct);

    private static StageDto ToDto(Stage s) => new(s.Id, s.Name, s.Order, s.Probability, s.Kind);
}
