using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Wasl.Domain;

namespace Wasl.Application.Activities;

public record RelatedRef(string Kind, Guid Id, string Name);

public record ActivityDto(
    Guid Id,
    ActivityType Type,
    string Subject,
    string? Notes,
    DateTime? DueAt,
    DateTime? CompletedAt,
    PersonRef Owner,
    DateTime CreatedAt,
    bool IsOverdue,
    RelatedRef? Related);

public record CreateActivityRequest(
    ActivityType Type,
    [Required, MaxLength(200)] string Subject,
    [MaxLength(4000)] string? Notes,
    DateTime? DueAt,
    Guid? OwnerId,
    Guid? DealId,
    Guid? ContactId,
    Guid? CompanyId,
    Guid? LeadId);

public record MyTasksDto(
    IReadOnlyList<ActivityDto> Overdue,
    IReadOnlyList<ActivityDto> Today,
    IReadOnlyList<ActivityDto> Upcoming,
    IReadOnlyList<ActivityDto> RecentlyDone);

public class ActivityService(IAppDbContext db, ICurrentUser me)
{
    /// <summary>Projection shared by every timeline so they all look the same.</summary>
    public static IQueryable<ActivityDto> Project(IQueryable<Activity> query, DateTime now) =>
        query.Select(a => new ActivityDto(
            a.Id, a.Type, a.Subject, a.Notes, a.DueAt, a.CompletedAt,
            new PersonRef(a.OwnerId, a.Owner.FullName), a.CreatedAt,
            a.CompletedAt == null && a.DueAt != null && a.DueAt < now,
            a.DealId != null ? new RelatedRef("deal", a.DealId.Value, a.Deal!.Title)
            : a.LeadId != null ? new RelatedRef("lead", a.LeadId.Value, a.Lead!.FullName)
            : a.ContactId != null ? new RelatedRef("contact", a.ContactId.Value, a.Contact!.FullName)
            : a.CompanyId != null ? new RelatedRef("company", a.CompanyId.Value, a.Company!.Name)
            : null));

    /// <summary>Newest first, but open tasks float to the top.</summary>
    public static Task<List<ActivityDto>> TimelineAsync(IQueryable<Activity> query, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return Project(query
                .OrderBy(a => a.CompletedAt == null ? 0 : 1)
                .ThenByDescending(a => a.CompletedAt ?? a.CreatedAt)
                .Take(100), now)
            .ToListAsync(ct);
    }

    public async Task<ActivityDto> CreateAsync(CreateActivityRequest req, CancellationToken ct)
    {
        if (req.Type == ActivityType.System) throw new DomainException("نوع النشاط غير صالح.");
        if (req.Type == ActivityType.Task && req.DueAt is null) throw new DomainException("حدد موعدًا للمهمة.");

        var ownerId = req.OwnerId ?? me.UserId;
        if (ownerId != me.UserId)
        {
            if (!await db.Users.AnyAsync(u => u.Id == ownerId && u.IsActive, ct))
                throw new DomainException("الموظف المحدد غير موجود.");
            if (!me.IsManager() && req.Type != ActivityType.Task)
                throw new ForbiddenException("يمكنك تسجيل الأنشطة باسمك فقط.");
        }

        var activity = new Activity(req.Type, req.Subject, ownerId, me.UserId)
            .WithNotes(req.Notes)
            .Due(req.Type == ActivityType.Task ? req.DueAt : null)
            .About(req.DealId, req.ContactId, req.CompanyId, req.LeadId);

        await ApplySideEffectsAsync(activity, ct);
        db.Activities.Add(activity);

        if (ownerId != me.UserId)
        {
            var link = req.DealId is { } d ? $"/deals/{d}" : req.LeadId is { } l ? $"/leads/{l}" : "/tasks";
            db.Notifications.Add(new Notification(ownerId, $"مهمة جديدة لك: {activity.Subject}", link));
        }

        await db.SaveChangesAsync(ct);
        return await Project(db.Activities.Where(a => a.Id == activity.Id), DateTime.UtcNow).FirstAsync(ct);
    }

    public async Task CompleteAsync(Guid id, CancellationToken ct)
    {
        var activity = await LoadOwnAsync(id, ct);
        activity.Complete();
        if (activity.DealId is { } dealId)
            (await db.Deals.FirstAsync(d => d.Id == dealId, ct)).Touch();
        await db.SaveChangesAsync(ct);
    }

    public async Task ReopenAsync(Guid id, CancellationToken ct)
    {
        var activity = await LoadOwnAsync(id, ct);
        activity.Reopen();
        await db.SaveChangesAsync(ct);
    }

    public async Task<MyTasksDto> MyTasksAsync(Guid? ownerId, CancellationToken ct)
    {
        var uid = ownerId is { } o && me.IsManager() ? o : me.UserId;
        var now = DateTime.UtcNow;
        var startOfToday = now.Date;
        var endOfToday = startOfToday.AddDays(1);

        var open = db.Activities.Where(a => a.OwnerId == uid && a.Type == ActivityType.Task && a.CompletedAt == null);

        return new MyTasksDto(
            Overdue: await Project(open.Where(a => a.DueAt < startOfToday).OrderBy(a => a.DueAt), now).ToListAsync(ct),
            Today: await Project(open.Where(a => a.DueAt >= startOfToday && a.DueAt < endOfToday).OrderBy(a => a.DueAt), now).ToListAsync(ct),
            Upcoming: await Project(open.Where(a => a.DueAt >= endOfToday).OrderBy(a => a.DueAt).Take(50), now).ToListAsync(ct),
            RecentlyDone: await Project(db.Activities
                .Where(a => a.OwnerId == uid && a.Type == ActivityType.Task && a.CompletedAt >= now.AddDays(-7))
                .OrderByDescending(a => a.CompletedAt).Take(20), now).ToListAsync(ct));
    }

    /// <summary>
    /// Logging work has consequences: the deal stops counting as cold, a new lead becomes
    /// "contacted", and the lead's score is recomputed with the extra engagement.
    /// </summary>
    private async Task ApplySideEffectsAsync(Activity activity, CancellationToken ct)
    {
        if (activity.DealId is { } dealId)
        {
            var deal = await db.Deals.VisibleTo(me).FirstOrDefaultAsync(d => d.Id == dealId, ct)
                ?? throw new NotFoundException("الصفقة غير موجودة.");
            deal.Touch();
        }

        if (activity.LeadId is { } leadId)
        {
            var lead = await db.Leads.VisibleTo(me).FirstOrDefaultAsync(l => l.Id == leadId, ct)
                ?? throw new NotFoundException("العميل المحتمل غير موجود.");

            if (activity.Type is ActivityType.Call or ActivityType.Meeting or ActivityType.Email)
            {
                lead.RecordContact();
                var touches = await db.Activities.CountAsync(a => a.LeadId == leadId &&
                    (a.Type == ActivityType.Call || a.Type == ActivityType.Meeting || a.Type == ActivityType.Email), ct);
                lead.SetScore(LeadScoring.Calculate(lead, touches + 1, DateTime.UtcNow).Total);
            }
        }

        if (activity.ContactId is { } contactId && !await db.Contacts.AnyAsync(c => c.Id == contactId, ct))
            throw new NotFoundException("جهة الاتصال غير موجودة.");
        if (activity.CompanyId is { } companyId && !await db.Companies.AnyAsync(c => c.Id == companyId, ct))
            throw new NotFoundException("الشركة غير موجودة.");
    }

    private async Task<Activity> LoadOwnAsync(Guid id, CancellationToken ct)
    {
        var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("النشاط غير موجود.");
        if (activity.OwnerId != me.UserId && !me.IsManager())
            throw new ForbiddenException("هذه المهمة مسندة لشخص آخر.");
        return activity;
    }
}
