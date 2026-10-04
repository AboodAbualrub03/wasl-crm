using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wasl.Application;
using Wasl.Application.Leads;
using Wasl.Domain;
using Wasl.Infrastructure;
using Wasl.Infrastructure.Persistence;

namespace Wasl.Tests;

/// <summary>
/// Runs against a throwaway SQL Server database: lead assignment depends on SQL Server's
/// application locks, which an in-memory fake would not reproduce.
/// Override with WASL_TEST_DB; tests are skipped if SQL Server is unreachable.
/// </summary>
public sealed class SqlFixture : IAsyncLifetime
{
    public string ConnectionString { get; } = Environment.GetEnvironmentVariable("WASL_TEST_DB")
        ?? "Server=.;Database=WaslCrm_Tests;Trusted_Connection=True;TrustServerCertificate=True";

    public bool Available { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            await using var db = Context(new TestUser());
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
            Available = true;
        }
        catch (SqlException) { Available = false; }
    }

    public async Task DisposeAsync()
    {
        if (!Available) return;
        await using var db = Context(new TestUser());
        await db.Database.EnsureDeletedAsync();
    }

    public AppDbContext Context(ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options, user);
}

public sealed class TestUser(Guid? tenant = null, Guid? user = null, UserRole role = UserRole.Manager) : ICurrentUser
{
    public bool IsAuthenticated => tenant is not null;
    public Guid UserId { get; } = user ?? Guid.NewGuid();
    public Guid TenantId { get; } = tenant ?? Guid.Empty;
    public UserRole Role { get; } = role;
}

[CollectionDefinition("sql")]
public class SqlCollection : ICollectionFixture<SqlFixture>;

[Collection("sql")]
public class PersistenceTests(SqlFixture sql)
{
    private sealed record Workspace(Tenant Tenant, User Manager, List<User> Reps);

    private async Task<Workspace> SeedAsync(int reps)
    {
        var tenant = new Tenant("اختبار", $"t{Guid.NewGuid():N}"[..20]);
        var stages = new[]
        {
            new Stage("أول", 1, 10, StageKind.Open), new Stage("ربح", 2, 100, StageKind.Won), new Stage("خسارة", 3, 0, StageKind.Lost)
        };
        var manager = new User("مدير", "m@t.test", UserRole.Manager);
        manager.Update("مدير", UserRole.Manager, true, receivesLeads: false);
        var people = Enumerable.Range(1, reps).Select(i => new User($"مندوب {i}", $"r{i}@t.test", UserRole.Rep)).ToList();

        await using var db = sql.Context(new TestUser());
        foreach (var e in stages.Cast<ITenantOwned>().Append(manager).Concat(people)) e.TenantId = tenant.Id;
        foreach (var u in people.Append(manager)) u.SetPasswordHash("x");
        db.Add(tenant);
        db.AddRange(stages);
        db.AddRange(people.Append(manager));
        await db.SaveChangesAsync();
        return new Workspace(tenant, manager, people);
    }

    private LeadService Leads(AppDbContext db, ICurrentUser me) => new(db, me, new SqlLeadAssigner(db));

    [SkippableFact]
    public async Task Leads_arriving_together_are_spread_evenly()
    {
        Skip.IfNot(sql.Available, "SQL Server not reachable.");
        var ws = await SeedAsync(reps: 3);
        var me = new TestUser(ws.Tenant.Id, ws.Manager.Id);

        // 30 requests at once, each with its own connection, like 30 web form posts.
        await Task.WhenAll(Enumerable.Range(0, 30).Select(async i =>
        {
            await using var db = sql.Context(me);
            await Leads(db, me).CreateAsync(new SaveLeadRequest($"عميل {i}", null, null, null, null, null, LeadSource.Website, null), default);
        }));

        await using var check = sql.Context(me);
        var perRep = await check.Leads.GroupBy(l => l.OwnerId).Select(g => g.Count()).ToListAsync();
        Assert.Equal(3, perRep.Count);
        Assert.All(perRep, n => Assert.Equal(10, n));
    }

    [SkippableFact]
    public async Task Inactive_or_opted_out_people_receive_no_leads()
    {
        Skip.IfNot(sql.Available, "SQL Server not reachable.");
        var ws = await SeedAsync(reps: 2);
        var me = new TestUser(ws.Tenant.Id, ws.Manager.Id);

        await using (var db = sql.Context(me))
        {
            var off = await db.Users.FirstAsync(u => u.Id == ws.Reps[0].Id);
            off.Update(off.FullName, UserRole.Rep, isActive: true, receivesLeads: false);
            await db.SaveChangesAsync();
        }

        for (var i = 0; i < 4; i++)
        {
            await using var db = sql.Context(me);
            await Leads(db, me).CreateAsync(new SaveLeadRequest($"ع {i}", null, null, null, null, null, LeadSource.Website, null), default);
        }

        await using var check = sql.Context(me);
        Assert.All(await check.Leads.ToListAsync(), l => Assert.Equal(ws.Reps[1].Id, l.OwnerId));
    }

    [SkippableFact]
    public async Task Workspaces_never_see_each_other()
    {
        Skip.IfNot(sql.Available, "SQL Server not reachable.");
        var a = await SeedAsync(reps: 1);
        var b = await SeedAsync(reps: 1);

        await using (var db = sql.Context(new TestUser(a.Tenant.Id, a.Manager.Id)))
        {
            db.Companies.Add(new Company("شركة أ", a.Manager.Id));
            await db.SaveChangesAsync();
        }

        await using var asB = sql.Context(new TestUser(b.Tenant.Id, b.Manager.Id));
        Assert.Empty(await asB.Companies.ToListAsync());
        Assert.DoesNotContain(await asB.Users.ToListAsync(), u => u.TenantId == a.Tenant.Id);
    }

    [SkippableFact]
    public async Task Converting_a_lead_creates_contact_company_and_deal_with_history()
    {
        Skip.IfNot(sql.Available, "SQL Server not reachable.");
        var ws = await SeedAsync(reps: 1);
        var rep = new TestUser(ws.Tenant.Id, ws.Reps[0].Id, UserRole.Rep);

        Guid leadId;
        await using (var db = sql.Context(rep))
            leadId = await Leads(db, rep).CreateAsync(new SaveLeadRequest("سامي", "شركة سامي", "s@sami.ps", null, null, 5000, LeadSource.Referral, null), default);

        await using (var db = sql.Context(rep))
        {
            db.Activities.Add(new Activity(ActivityType.Call, "اتصال", rep.UserId, rep.UserId).About(null, null, null, leadId));
            await db.SaveChangesAsync();
        }

        ConvertResult result;
        await using (var db = sql.Context(rep))
            result = await Leads(db, rep).ConvertAsync(leadId, new ConvertLeadRequest("صفقة سامي", 5000, null, null), default);

        await using var check = sql.Context(rep);
        var deal = await check.Deals.SingleAsync(d => d.Id == result.DealId);
        Assert.Equal(result.ContactId, deal.ContactId);
        Assert.NotNull(result.CompanyId);
        Assert.True(await check.Activities.AnyAsync(a => a.DealId == deal.Id && a.Subject == "اتصال"));
        Assert.Equal(1, await check.DealStageChanges.CountAsync(c => c.DealId == deal.Id));
        Assert.Equal(LeadStatus.Converted, (await check.Leads.SingleAsync(l => l.Id == leadId)).Status);
    }

    [SkippableFact]
    public async Task Two_people_moving_the_same_deal_at_once_conflict_instead_of_overwriting()
    {
        Skip.IfNot(sql.Available, "SQL Server not reachable.");
        var ws = await SeedAsync(reps: 1);
        var me = new TestUser(ws.Tenant.Id, ws.Manager.Id);

        Guid dealId;
        await using (var db = sql.Context(me))
        {
            var first = await db.Stages.FirstAsync(s => s.Kind == StageKind.Open);
            var deal = new Deal("صفقة", 100, first, ws.Manager.Id, null, null, null);
            db.Deals.Add(deal);
            await db.SaveChangesAsync();
            dealId = deal.Id;
        }

        await using var a = sql.Context(me);
        await using var b = sql.Context(me);
        var dealA = await a.Deals.FirstAsync(d => d.Id == dealId);
        var dealB = await b.Deals.FirstAsync(d => d.Id == dealId);
        var lost = await a.Stages.FirstAsync(s => s.Kind == StageKind.Lost);
        var lostB = await b.Stages.FirstAsync(s => s.Kind == StageKind.Lost);

        dealA.MoveTo(lost, me.UserId, "سبب أ");
        await a.SaveChangesAsync();

        dealB.MoveTo(lostB, me.UserId, "سبب ب");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }
}
