using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wasl.Api.Infrastructure;
using Wasl.Application;
using Wasl.Application.Customers;
using Wasl.Application.Deals;
using Wasl.Application.Insights;
using Wasl.Application.Workspace;

namespace Wasl.Api.Controllers;

[ApiController, Route("api/auth")]
public class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting(RateLimits.Login)]
    public Task<LoginResponse> Login(LoginRequest req, CancellationToken ct) => auth.LoginAsync(req, ct);
}

[ApiController, Authorize, Route("api/companies")]
public class CompaniesController(CustomerService customers) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<CompanyListItem>> List([FromQuery] DirectoryQuery q, CancellationToken ct) => customers.CompaniesAsync(q, ct);

    [HttpGet("{id:guid}")]
    public Task<CompanyDetails> Get(Guid id, CancellationToken ct) => customers.CompanyAsync(id, ct);

    [HttpPost]
    public async Task<IActionResult> Create(SaveCompanyRequest req, CancellationToken ct) =>
        Ok(new { id = await customers.CreateCompanyAsync(req, ct) });

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveCompanyRequest req, CancellationToken ct)
    {
        await customers.UpdateCompanyAsync(id, req, ct);
        return NoContent();
    }
}

[ApiController, Authorize, Route("api/contacts")]
public class ContactsController(CustomerService customers) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ContactListItem>> List([FromQuery] DirectoryQuery q, [FromQuery] Guid? companyId, CancellationToken ct) =>
        customers.ContactsAsync(q, companyId, ct);

    [HttpGet("{id:guid}")]
    public Task<ContactDetails> Get(Guid id, CancellationToken ct) => customers.ContactAsync(id, ct);

    [HttpPost]
    public async Task<IActionResult> Create(SaveContactRequest req, CancellationToken ct) =>
        Ok(new { id = await customers.CreateContactAsync(req, ct) });

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveContactRequest req, CancellationToken ct)
    {
        await customers.UpdateContactAsync(id, req, ct);
        return NoContent();
    }
}

[ApiController, Authorize, Route("api")]
public class InsightsController(HomeService home, ReportService reports, SearchService search) : ControllerBase
{
    [HttpGet("home")]
    public Task<HomeDto> Home(CancellationToken ct) => home.GetAsync(ct);

    [HttpGet("reports")]
    public Task<ReportDto> Reports(CancellationToken ct) => reports.GetAsync(ct);

    [HttpGet("search")]
    public Task<List<SearchHit>> Search([FromQuery] string q, CancellationToken ct) => search.SearchAsync(q, ct);
}

[ApiController, Authorize, Route("api")]
public class WorkspaceController(TeamService team, SettingsService settings, NotificationService notifications) : ControllerBase
{
    [HttpGet("team")]
    public Task<List<TeamMember>> Team(CancellationToken ct) => team.ListAsync(ct);

    [HttpPost("team")]
    public async Task<IActionResult> AddMember(CreateUserRequest req, CancellationToken ct) =>
        Ok(new { id = await team.CreateAsync(req, ct) });

    [HttpPut("team/{id:guid}")]
    public async Task<IActionResult> UpdateMember(Guid id, UpdateUserRequest req, CancellationToken ct)
    {
        await team.UpdateAsync(id, req, ct);
        return NoContent();
    }

    [HttpGet("settings")]
    public Task<WorkspaceSettings> Settings(CancellationToken ct) => settings.GetAsync(ct);

    [HttpPut("settings/stale-days")]
    public async Task<IActionResult> StaleDays(StaleRequest req, CancellationToken ct)
    {
        await settings.SetStaleDaysAsync(req.StaleAfterDays, ct);
        return NoContent();
    }

    [HttpPost("settings/intake-key")]
    public async Task<IActionResult> RotateKey(CancellationToken ct) => Ok(new { key = await settings.RotateIntakeKeyAsync(ct) });

    [HttpPut("settings/pipeline")]
    public Task<List<StageDto>> Pipeline(PipelineRequest req, CancellationToken ct) => settings.SavePipelineAsync(req, ct);

    [HttpGet("notifications")]
    public Task<NotificationFeed> Notifications(CancellationToken ct) => notifications.GetAsync(ct);

    [HttpPost("notifications/{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("notifications/read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return NoContent();
    }
}
