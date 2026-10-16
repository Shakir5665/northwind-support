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

### Known Limitations
- Task 2 SLA status filtering (`slaStatus`) is not yet implemented.
- Substring search uses database pattern matching (`Contains` / `LIKE`); full-text search indexing is out of scope.

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
