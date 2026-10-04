using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Wasl.Application.Deals;
using Wasl.Domain;

namespace Wasl.Application.Workspace;

public record LoginRequest([Required] string Workspace, [Required, EmailAddress] string Email, [Required] string Password);

public record Profile(Guid Id, string FullName, string Email, UserRole Role, string WorkspaceName);

public record LoginResponse(string Token, DateTime ExpiresAt, Profile User);

public record TeamMember(Guid Id, string FullName, string Email, UserRole Role, bool IsActive, bool ReceivesLeads, int OpenLeads, int OpenDeals);

public record CreateUserRequest(
    [Required, MaxLength(150)] string FullName,
    [Required, EmailAddress, MaxLength(200)] string Email,
    [Required, MinLength(8)] string Password,
    UserRole Role,
    bool ReceivesLeads = true);

public record UpdateUserRequest([Required, MaxLength(150)] string FullName, UserRole Role, bool IsActive, bool ReceivesLeads);

public record StageInput(Guid? Id, [Required, MaxLength(60)] string Name, [Range(0, 100)] int Probability);

public record PipelineRequest(
    [Required, MinLength(1)] List<StageInput> OpenStages,
    [Required, MaxLength(60)] string WonName,
    [Required, MaxLength(60)] string LostName);

public record WorkspaceSettings(string Name, string Code, int StaleAfterDays, string? IntakeKey);

public record StaleRequest([Range(1, 90)] int StaleAfterDays);

public record NotificationDto(Guid Id, string Title, string? Link, bool IsRead, DateTime CreatedAt);

public record NotificationFeed(int Unread, IReadOnlyList<NotificationDto> Items);

public class AuthService(IAppDbContext db, IPasswordHasher hasher, ITokenService tokens)
{
    private const string Invalid = "بيانات الدخول غير صحيحة.";

    public async Task<LoginResponse> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        var code = req.Workspace.Trim().ToLowerInvariant();
        var email = Text.NormalizeEmail(req.Email);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Code == code, ct) ?? throw new DomainException(Invalid);

        // Nobody is signed in yet, so the tenant filter is bypassed explicitly and the tenant matched by hand.
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == email, ct);
        if (user is null || !user.IsActive || !hasher.Verify(user, req.Password)) throw new DomainException(Invalid);

        var (token, expires) = tokens.Issue(user);
        return new LoginResponse(token, expires, new Profile(user.Id, user.FullName, user.Email, user.Role, tenant.Name));
    }
}

public class TeamService(IAppDbContext db, ICurrentUser me, IPasswordHasher hasher)
{
    public Task<List<TeamMember>> ListAsync(CancellationToken ct) =>
        db.Users.AsNoTracking().OrderBy(u => u.Role == UserRole.Rep).ThenBy(u => u.FullName)
            .Select(u => new TeamMember(u.Id, u.FullName, u.Email, u.Role, u.IsActive, u.ReceivesLeads,
                db.Leads.Count(l => l.OwnerId == u.Id && (l.Status == LeadStatus.New || l.Status == LeadStatus.Contacted || l.Status == LeadStatus.Qualified)),
                db.Deals.Count(d => d.OwnerId == u.Id && d.Status == DealStatus.Open)))
            .ToListAsync(ct);

    public async Task<Guid> CreateAsync(CreateUserRequest req, CancellationToken ct)
    {
        RequireAdmin();
        var email = Text.NormalizeEmail(req.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) throw new DomainException("البريد مستخدم مسبقًا.");

        var user = new User(req.FullName, req.Email, req.Role);
        user.Update(req.FullName, req.Role, true, req.ReceivesLeads);
        user.SetPasswordHash(hasher.Hash(user, req.Password));
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user.Id;
    }

    public async Task UpdateAsync(Guid id, UpdateUserRequest req, CancellationToken ct)
    {
        RequireAdmin();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException();
        if (id == me.UserId && (req.Role != UserRole.Admin || !req.IsActive))
            throw new DomainException("لا يمكنك إزالة صلاحية المدير عن حسابك.");
        user.Update(req.FullName, req.Role, req.IsActive, req.ReceivesLeads);
        await db.SaveChangesAsync(ct);
    }

    private void RequireAdmin()
    {
        if (me.Role != UserRole.Admin) throw new ForbiddenException("هذا الإجراء لمدير النظام فقط.");
    }
}

public class SettingsService(IAppDbContext db, ICurrentUser me)
{
    public async Task<WorkspaceSettings> GetAsync(CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == me.TenantId, ct);
        // The intake key is a secret; only admins see it.
        return new WorkspaceSettings(t.Name, t.Code, t.StaleAfterDays, me.Role == UserRole.Admin ? t.IntakeKey : null);
    }

    public async Task SetStaleDaysAsync(int days, CancellationToken ct)
    {
        RequireAdmin();
        var t = await db.Tenants.FirstAsync(x => x.Id == me.TenantId, ct);
        t.SetStaleAfterDays(days);
        await db.SaveChangesAsync(ct);
    }

    public async Task<string> RotateIntakeKeyAsync(CancellationToken ct)
    {
        RequireAdmin();
        var t = await db.Tenants.FirstAsync(x => x.Id == me.TenantId, ct);
        t.RegenerateIntakeKey();
        await db.SaveChangesAsync(ct);
        return t.IntakeKey;
    }

    /// <summary>
    /// Replaces the open stages in the given order. Existing stages keep their id (so deals stay put),
    /// new ones are added, and a stage that still holds deals cannot be removed.
    /// </summary>
    public async Task<List<StageDto>> SavePipelineAsync(PipelineRequest req, CancellationToken ct)
    {
        RequireAdmin();
        if (req.OpenStages.Count > 10) throw new DomainException("الحد الأقصى 10 مراحل مفتوحة.");

        return await db.InTransactionAsync(async () =>
        {
            var stages = await db.Stages.ToListAsync(ct);
            var keep = req.OpenStages.Where(s => s.Id is not null).Select(s => s.Id!.Value).ToHashSet();

            foreach (var removed in stages.Where(s => s.Kind == StageKind.Open && !keep.Contains(s.Id)))
            {
                if (await db.Deals.AnyAsync(d => d.StageId == removed.Id, ct))
                    throw new DomainException($"لا يمكن حذف مرحلة «{removed.Name}» لأنها تحتوي صفقات.");
                if (await db.DealStageChanges.AnyAsync(c => c.ToStageId == removed.Id || c.FromStageId == removed.Id, ct))
                    throw new DomainException($"لا يمكن حذف مرحلة «{removed.Name}» لأنها مستخدمة في سجل الصفقات.");
                db.Stages.Remove(removed);
            }

            var order = 1;
            foreach (var input in req.OpenStages)
            {
                var stage = input.Id is { } id
                    ? stages.FirstOrDefault(s => s.Id == id && s.Kind == StageKind.Open) ?? throw new DomainException("مرحلة غير معروفة.")
                    : AddStage(input.Name);
                stage.Rename(input.Name);
                stage.SetProbability(input.Probability);
                stage.MoveTo(order++);
            }

            var won = stages.First(s => s.Kind == StageKind.Won);
            won.Rename(req.WonName);
            won.MoveTo(order++);
            var lost = stages.First(s => s.Kind == StageKind.Lost);
            lost.Rename(req.LostName);
            lost.MoveTo(order);

            await db.SaveChangesAsync(ct);
            return (await db.Stages.AsNoTracking().OrderBy(s => s.Order).ToListAsync(ct))
                .Select(s => new StageDto(s.Id, s.Name, s.Order, s.Probability, s.Kind)).ToList();
        }, ct);

        Stage AddStage(string name)
        {
            var stage = new Stage(name, 0, 0, StageKind.Open);
            db.Stages.Add(stage);
            return stage;
        }
    }

    private void RequireAdmin()
    {
        if (me.Role != UserRole.Admin) throw new ForbiddenException("هذا الإجراء لمدير النظام فقط.");
    }
}

public class NotificationService(IAppDbContext db, ICurrentUser me)
{
    public async Task<NotificationFeed> GetAsync(CancellationToken ct)
    {
        var mine = db.Notifications.AsNoTracking().Where(n => n.UserId == me.UserId);
        return new NotificationFeed(
            await mine.CountAsync(n => !n.IsRead, ct),
            await mine.OrderByDescending(n => n.CreatedAt).Take(30)
                .Select(n => new NotificationDto(n.Id, n.Title, n.Link, n.IsRead, n.CreatedAt)).ToListAsync(ct));
    }

    public Task MarkReadAsync(Guid id, CancellationToken ct) =>
        db.Notifications.Where(n => n.Id == id && n.UserId == me.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);

    public Task MarkAllReadAsync(CancellationToken ct) =>
        db.Notifications.Where(n => n.UserId == me.UserId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
}
