# Wasl · وصل — Sales CRM

A multi-tenant CRM for B2B sales teams: leads come in (including from the company's own website), get scored and routed fairly to reps, become deals on a drag-and-drop pipeline, and roll up into forecasts and team reports.

**Stack:** ASP.NET Core 10 Web API · EF Core 10 · SQL Server · Angular 20 (standalone components, signals, CDK drag & drop) · xUnit

---

## What makes it more than CRUD

| Feature | What happens |
|---|---|
| **Fair lead routing** | A new lead goes to the active rep with the fewest open leads; ties go to whoever waited longest. Thirty leads arriving at once still split exactly evenly (see tests). |
| **Explainable lead score** | Each lead gets 0–100 with every point justified ("referral +25", "business email +15", "a month without contact −15"). Logging a call re-scores it. |
| **Pipeline with rules** | Drag a deal between stages. A deal can't be won without a value and a contact, or lost without a reason. Every move is stored as history. |
| **Cold-deal detection** | A background job flags open deals with no activity for N days (configurable per workspace) and warns the owner once; new activity re-arms it. Overdue tasks get one reminder. |
| **Forecast & funnel** | Weighted forecast (deal value × stage probability) by expected close month, actual wins, best case, a funnel built from stage history, win rate, cycle time, per-rep leaderboard, conversion by lead source, top loss reasons. |
| **Lead → deal conversion** | One transaction creates the contact, reuses or creates the company, opens a deal in the first stage, and moves the lead's activity history onto the deal. |
| **Duplicate detection** | Matches on email or phone, however the phone was typed (`+970 59-123 4567` = `0591234567`), warning live in the form. |
| **Web-to-lead** | Each workspace gets a secret intake URL and a copy-paste HTML form. Public endpoint: CORS-enabled, rate-limited, the key can be rotated. |
| **Ctrl+K command palette** | Search deals, companies, contacts and leads from anywhere, keyboard-driven. |

## Engineering decisions worth discussing

### Fair assignment under concurrency
Choosing the least-loaded rep is read → decide → write. Two leads arriving together would both read the same counts and land on the same person. `SqlLeadAssigner` takes a SQL Server application lock (`sp_getapplock`) scoped to the workspace and held until the transaction commits, so assignments within a workspace are serialized without locking any table, and other workspaces aren't blocked. Integration test: 30 parallel requests, 3 reps → exactly 10 each.

### Business rules live in the domain
`Deal.MoveTo`, `Lead.MarkConverted`, `Activity.About` enforce their invariants themselves. Services orchestrate; entities decide. The rules are covered by unit tests that need no database.

### Optimistic concurrency on deals
`Deal` has a SQL Server `rowversion`. Two people dragging the same card at once: the second save fails with `409 Conflict` instead of silently overwriting. The board then reloads the true state.

### Visibility in one place
Reps see their own deals and leads (plus the unassigned pool); managers see the team. `VisibleTo(me)` is applied to every query, and timelines on shared records (companies, contacts) also hide activity on deals the user can't open. That was a real leak found while testing and fixed.

### Multi-tenancy
EF Core global query filters on `TenantId` for every workspace-owned table; `SaveChanges` stamps new rows and refuses to move rows between tenants. The paths that legitimately cross tenants (login, public intake, background job) opt out explicitly with `IgnoreQueryFilters()`.

### Errors and security
RFC 7807 problem details everywhere; business-rule violations return `422` with a message the UI shows as is. JWT auth, PBKDF2 password hashing, login rate limiting (10/min/IP), separate sliding-window limit on the public form, intake key hidden from non-admins.

## Architecture

```
backend/
  src/
    Wasl.Domain           Entities, rules, lead scoring. No dependencies.
    Wasl.Application      Use cases per feature (Leads, Deals, Activities, Customers, Insights, Workspace).
    Wasl.Infrastructure   EF Core + SQL Server, lead assigner, JWT, background job, demo data.
    Wasl.Api              Controllers, auth, error handling, rate limiting, CORS, health checks.
  tests/Wasl.Tests        Domain unit tests + SQL Server integration tests.
frontend/                 Angular app (Arabic, RTL).
```

## Running locally

**Requirements:** .NET 10 SDK, Node 20+, SQL Server (default connection: local default instance, Windows auth).

```bash
# API: creates the database and loads demo data on first run
cd backend/src/Wasl.Api
dotnet run --launch-profile http        # http://localhost:5021  ·  API docs at /scalar

# Web app
cd frontend
npm install
npm start                               # http://localhost:4210
```

### Demo accounts
Workspace `nour` (a fictional software house), password `Demo@12345` for all:

| Email | Role |
|---|---|
| `karim@nour.test` | Sales rep |
| `salma@nour.test` | Sales rep |
| `basel@nour.test` | Sales rep |
| `manager@nour.test` | Sales manager: sees the whole team |
| `admin@nour.test` | Admin: pipeline stages, team, web form |

A second workspace `ofuq` (`admin@ofuq.test`) exists only to prove isolation.

### Tests
```bash
cd backend
dotnet test
```
Persistence tests create and drop `WaslCrm_Tests` on the local SQL Server (override with `WASL_TEST_DB`) and are skipped if it isn't reachable.

## Roadmap
- Email sync (Gmail/Outlook) to log emails on deals automatically
- Real-time board updates with SignalR
- Products and quote PDFs on deals
- Docker Compose and CI
