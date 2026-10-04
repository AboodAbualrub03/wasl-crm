using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wasl.Api.Infrastructure;
using Wasl.Application;
using Wasl.Application.Activities;
using Wasl.Application.Deals;
using Wasl.Application.Leads;

namespace Wasl.Api.Controllers;

[ApiController, Authorize, Route("api/deals")]
public class DealsController(DealService deals) : ControllerBase
{
    [HttpGet("board")]
    public Task<BoardDto> Board([FromQuery] Guid? ownerId, [FromQuery] string? search, CancellationToken ct) =>
        deals.BoardAsync(ownerId, search, ct);

    [HttpGet("stages")]
    public Task<List<StageDto>> Stages(CancellationToken ct) => deals.StagesAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<DealDetails> Get(Guid id, CancellationToken ct) => deals.GetAsync(id, ct);

    [HttpPost]
    public async Task<IActionResult> Create(SaveDealRequest req, CancellationToken ct) =>
        Ok(new { id = await deals.CreateAsync(req, ct) });

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveDealRequest req, CancellationToken ct)
    {
        await deals.UpdateAsync(id, req, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/move")]
    public async Task<IActionResult> Move(Guid id, MoveDealRequest req, CancellationToken ct)
    {
        await deals.MoveAsync(id, req, ct);
        return NoContent();
    }
}

[ApiController, Authorize, Route("api/leads")]
public class LeadsController(LeadService leads) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<LeadListItem>> List([FromQuery] LeadQuery q, CancellationToken ct) => leads.ListAsync(q, ct);

    [HttpGet("{id:guid}")]
    public Task<LeadDetails> Get(Guid id, CancellationToken ct) => leads.GetAsync(id, ct);

    [HttpGet("duplicates")]
    public Task<List<DuplicateMatch>> Duplicates([FromQuery] string? email, [FromQuery] string? phone, [FromQuery] Guid? excludeId, CancellationToken ct) =>
        leads.FindDuplicatesAsync(email, phone, excludeId, ct);

    [HttpPost]
    public async Task<IActionResult> Create(SaveLeadRequest req, CancellationToken ct) =>
        Ok(new { id = await leads.CreateAsync(req, ct) });

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveLeadRequest req, CancellationToken ct)
    {
        await leads.UpdateAsync(id, req, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/assign")]
    public async Task<IActionResult> Assign(Guid id, AssignRequest req, CancellationToken ct)
    {
        await leads.AssignAsync(id, req.OwnerId, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/claim")]
    public async Task<IActionResult> Claim(Guid id, CancellationToken ct)
    {
        await leads.ClaimAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/qualify")]
    public async Task<IActionResult> Qualify(Guid id, CancellationToken ct)
    {
        await leads.QualifyAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/disqualify")]
    public async Task<IActionResult> Disqualify(Guid id, ReasonRequest req, CancellationToken ct)
    {
        await leads.DisqualifyAsync(id, req.Reason, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken ct)
    {
        await leads.ReopenAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/convert")]
    public Task<ConvertResult> Convert(Guid id, ConvertLeadRequest req, CancellationToken ct) => leads.ConvertAsync(id, req, ct);
}

/// <summary>Web-to-lead: a form on the customer's own website posts here. No login; the key picks the workspace.</summary>
[ApiController, AllowAnonymous, Route("api/public/leads")]
public class PublicIntakeController(LeadService leads) : ControllerBase
{
    [HttpPost("{key}")]
    [EnableRateLimiting(RateLimits.Intake)]
    [Microsoft.AspNetCore.Cors.EnableCors("intake")]
    public async Task<IActionResult> Submit(string key, WebLeadRequest req, CancellationToken ct)
    {
        await leads.CreateFromWebAsync(key, req, ct);
        return Accepted(new { message = "شكرًا لتواصلك، سنعود إليك قريبًا." });
    }
}

[ApiController, Authorize, Route("api/activities")]
public class ActivitiesController(ActivityService activities) : ControllerBase
{
    [HttpPost]
    public Task<ActivityDto> Create(CreateActivityRequest req, CancellationToken ct) => activities.CreateAsync(req, ct);

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        await activities.CompleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken ct)
    {
        await activities.ReopenAsync(id, ct);
        return NoContent();
    }

    [HttpGet("tasks")]
    public Task<MyTasksDto> MyTasks([FromQuery] Guid? ownerId, CancellationToken ct) => activities.MyTasksAsync(ownerId, ct);
}
