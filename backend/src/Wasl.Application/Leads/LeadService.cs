using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Wasl.Application.Activities;
using Wasl.Domain;

namespace Wasl.Application.Leads;

public record LeadQuery(
    LeadStatus? Status,
    LeadSource? Source,
    Guid? OwnerId,
    string? Search,
    bool OpenOnly = true,
    string Sort = "score",
    int Page = 1,
    int PageSize = 25);

public record LeadListItem(
    Guid Id,
    string FullName,
    string? CompanyName,
    string? Email,
    string? Phone,
    LeadSource Source,
    LeadStatus Status,
    int Score,
    PersonRef? Owner,
    decimal? EstimatedValue,
    DateTime CreatedAt,
    DateTime? LastContactedAt);

public record DuplicateMatch(string Kind, Guid Id, string Name, string? Detail, string Reason);

public record LeadDetails(
    Guid Id,
    string FullName,
    string? CompanyName,
    string? Email,
    string? Phone,
    string? Interest,
    decimal? EstimatedValue,
    LeadSource Source,
    LeadStatus Status,
    PersonRef? Owner,
    DateTime CreatedAt,
    DateTime? LastContactedAt,
    string? DisqualifyReason,
    Guid? ConvertedDealId,
    LeadScore Score,
    IReadOnlyList<DuplicateMatch> Duplicates,
    IReadOnlyList<ActivityDto> Activities);

public record SaveLeadRequest(
    [Required, MaxLength(150)] string FullName,
    [MaxLength(150)] string? CompanyName,
    [EmailAddress, MaxLength(200)] string? Email,
    [MaxLength(40)] string? Phone,
    [MaxLength(500)] string? Interest,
    [Range(0, 100_000_000)] decimal? EstimatedValue,
    LeadSource Source,
    Guid? OwnerId);

/// <summary>What a website form posts to the public intake endpoint.</summary>
public record WebLeadRequest(
    [Required, MaxLength(150)] string FullName,
    [MaxLength(150)] string? CompanyName,
    [EmailAddress, MaxLength(200)] string? Email,
    [MaxLength(40)] string? Phone,
    [MaxLength(500)] string? Message);

public record ConvertLeadRequest(
    [Required, MaxLength(200)] string DealTitle,
    [Range(0, 100_000_000)] decimal DealValue,
    DateTime? ExpectedClose,
    Guid? ExistingCompanyId);

public record ReasonRequest([Required, MaxLength(300)] string Reason);

public record AssignRequest(Guid OwnerId);

public record ConvertResult(Guid DealId, Guid ContactId, Guid? CompanyId);

public class LeadService(IAppDbContext db, ICurrentUser me, ILeadAssigner assigner)
{
    private static readonly ActivityType[] Touches = [ActivityType.Call, ActivityType.Meeting, ActivityType.Email];

    public async Task<PagedResult<LeadListItem>> ListAsync(LeadQuery q, CancellationToken ct)
    {
        var query = db.Leads.AsNoTracking().VisibleTo(me);

        if (q.Status is { } status) query = query.Where(l => l.Status == status);
        else if (q.OpenOnly) query = query.Where(l => l.Status != LeadStatus.Converted && l.Status != LeadStatus.Disqualified);
        if (q.Source is { } source) query = query.Where(l => l.Source == source);
        if (q.OwnerId is { } owner) query = query.Where(l => l.OwnerId == owner);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(l => l.FullName.Contains(s) || (l.CompanyName != null && l.CompanyName.Contains(s))
                || (l.Email != null && l.Email.Contains(s)) || (l.Phone != null && l.Phone.Contains(s)));
        }

        query = q.Sort == "recent"
            ? query.OrderByDescending(l => l.CreatedAt)
            : query.OrderByDescending(l => l.Score).ThenByDescending(l => l.CreatedAt);

        return await query
            .Select(l => new LeadListItem(l.Id, l.FullName, l.CompanyName, l.Email, l.Phone, l.Source, l.Status, l.Score,
                l.OwnerId == null ? null : new PersonRef(l.OwnerId.Value, l.Owner!.FullName),
                l.EstimatedValue, l.CreatedAt, l.LastContactedAt))
            .ToPagedAsync(q.Page, q.PageSize, ct);
    }

    public async Task<LeadDetails> GetAsync(Guid id, CancellationToken ct)
    {
        var lead = await db.Leads.AsNoTracking().VisibleTo(me).Include(l => l.Owner)
            .FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("العميل المحتمل غير موجود.");

        var touches = await db.Activities.CountAsync(a => a.LeadId == id && Touches.Contains(a.Type), ct);
        var score = LeadScoring.Calculate(lead, touches, DateTime.UtcNow);
        var duplicates = await FindDuplicatesAsync(lead.Email, lead.Phone, id, ct);
        var activities = await ActivityService.TimelineAsync(db.Activities.Where(a => a.LeadId == id), ct);

        return new LeadDetails(lead.Id, lead.FullName, lead.CompanyName, lead.Email, lead.Phone, lead.Interest,
            lead.EstimatedValue, lead.Source, lead.Status,
            lead.Owner is null ? null : new PersonRef(lead.Owner.Id, lead.Owner.FullName),
            lead.CreatedAt, lead.LastContactedAt, lead.DisqualifyReason, lead.ConvertedDealId,
            score, duplicates, activities);
    }

    /// <summary>
    /// Finds leads and contacts with the same email or the same phone, however the phone was typed.
    /// Shown as a warning while typing and on the lead page; it never blocks saving.
    /// </summary>
    public async Task<List<DuplicateMatch>> FindDuplicatesAsync(string? email, string? phone, Guid? excludeId, CancellationToken ct)
    {
        var mail = Text.NormalizeEmail(email);
        var phoneKey = Text.NormalizePhone(phone);
        if (mail is null && phoneKey is null) return [];

        var leads = await db.Leads.AsNoTracking()
            .Where(l => l.Id != excludeId && ((mail != null && l.Email == mail) || (phoneKey != null && l.PhoneKey == phoneKey)))
            .Take(5)
            .Select(l => new DuplicateMatch("lead", l.Id, l.FullName, l.CompanyName,
                mail != null && l.Email == mail ? "نفس البريد" : "نفس الهاتف"))
            .ToListAsync(ct);

        var contacts = await db.Contacts.AsNoTracking()
            .Where(c => (mail != null && c.Email == mail) || (phoneKey != null && c.PhoneKey == phoneKey))
            .Take(5)
            .Select(c => new DuplicateMatch("contact", c.Id, c.FullName, c.Company != null ? c.Company.Name : null,
                mail != null && c.Email == mail ? "نفس البريد" : "نفس الهاتف"))
            .ToListAsync(ct);

        return [.. leads, .. contacts];
    }

    public async Task<Guid> CreateAsync(SaveLeadRequest req, CancellationToken ct)
    {
        return await db.InTransactionAsync(async () =>
        {
            var lead = new Lead(req.FullName, req.Source);
            lead.Update(req.FullName, req.CompanyName, req.Email, req.Phone, req.Interest, req.EstimatedValue, req.Source);

            // Managers may hand-pick an owner; reps keep what they create; otherwise the assigner decides.
            if (req.OwnerId is { } chosen && me.IsManager())
                lead.AssignTo(await RequireUserAsync(chosen, ct));
            else if (me.Role == UserRole.Rep)
                lead.AssignTo(me.UserId);
            else
                await AutoAssignAsync(lead, me.TenantId, ct);

            lead.SetScore(LeadScoring.Calculate(lead, 0, DateTime.UtcNow).Total);
            db.Leads.Add(lead);
            NotifyOwner(lead);
            await db.SaveChangesAsync(ct);
            return lead.Id;
        }, ct);
    }

    /// <summary>
    /// Public web-to-lead intake. There is no signed-in user: the tenant comes from the secret key,
    /// so this path sets TenantId itself and reads across the tenant filter on purpose.
    /// </summary>
    public async Task<Guid> CreateFromWebAsync(string intakeKey, WebLeadRequest req, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.IntakeKey == intakeKey, ct)
            ?? throw new NotFoundException("رابط النموذج غير صالح.");

        return await db.InTransactionAsync(async () =>
        {
            var lead = new Lead(req.FullName, LeadSource.Website) { TenantId = tenant.Id };
            lead.Update(req.FullName, req.CompanyName, req.Email, req.Phone, req.Message, null, LeadSource.Website);
            await AutoAssignAsync(lead, tenant.Id, ct);
            lead.SetScore(LeadScoring.Calculate(lead, 0, DateTime.UtcNow).Total);
            db.Leads.Add(lead);
            NotifyOwner(lead, tenant.Id);
            await db.SaveChangesAsync(ct);
            return lead.Id;
        }, ct);
    }

    public async Task UpdateAsync(Guid id, SaveLeadRequest req, CancellationToken ct)
    {
        var lead = await LoadAsync(id, ct);
        lead.Update(req.FullName, req.CompanyName, req.Email, req.Phone, req.Interest, req.EstimatedValue, req.Source);
        await RescoreAsync(lead, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task AssignAsync(Guid id, Guid ownerId, CancellationToken ct)
    {
        if (!me.IsManager()) throw new ForbiddenException("إسناد العملاء من صلاحية المدير.");
        var lead = await LoadAsync(id, ct);
        lead.AssignTo(await RequireUserAsync(ownerId, ct));
        NotifyOwner(lead);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A rep can take an unassigned lead from the pool.</summary>
    public async Task ClaimAsync(Guid id, CancellationToken ct)
    {
        var lead = await LoadAsync(id, ct);
        if (lead.OwnerId is not null) throw new DomainException("هذا العميل مسند لموظف بالفعل.");
        lead.AssignTo(me.UserId);
        await db.SaveChangesAsync(ct);
    }

    public async Task QualifyAsync(Guid id, CancellationToken ct)
    {
        var lead = await LoadAsync(id, ct);
        lead.Qualify();
        await db.SaveChangesAsync(ct);
    }

    public async Task DisqualifyAsync(Guid id, string reason, CancellationToken ct)
    {
        var lead = await LoadAsync(id, ct);
        lead.Disqualify(reason);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReopenAsync(Guid id, CancellationToken ct)
    {
        var lead = await LoadAsync(id, ct);
        lead.Reopen();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Turns a qualified lead into a contact, an optional company, and a deal in the first stage,
    /// all in one transaction, and moves the lead's history onto the new deal.
    /// </summary>
    public async Task<ConvertResult> ConvertAsync(Guid id, ConvertLeadRequest req, CancellationToken ct)
    {
        return await db.InTransactionAsync(async () =>
        {
            var lead = await LoadAsync(id, ct);
            var ownerId = lead.OwnerId ?? me.UserId;
            if (lead.OwnerId is null) lead.AssignTo(ownerId);

            Guid? companyId = req.ExistingCompanyId;
            if (companyId is { } existing)
            {
                if (!await db.Companies.AnyAsync(c => c.Id == existing, ct)) throw new DomainException("الشركة غير موجودة.");
            }
            else if (lead.CompanyName is { } name)
            {
                // Reuse a company with the same name instead of creating a twin.
                companyId = await db.Companies.Where(c => c.Name == name).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
                if (companyId is null)
                {
                    var company = new Company(name, ownerId);
                    db.Companies.Add(company);
                    companyId = company.Id;
                }
            }

            var contact = new Contact(lead.FullName, ownerId);
            contact.Update(lead.FullName, lead.Email, lead.Phone, null, companyId, ownerId);
            db.Contacts.Add(contact);

            var firstStage = await db.Stages.Where(s => s.Kind == StageKind.Open).OrderBy(s => s.Order).FirstAsync(ct);
            var deal = new Deal(req.DealTitle, req.DealValue, firstStage, ownerId, companyId, contact.Id, req.ExpectedClose);
            db.Deals.Add(deal);
            db.DealStageChanges.Add(new DealStageChange(deal.Id, null, firstStage.Id, me.UserId));

            lead.MarkConverted(deal.Id);
            await db.SaveChangesAsync(ct);

            // The deal and contact rows must exist before the lead's history can point at them.
            await db.Activities.Where(a => a.LeadId == lead.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.DealId, deal.Id).SetProperty(a => a.ContactId, contact.Id), ct);

            db.Activities.Add(new Activity(ActivityType.System, $"تحوّل من عميل محتمل ({lead.FullName})", ownerId, me.UserId)
                .About(deal.Id, contact.Id, null, lead.Id));
            await db.SaveChangesAsync(ct);
            return new ConvertResult(deal.Id, contact.Id, companyId);
        }, ct);
    }

    private async Task AutoAssignAsync(Lead lead, Guid tenantId, CancellationToken ct)
    {
        var owner = await assigner.PickOwnerAsync(tenantId, ct);
        if (owner is null) return; // nobody receives leads: it stays in the shared pool
        lead.AssignTo(owner.Id);
        owner.MarkLeadAssigned();
    }

    private void NotifyOwner(Lead lead, Guid? tenantId = null)
    {
        if (lead.OwnerId is not { } ownerId || (me.IsAuthenticated && ownerId == me.UserId)) return;
        var n = new Notification(ownerId, $"عميل محتمل جديد مسند إليك: {lead.FullName}", $"/leads/{lead.Id}");
        if (tenantId is { } t) n.TenantId = t;
        db.Notifications.Add(n);
    }

    private async Task RescoreAsync(Lead lead, CancellationToken ct)
    {
        var touches = await db.Activities.CountAsync(a => a.LeadId == lead.Id && Touches.Contains(a.Type), ct);
        lead.SetScore(LeadScoring.Calculate(lead, touches, DateTime.UtcNow).Total);
    }

    private async Task<Lead> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Leads.VisibleTo(me).FirstOrDefaultAsync(l => l.Id == id, ct)
        ?? throw new NotFoundException("العميل المحتمل غير موجود.");

    private async Task<Guid> RequireUserAsync(Guid id, CancellationToken ct) =>
        await db.Users.AnyAsync(u => u.Id == id && u.IsActive, ct) ? id : throw new DomainException("الموظف غير موجود.");
}
