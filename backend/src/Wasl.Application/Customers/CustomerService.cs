using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Wasl.Application.Activities;
using Wasl.Application.Deals;
using Wasl.Domain;

namespace Wasl.Application.Customers;

public record CompanyListItem(
    Guid Id, string Name, string? Industry, string? City, PersonRef Owner,
    int ContactCount, int OpenDeals, decimal OpenValue, decimal WonValue);

public record ContactRow(Guid Id, string FullName, string? JobTitle, string? Email, string? Phone);

public record CompanyDetails(
    Guid Id, string Name, string? Industry, string? City, string? Website, string? Phone, PersonRef Owner, DateTime CreatedAt,
    decimal OpenValue, decimal WonValue,
    IReadOnlyList<ContactRow> Contacts, IReadOnlyList<DealRow> Deals, IReadOnlyList<ActivityDto> Activities);

public record ContactListItem(Guid Id, string FullName, string? JobTitle, string? Email, string? Phone, Ref? Company, PersonRef Owner);

public record ContactDetails(
    Guid Id, string FullName, string? JobTitle, string? Email, string? Phone, Ref? Company, PersonRef Owner, DateTime CreatedAt,
    IReadOnlyList<DealRow> Deals, IReadOnlyList<ActivityDto> Activities);

public record SaveCompanyRequest(
    [Required, MaxLength(150)] string Name,
    [MaxLength(100)] string? Industry,
    [MaxLength(100)] string? City,
    [MaxLength(200)] string? Website,
    [MaxLength(40)] string? Phone,
    Guid? OwnerId);

public record SaveContactRequest(
    [Required, MaxLength(150)] string FullName,
    [EmailAddress, MaxLength(200)] string? Email,
    [MaxLength(40)] string? Phone,
    [MaxLength(100)] string? JobTitle,
    Guid? CompanyId,
    Guid? OwnerId);

public record DirectoryQuery(string? Search, int Page = 1, int PageSize = 25);

/// <summary>Companies and contacts are a shared directory: every rep can see them; deals stay private.</summary>
public class CustomerService(IAppDbContext db, ICurrentUser me)
{
    public Task<PagedResult<CompanyListItem>> CompaniesAsync(DirectoryQuery q, CancellationToken ct)
    {
        var query = db.Companies.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(c => c.Name.Contains(s) || (c.City != null && c.City.Contains(s)) || (c.Industry != null && c.Industry.Contains(s)));
        }

        return query
            .OrderBy(c => c.Name)
            .Select(c => new CompanyListItem(c.Id, c.Name, c.Industry, c.City, new PersonRef(c.OwnerId, c.Owner.FullName),
                db.Contacts.Count(x => x.CompanyId == c.Id),
                db.Deals.Count(d => d.CompanyId == c.Id && d.Status == DealStatus.Open),
                db.Deals.Where(d => d.CompanyId == c.Id && d.Status == DealStatus.Open).Sum(d => (decimal?)d.Value) ?? 0,
                db.Deals.Where(d => d.CompanyId == c.Id && d.Status == DealStatus.Won).Sum(d => (decimal?)d.Value) ?? 0))
            .ToPagedAsync(q.Page, q.PageSize, ct);
    }

    public async Task<CompanyDetails> CompanyAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Companies.AsNoTracking().Include(x => x.Owner).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("الشركة غير موجودة.");

        var contacts = await db.Contacts.AsNoTracking().Where(x => x.CompanyId == id).OrderBy(x => x.FullName)
            .Select(x => new ContactRow(x.Id, x.FullName, x.JobTitle, x.Email, x.Phone)).ToListAsync(ct);
        var deals = await DealService.Rows(db.Deals.AsNoTracking().VisibleTo(me).Where(d => d.CompanyId == id)).ToListAsync(ct);

        // The company timeline also gathers what happened on its deals and with its people,
        // but never reveals activity on a deal this user is not allowed to open.
        var activities = await ActivityService.TimelineAsync(VisibleActivities().Where(a =>
            a.CompanyId == id || (a.Deal != null && a.Deal.CompanyId == id) || (a.Contact != null && a.Contact.CompanyId == id)), ct);

        var totals = await db.Deals.VisibleTo(me).Where(d => d.CompanyId == id)
            .GroupBy(d => d.Status).Select(g => new { g.Key, Sum = g.Sum(d => d.Value) }).ToListAsync(ct);

        return new CompanyDetails(c.Id, c.Name, c.Industry, c.City, c.Website, c.Phone,
            new PersonRef(c.Owner.Id, c.Owner.FullName), c.CreatedAt,
            totals.FirstOrDefault(t => t.Key == DealStatus.Open)?.Sum ?? 0,
            totals.FirstOrDefault(t => t.Key == DealStatus.Won)?.Sum ?? 0,
            contacts, deals, activities);
    }

    public async Task<Guid> CreateCompanyAsync(SaveCompanyRequest req, CancellationToken ct)
    {
        var name = req.Name.Trim();
        if (await db.Companies.AnyAsync(c => c.Name == name, ct))
            throw new DomainException("توجد شركة بهذا الاسم مسبقًا.");

        var company = new Company(name, req.OwnerId ?? me.UserId);
        company.Update(name, req.Industry, req.City, req.Website, req.Phone, req.OwnerId ?? me.UserId);
        db.Companies.Add(company);
        await db.SaveChangesAsync(ct);
        return company.Id;
    }

    public async Task UpdateCompanyAsync(Guid id, SaveCompanyRequest req, CancellationToken ct)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException();
        var name = req.Name.Trim();
        if (await db.Companies.AnyAsync(c => c.Name == name && c.Id != id, ct))
            throw new DomainException("توجد شركة بهذا الاسم مسبقًا.");
        company.Update(name, req.Industry, req.City, req.Website, req.Phone, req.OwnerId ?? company.OwnerId);
        await db.SaveChangesAsync(ct);
    }

    public Task<PagedResult<ContactListItem>> ContactsAsync(DirectoryQuery q, Guid? companyId, CancellationToken ct)
    {
        var query = db.Contacts.AsNoTracking();
        if (companyId is { } cid) query = query.Where(c => c.CompanyId == cid);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(c => c.FullName.Contains(s) || (c.Email != null && c.Email.Contains(s))
                || (c.Phone != null && c.Phone.Contains(s)) || (c.Company != null && c.Company.Name.Contains(s)));
        }

        return query
            .OrderBy(c => c.FullName)
            .Select(c => new ContactListItem(c.Id, c.FullName, c.JobTitle, c.Email, c.Phone,
                c.CompanyId == null ? null : new Ref(c.CompanyId.Value, c.Company!.Name),
                new PersonRef(c.OwnerId, c.Owner.FullName)))
            .ToPagedAsync(q.Page, q.PageSize, ct);
    }

    public async Task<ContactDetails> ContactAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Contacts.AsNoTracking().Include(x => x.Owner).Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("جهة الاتصال غير موجودة.");

        var deals = await DealService.Rows(db.Deals.AsNoTracking().VisibleTo(me).Where(d => d.ContactId == id)).ToListAsync(ct);
        var activities = await ActivityService.TimelineAsync(VisibleActivities().Where(a =>
            a.ContactId == id || (a.Deal != null && a.Deal.ContactId == id)), ct);

        return new ContactDetails(c.Id, c.FullName, c.JobTitle, c.Email, c.Phone,
            c.Company is null ? null : new Ref(c.Company.Id, c.Company.Name),
            new PersonRef(c.Owner.Id, c.Owner.FullName), c.CreatedAt, deals, activities);
    }

    public async Task<Guid> CreateContactAsync(SaveContactRequest req, CancellationToken ct)
    {
        await EnsureCompanyAsync(req.CompanyId, ct);
        var ownerId = req.OwnerId ?? me.UserId;
        var contact = new Contact(req.FullName, ownerId);
        contact.Update(req.FullName, req.Email, req.Phone, req.JobTitle, req.CompanyId, ownerId);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(ct);
        return contact.Id;
    }

    public async Task UpdateContactAsync(Guid id, SaveContactRequest req, CancellationToken ct)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException();
        await EnsureCompanyAsync(req.CompanyId, ct);
        contact.Update(req.FullName, req.Email, req.Phone, req.JobTitle, req.CompanyId, req.OwnerId ?? contact.OwnerId);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Activities on deals follow the deal's visibility (reps see their own deals only).</summary>
    private IQueryable<Activity> VisibleActivities() =>
        me.IsManager() ? db.Activities : db.Activities.Where(a => a.DealId == null || a.Deal!.OwnerId == me.UserId);

    private async Task EnsureCompanyAsync(Guid? companyId, CancellationToken ct)
    {
        if (companyId is { } id && !await db.Companies.AnyAsync(c => c.Id == id, ct))
            throw new DomainException("الشركة غير موجودة.");
    }
}
