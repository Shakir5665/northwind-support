# Northwind Support Desk

A support ticket management system: raise tickets, search and filter them, assign them to agents,
and move them through their lifecycle.

**.NET 10 Web API** + **React 19 / TypeScript** + **SQL Server**, in one monorepo.

---

## The assignment

You are joining a team that already has this app working. Complete the tasks **in order**.
Full requirements are in **[`ASSIGNMENT_CANDIDATE.md`](ASSIGNMENT_CANDIDATE.md)**.

| # | Task | Done looks like | Effort |
| --- | --- | --- | --- |
| 1 | **Fix a defect, add server-side filtering** | Root cause fixed with a regression test; every list filter applied in the database with a correct count | ~2.5 h |
| 2 | **Ticket escalation & assignment** | Automatic triage on create, escalation with history, SLA status, UI for all of it | ~7 h |
| 3 | **Login and authentication** | Agents sign in; API rejects unauthenticated calls; login UI | ~4 h |
| 4 | **Dockerise the API** *(optional)* | `docker compose up` gives a migrated, seeded API with no baked-in secret | ~1.5 h |

### Guidance

- **Time: 2 days.** Honestly marking a task "not finished" beats rushing all of them.
- **Fork** this repo to your own GitHub account and work there. **Do not push to the original.**
- **AI tools are allowed.** You must understand and be able to defend every line you submit.
- **Commit as you go** and keep the real history. Commit the Task 1 defect fix and the filtering
  separately from each other and from the feature work. A single squashed commit is not accepted.
- **Where the spec is ambiguous**, make a decision, write it down in the README, and move on.
- **Out of scope:** registration, password reset, roles, OAuth/SSO, refresh tokens, MFA,
  notifications, CI, caching, visual redesign. Mention them under *Known limitations* instead.

### Submission

1. Update this **README** with what you built, your design decisions (chosen, rejected and why),
   the defect's root cause, dev login credentials, how to run and test it, AI usage, and known
   limitations. A reviewer must be able to run it without asking you anything.
2. Add an **APPROACH.md** explaining your approach and thought process for each task.
3. Commit both, then send us the **link to your fork**, the **dev login credentials**, and a
   **rough note of time spent** on what. (If the fork is private, add the reviewers as collaborators.)

---

## Task 1.1 Defect Fix (DEFECT-117)

### Root Cause
The data-fetching `useEffect` in `apps/web/src/features/tickets/hooks/useTicketList.ts` watched only `[filters.search, filters.page, filters.pageSize]`. All non-search filters (`status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `sortBy`, `sortDirection`) were omitted from the dependency array. Because `updateFilters` defaults `page` to `1`, changing a non-search filter while on page 1 left all watched dependencies unchanged, preventing the effect from firing and sending an API request. Furthermore, the effect wrapped the request in a blanket 300 ms `setTimeout`, which would have delayed non-search filters even if they were watched.

### Implementation
- **Decoupled Search Debounce**: Added a dedicated `useEffect` that listens exclusively to `filters.search` and updates an independent `debouncedSearch` state after 300 ms (`DEBOUNCE_MS`), clearing pending timers on successive keystrokes.
- **Immediate Non-Search Requests**: Configured the data-fetching `useEffect` to watch `debouncedSearch` alongside all individual non-search filter properties (`filters.status`, `filters.priority`, etc.). When any non-search filter changes, this effect runs synchronously without timer delay, calling `ticketsApi.getTickets({ ...filters, search: debouncedSearch })`.
- **Why Search Remains Debounced While Other Filters Are Immediate**: Keystrokes update `filters.search` immediately (keeping the search input responsive) but do not trigger data-fetching until `debouncedSearch` settles after 300 ms. Meanwhile, non-search filter changes update their respective state immediately and trigger the data-fetching effect right away using the current settled `debouncedSearch` and new filter value.
- **Immediate Reset & Reload**: Updated `resetFilters` to reset both `filters` and `debouncedSearch` synchronously, and updated `reload` to query using active filters with `debouncedSearch`.

### Verification & Tests Run
- **Automated Regression Tests**:
  - `npm run test --workspace apps/web`: 6 tests passing across 2 files in Vitest.
  - Added 4 regression tests in `apps/web/src/features/tickets/hooks/useTicketList.test.ts` verifying:
    1. Changing a non-search filter (`status`) triggers `getTickets` immediately without advancing timers.
    2. Changing a filter while on a later page (e.g., page 2) triggers immediately and resets `page` to 1.
    3. Typing in `search` delays the request by 300 ms, while subsequently changing `status` triggers immediately and preserves active search text.
    4. Resetting filters immediately triggers a request with default filters.
- **Code Quality**:
  - `npm run lint --workspace apps/web` passed with 0 errors and 0 warnings.
  - `npm run typecheck --workspace apps/web` passed with 0 errors.

---

## Task 1.2 Server-Side Filtering

### Original Defect
`GET /api/tickets` bound query string parameters into `TicketQuery`, but `TicketQueries.GetPagedAsync` did not apply them to the database query. It executed `CountAsync` and `ApplySort().Skip().Take()` on the unfiltered `TicketsWithLabels()` set, returning the entire ticket table with a table-wide total count regardless of requested filters.

### Filtering Strategy
- **Centralized `ApplyFilters` on `IQueryable<TicketWithLabels>`**: All filters are conditionally applied via `.Where()` clauses on `IQueryable` before executing any query against the database.
- **Why `IQueryable`**: Ensures EF Core translates all filter criteria directly into native SQL `WHERE` clauses on the database server. No in-memory filtering (`ToList` or `AsEnumerable`) occurs, preserving `AsNoTracking` performance.
- **Single Source for Count and Paged Slices**: Both `totalCount` (`CountAsync`) and the paged item slice (`ApplySort().Skip().Take().ToListAsync()`) branch from the exact same filtered `IQueryable`. If `totalCount` were evaluated without filters, pagination controls in the client would reflect total table rows rather than the filtered subset, breaking page count calculations.
- **Filter Semantics**:
  - Exact matching on `status`, `priority`, `categoryId`, `customerId`, and `assignedAgentId`.
  - `unassignedOnly=true` filters for `AssignedAgentId IS NULL`. When combined with `assignedAgentId`, AND logic ensures zero records match.
  - `search` is trimmed, ignored when blank, and tests `Title`, `Reference`, or `Customer.Name` with OR conditions.
  - Independent filters combine using logical AND.

### Captured Generated SQL
Verified in integration tests via EF Core command execution logging. For a query with `status=Open`, `priority=Critical`, and `search=VPN`:

**Count Query:**
```sql
SELECT COUNT(*)
FROM "Tickets" AS "t"
INNER JOIN "Customers" AS "c" ON "t"."CustomerId" = "c"."Id"
INNER JOIN "Categories" AS "c0" ON "t"."CategoryId" = "c0"."Id"
LEFT JOIN "Agents" AS "a" ON "t"."AssignedAgentId" = "a"."Id"
WHERE "t"."Status" = @query_Status_Value AND "t"."Priority" = @query_Priority_Value AND (instr("t"."Title", @search) > 0 OR instr("t"."Reference", @search) > 0 OR instr("c"."Name", @search) > 0)
```

**Paged Query:**
```sql
SELECT "t"."Id", "t"."Reference", "t"."Title", "t"."Status", "t"."Priority", "c"."Id", "c"."Name", "c"."Tier", "c0"."Id", "c0"."Name", "a"."Id" IS NULL, "a"."Id", "a"."FullName", "t"."CreatedAtUtc", "t"."UpdatedAtUtc", "t"."DueAtUtc", "t"."ResolvedAtUtc"
FROM "Tickets" AS "t"
INNER JOIN "Customers" AS "c" ON "t"."CustomerId" = "c"."Id"
INNER JOIN "Categories" AS "c0" ON "t"."CategoryId" = "c0"."Id"
LEFT JOIN "Agents" AS "a" ON "t"."AssignedAgentId" = "a"."Id"
WHERE "t"."Status" = @query_Status_Value AND "t"."Priority" = @query_Priority_Value AND (instr("t"."Title", @search) > 0 OR instr("t"."Reference", @search) > 0 OR instr("c"."Name", @search) > 0)
ORDER BY "t"."CreatedAtUtc" DESC, "t"."Id" DESC
LIMIT @p7 OFFSET @p
```
*(On SQL Server, paging maps to `OFFSET @p ROWS FETCH NEXT @p7 ROWS ONLY` and substring matching maps to collation-aware string comparisons).*

### Verification & Regression Tests Run
- Added 16 tests in `SupportDesk.UnitTests/Infrastructure/Queries/TicketQueriesTests.cs` using an in-memory SQLite database:
  - Exact match for each individual filter (`status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`).
  - `unassignedOnly=true` returns only unassigned tickets; combined with `assignedAgentId` returns empty.
  - Search matching on title, reference, and customer name; trimmed whitespace; blank search ignored.
  - Multi-filter AND combination.
  - Paging consistency: `totalCount`, `totalPages`, and distinct slices on filtered data.
  - Generated SQL command inspection confirming database-side filtering.
- `dotnet test apps/api/tests/SupportDesk.UnitTests`: 42 passed (16 query tests + 26 existing unit tests).
- `dotnet format apps/api/src/SupportDesk.Infrastructure --verify-no-changes`: 0 formatting changes.

---

## Task 2.1 Business Rules

### Overview & Rules Implemented
Implemented the complete domain business rules for automatic ticket triage, SLA calculation, workload assignment, escalation, and SLA status derivation (**BR-1 through BR-8**).

| Rule | Domain Component | Behavior |
|---|---|---|
| **BR-1 Priority** | `PriorityPolicy.DeterminePriority` | Uses requested priority, defaulting to `Medium`. A category with `ForcesCriticalPriority` (e.g. Security, Outage) is always `Critical`. No category names are hard-coded. |
| **BR-2 SLA Window** | `SlaCalculator.CalculateDueDate` | Base window hours from configuration: Critical: 4h, High: 8h, Medium: 24h, Low: 72h. Due date = creation time + base window. |
| **BR-3 Premium** | `SlaCalculator.CalculateDueDate` | Premium customers get half the window (`0.5x`), strictly subject to a 1-hour minimum floor (`MinimumWindowHours`). |
| **BR-4 Specialists** | `AgentAssignmentPolicy.IsAgentEligible` | A category flagged `RequiresSpecialist` (e.g. Billing, Outage, Security) may only be assigned to agents with that specialization. |
| **BR-5 Assignment** | `AgentAssignmentPolicy.SelectAgent` | Among eligible agents (active, specialized if required, strictly below `MaxOpenTickets`), selects the agent with fewest open tickets (open = not Resolved/Closed). Ties broken deterministically by `Agent.Id` ascending. If nobody is eligible, returns unassigned (`null`) with a reason (normal outcome, not an error). |
| **BR-6 Configurable** | `SlaOptions` & `appsettings.json` | Base window hours, premium multiplier, 1h floor, and at-risk threshold are configurable without code modifications via the `"Sla"` configuration section. |
| **BR-7 Escalation** | `Ticket.Escalate` & `PriorityPolicy.GetEscalatedPriority` | Raises priority one level (`Low -> Medium -> High -> Critical`). Recomputes due date from escalation moment (`nowUtc`). Re-evaluates owner (retains current agent if still eligible; otherwise reassigns per BR-5). Validates 5–500 character reason and actor. Records immutable `TicketEscalation` history. Invariant checks prevent escalating Critical, Resolved, or Closed tickets (`BusinessRuleViolationException`). |
| **BR-8 SLA Status** | `SlaEvaluator.Evaluate` | Derived, never stored. `NotApplicable` (no due date), `Met` (resolved on/before due), `Breached` (resolved late or open past due), `AtRisk` (unresolved with $\le 25\%$ window remaining), `WithinSla` (otherwise). "Now" is injectable. |

### Architecture & Design Decisions
- **Domain Purity & SOLID**: Business rules live as small, focused, cohesive domain policies and calculators in `SupportDesk.Domain` with zero dependencies on external frameworks or databases.
- **Shared Between Create & Escalate**: Priority determination, SLA calculation, and agent assignment policies are reusable pure components shared between ticket creation triage and ticket escalation.
- **Aggregate Root Encapsulation**: State transitions and invariants (rejecting escalation of Critical/Resolved/Closed tickets, ensuring 5–500 character reasons, and creating immutable escalation entries) are strictly guarded within the `Ticket` aggregate root.
- **Data-Driven Rules**: Categories carry `RequiresSpecialist` and `ForcesCriticalPriority` flags, avoiding brittle hardcoded category string checks.

### Verification & Tests Run
- Added 46 new unit tests (bringing the unit test suite from 42 to 88 passing tests):
  - `PriorityPolicyTests.cs`: Forced-Critical categories, requested priority, default Medium, one-level escalation bump, and Critical escalation rejection.
  - `SlaCalculatorTests.cs`: Base windows per priority, Premium tier halving, 1h minimum floor, and configurable option overrides.
  - `AgentAssignmentPolicyTests.cs`: Fewest open tickets, deterministic ID tie-breaking, specialist requirements, capacity limits, inactive agent exclusion, unassigned diagnostics, and escalation owner re-evaluation.
  - `SlaEvaluatorTests.cs`: Exact boundary conditions for SLA statuses (NotApplicable, Met, Breached, WithinSla, and AtRisk at exact 25% boundary, $<25\%$, and exact due date).
  - `TicketEscalationTests.cs`: Aggregate invariants, history record creation, and input validation.
- `dotnet test apps/api/tests/SupportDesk.UnitTests`: 88 passed, 0 failed, 0 skipped.

---

## Task 2.2 Backend Business Rules Integration

### Overview & Endpoints Implemented
Integrated Task 2.1 domain business rules with Web API endpoints, EF Core SQL Server persistence, and database-level query filtering:

| Endpoint | Method | Behavior & Contract |
|---|---|---|
| `/api/tickets` | `POST` | Applies BR-1–BR-6 on ticket creation. Computes effective priority, SLA due date, and eligible agent assignment with fewest open tickets (or `null` if none qualify). Returns `201 Created` with a `triage` object explaining priority, SLA, and assignment decisions. |
| `/api/tickets/{id}/escalate` | `POST` | Steps priority one level (`Low -> Medium -> High -> Critical`), recomputes SLA due date from escalation timestamp, re-evaluates agent eligibility (retains current owner if eligible; reassigns per BR-4/BR-5 otherwise), and atomically writes immutable `TicketEscalations` audit record. Returns `200 OK` on success, `400 Bad Request` on invalid reason/actor, `404 Not Found` if missing, or `409 Conflict` if already Critical, Resolved, or Closed. |
| `/api/tickets/{id}/escalations` | `GET` | Returns ticket escalation audit history ordered newest-first (`EscalatedAtUtc DESC`). Returns `404 Not Found` for unknown tickets, and an empty list `[]` for existing tickets with no escalation history. |
| `/api/tickets?slaStatus=...` | `GET` | Evaluates SLA status filter (`NotApplicable`, `Met`, `Breached`, `AtRisk`, `WithinSla`) directly in SQL before total count calculation, deterministic sorting, and pagination. Compatible with all existing filters via AND semantics. |

### Architecture & Design Decisions
- **Clean Architecture Integration**: Orchestration is performed in application command/query handlers (`RaiseTicketCommandHandler`, `EscalateTicketCommandHandler`, `GetTicketEscalationsQueryHandler`), keeping `TicketsController` thin and preserving domain aggregate invariants.
- **Persistent Audit History (`TicketEscalations`)**: Configured primary key, foreign keys (`TicketId` cascade delete; `FromAgentId`/`ToAgentId` restrict delete to safeguard audit history), string constraints, and index `IX_TicketEscalations_TicketId_EscalatedAtUtc`. Generated committed EF Core migration `20261016165900_AddTicketEscalations`.
- **Atomic Persistence**: Handled in `Ticket.Escalate(...)` by attaching the history entry to the `Ticket` aggregate and committing within `IUnitOfWork.SaveChangesAsync`.
- **Workload Efficiency (Zero N+1)**: `AgentRepository.GetCandidateWorkloadsAsync` retrieves agent capacity and active ticket counts in a single group-by database query rather than loading full ticket histories into memory.
- **SQL-Based SLA Filtering**: `TicketQueries.ApplyFilters` translates the domain `SlaEvaluator` rules directly to SQL via `.Ticks` date difference arithmetic, executing entirely on the database server.

### Verification & Tests Run
- Added 38 new automated tests (bringing the unit test suite from 88 to 126 passing tests):
  - `RaiseTicketCommandHandlerTests`: Validates triage response, forced Critical priority, standard/premium SLA window calculations, agent capacity/specialization eligibility, lowest workload selection, and unassigned fallback.
  - `EscalateTicketCommandHandlerTests`: Priority progression (`Low -> Med -> High -> Critical`), status invariants (`409 Conflict` on Critical/Resolved/Closed), input validation (`400 Bad Request` on short reason or missing actor), `404 Not Found`, owner retention vs reassignment, and atomic persistence.
  - `GetTicketEscalationsQueryHandlerTests`: Newest-first history retrieval, empty list handling, and `404 Not Found` handling.
  - `TicketEscalationPersistenceTests`: SQLite in-memory persistence verifying schema constraints, nullable agent/due date fields, and foreign key cascade deletion.
  - `TicketQueriesTests`: All 5 SLA filter statuses evaluated in SQL, combined with multi-filter criteria, verified filtered counts, and pagination stability.
- **`dotnet test apps/api/tests/SupportDesk.UnitTests`**: 126 passed, 0 failed, 0 skipped.
- **`dotnet test apps/api/tests/SupportDesk.ArchitectureTests`**: 4 passed, 0 failed (verifying Clean Architecture layer rules and EF Core model matching migration).
- **`npm test -- --run` (`apps/web`)**: 6 passed, 0 failed.

---

## Task 2.3 Frontend Business Rules Integration

### Overview & Features Implemented
Integrated Task 2.2 business rules and backend endpoints into the React 19 / TypeScript web app under `apps/web/src/features/tickets`:

| Feature | Component / File | Behavior & Contract |
|---|---|---|
| **SLA Indicator** | `SlaIndicator.tsx` | Single reusable component used across ticket table (`SlaCell.tsx`) and detail summary (`TicketSummary.tsx`). Fully types all 5 domain states (`WithinSla`, `AtRisk`, `Breached`, `Met`, `NotApplicable`) with text, symbols, badges, and accessible labels. |
| **SLA Status Filter** | `TicketFilters.tsx` · `useTicketList.ts` | Filter bar dropdown sending `slaStatus` query parameter to backend. Seamlessly combines with existing filters, resets page to 1 on change, and omits the query parameter when cleared. |
| **Escalation Panel** | `EscalationPanel.tsx` · `TicketDetailPage.tsx` | Detail page panel calling `POST /api/tickets/{id}/escalate`. Validates reason (5–500 chars) and actor ($\le 100$ chars) on the client, prevents rapid multi-clicks, displays pending state, handles 400/404/409 errors, and refreshes state in place without full page reload. |
| **Escalation History** | `EscalationHistory.tsx` · `useTicketEscalations.ts` | Displays audit log from `GET /api/tickets/{id}/escalations` newest-first. Formats before/after priorities, resolved agent names ("Unassigned" if null), formatted due dates ("No due date" if null), reason, actor, and timestamp. |
| **Creation Triage Decision** | `CreateTicketPage.tsx` | Displays automatic triage decisions returned by `POST /api/tickets`: effective priority + reason, SLA due date + reason, and assigned agent + reason (or unassigned explanation). |
| **Escalation Invariants** | `EscalationPanel.tsx` | Disables/hides escalation controls when ticket is Critical, Resolved, or Closed with explanatory messages, while also handling server 409 responses. |
| **Strict Type Safety** | `apps/web/src/types/api.ts` | Fully typed API request/response contracts matching backend DTOs. Zero `any` and zero `@ts-ignore` across all touched code. |

### Verification & Tests Run
- **`npm test` (`apps/web`)**: 41 passed across 9 test files (Vitest):
  - `SlaIndicator.test.tsx`: All 5 SLA states, text labels, accessibility attributes, badge tone styling.
  - `TicketFilters.test.tsx`: SLA dropdown, user-friendly labels, filter selection/clearing, multi-filter combination.
  - `useTicketList.test.ts`: SLA status parameter handling, immediate fetching, search debounce, and page-1 reset.
  - `EscalationPanel.test.tsx`: Client validation (5–500 chars), missing actor, pending state, double-click suppression, 400/404/409 error handling, and availability rules.
  - `EscalationHistory.test.tsx`: Loading, error retry, empty state, and transition formatting with null agent/due date handling.
  - `CreateTicketPage.test.tsx`: Assigned triage display, unassigned explanation display, and reset flow.
  - `TicketDetailPage.test.tsx`: In-place state refresh after escalation without full page reload.
  - `api/tickets.test.ts`: Query string building and escalation API contract calls.
  - `TicketTable.test.tsx`: Row rendering with SLA indicators.
- **`npm run typecheck` (`tsc -b --noEmit`)**: 0 errors.
- **`npm run lint` (`eslint .`)**: 0 errors, 0 warnings.
- **`npm run build` (`tsc -b && vite build`)**: Succeeded cleanly in 710ms.

---

## Task 2.4 Comprehensive Tests

### Implemented Coverage
- **Backend (xUnit & Moq)**:
  - `SlaCalculatorTests`: SLA windows for all priorities (`Low`, `Medium`, `High`, `Critical`), Premium tier halving (`0.5x`), configurable `MinimumWindowHours` floor, and null options rejection.
  - `PriorityPolicyTests` & `RaiseTicketCommandHandlerTests`: Forced-Critical category priority override and alignment with Critical SLA window.
  - `AgentAssignmentPolicyTests`: Fewest open tickets, deterministic `Agent.Id` ascending tie-breaking, specialist requirements, capacity limits, inactive agent exclusion, explanatory rejection reasons, and escalation re-evaluation.
  - `TicketEscalationTests` & `EscalateTicketCommandHandlerTests`: One-step priority increase, SLA due date recalculation from injected clock (`FixedClock`), agent retention/reassignment/unassigned fallback, accurate escalation audit trail persistence, sequential multi-step escalations, 404/409/status rejections, and exact 5/500 char boundary validation.
  - `SlaEvaluatorTests`: SLA evaluation at exact boundaries (`NotApplicable`, `Met`, `Breached`, `AtRisk`, `WithinSla`, 1-tick margins, and sub-second 25% threshold).
  - `TicketQueriesTests` & `SqlServerSlaQueryTests`: Provider-aware SQL SLA filtering on SQLite and live SQL Server (`localhost:1433`) for all statuses (`WithinSla`, `AtRisk`, `Breached`, `Met`, `NotApplicable`) and combined filters.
- **Frontend (Vitest & React Testing Library)**:
  - `EscalationPanel.test.tsx` & `TicketDetailPage.test.tsx`: Client-side rejection of short reasons (< 5 chars) without calling API; clear display of HTTP 409 conflict errors without falsely updating ticket priority or escalation history.
  - `SlaIndicator.test.tsx`: Distinct rendering of all 5 SLA states with unique visible labels, unique `aria-label` attributes, and distinct badge tones, ensuring status is not communicated by color alone.

### Verification Results
- `dotnet test apps/api/tests/SupportDesk.UnitTests`: 153 passed, 0 failed.
- `dotnet test apps/api/tests/SupportDesk.ArchitectureTests --no-build`: 4 passed, 0 failed.
- `npm test --workspace apps/web`: 44 passed across 9 suites, 0 failed.
- `npm run typecheck --workspace apps/web`: 0 errors.
- `npm run lint --workspace apps/web`: 0 errors.

---

## Task 2.5 Optional Stretch: SLA Summary Report

### Gating Assessment
Per `ASSIGNMENT_CANDIDATE.md` Section 2.5: *"Only if Tasks 1-3 are finished and tested."*
Because **Task 3 (Login & Authentication)** is required before implementing stretch features, production code implementation is gated behind Task 3 completion.

### Architecture & Single Database Aggregation Blueprint
The complete endpoint specification, single SQL query architecture, and EF Core translation query are documented in [SLA_SUMMARY_REPORT_DESIGN.md](file:///c:/Users/shaki/Desktop/northwind-support/SLA_SUMMARY_REPORT_DESIGN.md):
- **Endpoint**: `GET /api/reports/sla-summary?from=&to=&groupBy=agent|priority`
- **Single SQL Query**: Evaluates `total`, `breached`, `atRisk`, `within`, `met`, `breachRate`, and `averageResolutionHours` in a single `GROUP BY` statement with conditional aggregation (`COUNT(CASE WHEN ...)` and `AVG(...)`).
- **Zero Client-Side Evaluation**: Translates via `EF.Functions.DateDiffSecond` directly in SQL Server without client-side memory overhead.

---

## Quick start

**Prerequisites:** .NET SDK 10 (`global.json`), Node.js 22 (`.nvmrc`; 20.19+ works), Docker.

```bash
cp .env.example .env          # optional, every value has a working default
npm install                   # installs the web app
npm run db:up                 # SQL Server 2022 in Docker on localhost:1433
npm run api                   # terminal 1: migrates, seeds, then serves the API
npm run web                   # terminal 2: React dev server
```

| What | Where |
| --- | --- |
| Web app | http://localhost:5173 |
| API | https://localhost:7043 (also http://localhost:5043) |
| Swagger | https://localhost:7043/swagger |

In Development the API applies migrations and seeds demo data on startup. **Never commit real
credentials.**

**Visual Studio 2026:** run `npm run db:up`, open `SupportDesk.sln`, set
**SupportDesk.Presentation** as the startup project, and press F5 on the **https** profile.

## Tests

```bash
npm test                      # dotnet test, then the web tests (Vitest)
```

Both pass on a clean checkout and neither needs a database.

## Commands

| Command | What it does |
| --- | --- |
| `npm run db:up` / `db:down` / `db:reset` | Start / stop / wipe SQL Server |
| `npm run api` / `npm run web` | Run the API / the React app |
| `npm run build` | Build both |
| `npm run lint` / `typecheck` / `format` | Web lint and types / `dotnet format` |
| `npm run ef -- migrations add <Name>` | Add an EF Core migration |

## API at a glance

| Endpoint | Description |
| --- | --- |
| `GET /api/tickets` | Paged list with `search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `sortBy`, `sortDirection`. **Filters are accepted but not applied yet** (Task 1). |
| `GET /api/tickets/{id}` | One ticket |
| `POST /api/tickets` | Create a ticket |
| `PATCH /api/tickets/{id}/status` · `/assignment` | Change status / assignee |
| `GET /api/customers` · `/api/agents` · `/api/categories` | Reference data |

Errors are `ProblemDetails`: `400` validation, `404` not found, `409` conflict or business rule.

**Seed data:** 5 categories, 6 customers (3 Premium), 6 agents (one inactive, one at their limit)
and 40 tickets across every status, priority and SLA state.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| API cannot reach the database | `npm run db:up`, wait ~20 s, check `docker compose ps` |
| Port 1433 already in use | Change the port in `docker-compose.yml` and `.env` |
| Browser warns about the certificate | `dotnet dev-certs https --trust`, or use the web app, which proxies |
| Web app shows "Unable to load tickets" | The API is not running, or `VITE_API_PROXY_TARGET` points at the wrong port |
| SQL Server slow on Apple Silicon | Enable Rosetta in Docker Desktop → Settings → General |
