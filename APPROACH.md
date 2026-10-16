# Approach and Thought Process

## Task 1: Defect Fix and Server-Side Filtering

### Task 1.1: DEFECT-117 Ticket-List Filter Defect Fix

#### 1. Problem Analysis & Diagnosis
- **Symptom**: On the ticket list, selecting a status dropdown item (e.g., *In progress*) sends no request to `GET /api/tickets`. Typing in the search box afterwards sends a request that includes the previously selected status.
- **Root Cause**:
  1. The data-fetching `useEffect` in `apps/web/src/features/tickets/hooks/useTicketList.ts` watched only `[filters.search, filters.page, filters.pageSize]`. All non-search filter fields (`status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `sortBy`, `sortDirection`) were absent from the dependency array.
  2. `updateFilters` resets `page` to `1` when filters update. Since the initial page was already `1`, modifying any non-search dropdown produced no change in `[filters.search, filters.page, filters.pageSize]`, leaving the effect idle.
  3. The effect wrapped data-fetching in an unconditional `setTimeout(..., 300)` debounce, coupling all fetches to the search debounce timing.

#### 2. Architecture & Design Decisions
- **Chosen: Separate Debounced Search State (`debouncedSearch`)**:
  - Created a dedicated `useEffect` that listens exclusively to `filters.search` and updates `debouncedSearch` after a 300 ms delay.
  - The main data-fetching `useEffect` watches `debouncedSearch` and each individual non-search filter property.
  - *Why*: Isolates search debouncing to typing actions alone. Dropdown changes and resets execute immediately with 0 ms delay while reusing the settled search term.
- **Rejected Alternatives**:
  - *Placing all filters in a single debounced effect*: Would impose a 300 ms latency on every dropdown selection, violating the requirement that non-search filters trigger immediately.
  - *Triggering manual fetch directly in dropdown `onChange`*: Violates declarative React data-flow, risks state synchronization bugs with controlled inputs, and bypasses hook encapsulation.

#### 3. Verification & Testing
- Added regression tests in `apps/web/src/features/tickets/hooks/useTicketList.test.ts` covering:
  - Immediate `getTickets` dispatch on `status` change without timer advance.
  - Immediate refetch and page reset from page 2 to page 1 on filter change.
  - Preserved 300 ms search debounce alongside immediate non-search filtering.
  - Immediate refetch upon `resetFilters()`.
- Verified clean passes with `npm run test --workspace apps/web`, `npm run lint --workspace apps/web`, and `npm run typecheck --workspace apps/web`.

---

### Task 1.2: Server-Side Filtering

#### 1. Problem Analysis & Original Defect
- **Defect**: The API controller and DTO (`TicketQuery`) accepted filter parameters from the frontend (`search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`), but `TicketQueries.GetPagedAsync` did not apply them.
- **Root Cause**: `TicketsWithLabels()` was called directly and passed to `CountAsync(ct)` and `ApplySort().Skip().Take()`, querying the entire table and returning unfiltered records with a table-wide total count.

#### 2. Architecture & Design Decisions
- **Filtering on `IQueryable<TicketWithLabels>` via `ApplyFilters`**:
  - Filter clauses are chained via LINQ `.Where()` expressions before any database query execution.
  - *Why*: Ensures EF Core translates all criteria into native SQL `WHERE` clauses evaluated inside the database engine. Avoids in-memory evaluation (`ToList` / `AsEnumerable`) and preserves `AsNoTracking` performance.
  - *Extensibility (FL-6)*: Placing filter rules in a dedicated helper means adding future filters (such as Task 2's `slaStatus`) requires adding a single `.Where()` block without restructuring paging or projection pipelines.
- **Shared Filter Pipeline for Count and Page Results**:
  - `totalCount` and paged rows branch from the identical filtered `IQueryable`.
  - *Why*: If count were unfiltered or filtered differently, client-side pagination (`totalPages`, pager controls, empty states) would desynchronize from actual returned records.
- **Filter Semantics**:
  - Independent filters combine with logical `AND`.
  - `unassignedOnly=true` sets `AssignedAgentId == null`. When combined with `assignedAgentId`, conflicting conditions naturally produce an empty result set.
  - `search` is trimmed and ignored when blank; matches `Title`, `Reference`, or `Customer.Name` with `OR` conditions.
  - Sorting maintains a deterministic tie-breaker (`ThenByDescending(x => x.Ticket.Id)`).

#### 3. Verification & Captured SQL
- **Integration Tests**: Added 16 tests in `SupportDesk.UnitTests/Infrastructure/Queries/TicketQueriesTests.cs` using in-memory SQLite:
  - Exact matches for status, priority, category, customer, and agent.
  - `UnassignedOnly` and conflicting agent assignment.
  - Multi-field search (title, reference, customer name), trimming, and blank handling.
  - Multi-filter AND combination.
  - Pagination slice correctness and `totalCount` alignment on filtered data.
  - Generated SQL command inspection confirming database-side filtering.
- **Captured SQL Queries (EF Core command execution log for status=Open, priority=Critical, search="VPN")**:
  - *Count Query*:
    ```sql
    SELECT COUNT(*)
    FROM "Tickets" AS "t"
    INNER JOIN "Customers" AS "c" ON "t"."CustomerId" = "c"."Id"
    INNER JOIN "Categories" AS "c0" ON "t"."CategoryId" = "c0"."Id"
    LEFT JOIN "Agents" AS "a" ON "t"."AssignedAgentId" = "a"."Id"
    WHERE "t"."Status" = @query_Status_Value AND "t"."Priority" = @query_Priority_Value AND (instr("t"."Title", @search) > 0 OR instr("t"."Reference", @search) > 0 OR instr("c"."Name", @search) > 0)
    ```
  - *Paged Query*:
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
- **Test Results**: All 42 unit and query tests pass (`dotnet test apps/api/tests/SupportDesk.UnitTests`).

---

## Task 2: Ticket Escalation & Assignment

### Task 2.1: Business Rules (BR-1 through BR-8)

#### 1. Problem Analysis & Domain Modeling
- **Problem**: Support team leads previously triaged tickets by hand to determine priority, SLA response due dates, and agent assignment. The goal is to automate triage decisions upon ticket creation and establish explicit business rules for ticket escalation, ownership re-evaluation, and SLA status derivation.
- **Key Challenges & Placement**:
  - Rules must be small, cohesive, testable domain units shared between ticket creation and ticket escalation.
  - Zero hard-coded category names; category handling rules (`ForcesCriticalPriority`, `RequiresSpecialist`) must live in data on the `Category` aggregate.
  - SLA windows, multipliers, minimum duration floor, and at-risk thresholds must be configurable without code changes (via `appsettings.json` / `SlaOptions`).
  - Invariant enforcement (such as escalation eligibility, reason lengths, and priority transitions) must be strictly safeguarded in the domain layer.

#### 2. Architecture & Design Decisions
- **Rule Placement in Clean Architecture**:
  - **`PriorityPolicy` (`SupportDesk.Domain.Aggregates.Tickets`)**: Pure static domain policy evaluating BR-1 (`category.ForcesCriticalPriority` forces `Critical`; otherwise `requestedPriority ?? Medium`) and BR-7 priority progression (`Low -> Medium -> High -> Critical`, rejecting already `Critical` tickets). Returns both determined priority and a human-readable reason string for triage transparency.
  - **`SlaOptions` & `SlaCalculator` (`SupportDesk.Domain.Aggregates.Tickets`)**:
    - `SlaOptions` is a strongly typed POCO matching the `"Sla"` configuration section (`BaseWindowHours`, `PremiumCustomerMultiplier`, `MinimumWindowHours`, `AtRiskThresholdPercent`).
    - `SlaCalculator` computes SLA duration windows and resulting due dates from a specified timestamp (`startUtc`). Enforces BR-2 base windows (Critical 4h, High 8h, Medium 24h, Low 72h), BR-3 Premium customer tier reduction (`0.5x`), and the mandatory 1-hour minimum window floor (`MinimumWindowHours = 1`). Generates descriptive triage explanations.
  - **`AgentWorkload` & `AgentAssignmentPolicy` (`SupportDesk.Domain.Aggregates.Agents`)**:
    - `AgentWorkload` record pairs an `Agent` aggregate with their active open ticket count (`Status` not in `Resolved`, `Closed`).
    - `AgentAssignmentPolicy.IsAgentEligible` enforces BR-4 (`RequiresSpecialist` requires matching category specialization) and BR-5 (`IsActive == true` and `openTicketCount < agent.MaxOpenTickets`).
    - `AgentAssignmentPolicy.SelectAgent` selects the candidate with the fewest open tickets, breaking ties deterministically by `Agent.Id` ascending. If no agent is eligible, returns `null` with a diagnostic explanation (normal outcome, not an error).
    - `AgentAssignmentPolicy.ReevaluateAgentForEscalation` (BR-7) verifies whether the current assigned agent remains active, qualified, and within capacity before considering reassignment.
  - **`Ticket` Aggregate Invariants & `TicketEscalation` Entity (`SupportDesk.Domain.Aggregates.Tickets`)**:
    - `Ticket.Raise(...)` supports setting initial triage decisions (`dueAtUtc`, `assignedAgentId`).
    - `Ticket.Escalate(...)` encapsulates escalation state transitions: validates reason (5–500 chars) and actor, raises priority, updates due date from the escalation moment (`nowUtc`), updates assignee, and appends an immutable `TicketEscalation` history entity.
    - Prevents escalating `Critical`, `Resolved`, or `Closed` tickets by throwing `BusinessRuleViolationException`.
  - **`SlaEvaluator` (`SupportDesk.Domain.Aggregates.Tickets`)**:
    - Implements BR-8 SLA status derivation with injectable `nowUtc`.
    - Returns `NotApplicable` when `dueAtUtc == null`, `Met` when resolved on or before due date, `Breached` when resolved late or open past due date, `AtRisk` when unresolved inside window with $\le 25\%$ remaining, and `WithinSla` otherwise.
    - Backwards-compatible 3-parameter overload preserved for callers where window duration is not provided.
- **Rejected Alternatives**:
  - *Embedding triage logic inside Application Command Handlers*: Would scatter business rules into use cases and prevent sharing identical priority/SLA/assignment rules between Create and Escalate.
  - *Hardcoding Category Names (e.g. `category.Name == "Security"`)*: Fragile and violates the domain design where categories define their own handling behaviors via flags.
  - *Storing Derived SLA Status in Database*: Violates BR-8 ("derived, never stored"). SLA status dynamically changes as time progresses without any database writes.

#### 3. Verification & Testing
- Added comprehensive unit test suites in `SupportDesk.UnitTests`:
  - `PriorityPolicyTests.cs`: Forced-Critical categories (Outage/Security) always yield Critical; non-forcing categories respect requested priority or fallback to Medium; escalation priority step transitions from Low to Critical; throws on escalating Critical.
  - `SlaCalculatorTests.cs`: Standard tier base windows (72h, 24h, 8h, 4h); Premium tier halving (36h, 12h, 4h, 2h); 1-hour minimum floor boundary enforcement; configurable options from settings.
  - `AgentAssignmentPolicyTests.cs`: Fewest open tickets selection; deterministic tie-breaking by Agent.Id; specialist qualification filtering; capacity limit enforcement (strictly below max tickets); inactive agent exclusion; nobody eligible null fallback with reasons; escalation owner retention vs reassignment.
  - `SlaEvaluatorTests.cs`: Exact boundary tests: NotApplicable, Met, Breached, AtRisk at exactly 25% window remaining, AtRisk at < 25%, AtRisk at exact due date, Breached at +1ms past due, WithinSla at > 25%.
  - `TicketEscalationTests.cs`: Ticket aggregate state mutation; immutable history row generation; rejection of Critical, Resolved, and Closed tickets; validation of reason bounds (5–500 chars) and actor.
- **Test Results**: All 88 backend unit tests pass (`dotnet test apps/api/tests/SupportDesk.UnitTests`).

---

### Task 2.2: Backend Business Rules Integration

#### 1. Scope & Deliverables
Integrated the Task 2.1 business rules into the backend Web API, persistence layer, and query pipelines:
- **Deliverable A — Triage on Create (`POST /api/tickets`)**: Integrated BR-1 through BR-6 in `RaiseTicketCommandHandler`. Sets effective priority, calculates SLA due date via `SlaCalculator`, assigns eligible agents with fewest open tickets (or unassigned if none qualify), and populates a transparent `triage` response object.
- **Deliverable B — Persistent Escalation History & Migration**: Created `TicketEscalations` entity mapping, configured foreign keys (`TicketId` Cascade, `AgentId` Restrict), composite index `(TicketId, EscalatedAtUtc)`, and generated EF Core migration `20261016165900_AddTicketEscalations`.
- **Deliverable C — Escalation API (`POST /api/tickets/{id}/escalate`)**: Handled by `EscalateTicketCommandHandler`. Validates 5–500 character reason and actor, enforces status invariants (`409 Conflict` for Critical, Resolved, or Closed tickets; `404 Not Found` for unknown tickets), steps priority one level, recomputes SLA due date from escalation timestamp, re-evaluates current agent eligibility, and atomically persists ticket updates with audit history.
- **Deliverable D — Escalation History API (`GET /api/tickets/{id}/escalations`)**: Implemented in `GetTicketEscalationsQueryHandler` and `ITicketQueries.GetEscalationsAsync`. Returns history ordered newest-first (`EscalatedAtUtc DESC`), returns `404 Not Found` for nonexistent tickets, and returns an empty list for tickets without escalations.
- **Deliverable E — SQL-Based SLA Filtering (`GET /api/tickets?slaStatus=...`)**: Implemented in `TicketQueries.ApplyFilters`. Filters database records across all 5 SLA statuses (`NotApplicable`, `Met`, `Breached`, `AtRisk`, `WithinSla`) in SQL before total count calculation, deterministic sorting, and pagination.

#### 2. Architecture & Design Decisions
- **Clean Architecture & Separation of Concerns**:
  - `TicketsController` remains thin, delegating command and query dispatching to dedicated application handlers (`RaiseTicketCommandHandler`, `EscalateTicketCommandHandler`, `GetTicketEscalationsQueryHandler`).
  - Domain invariants (state transitions, eligibility checks, priority progression) remain strictly encapsulated in `Ticket` aggregate and domain policies (`PriorityPolicy`, `SlaCalculator`, `AgentAssignmentPolicy`).
  - Read queries and database projections remain encapsulated in `TicketQueries` within `SupportDesk.Infrastructure`.
- **Efficient Workload Querying (Eliminating N+1)**:
  - Added `IAgentRepository.GetCandidateWorkloadsAsync` which executes a single SQL query joining `Agents` with an open ticket count group-by subquery (`Status` not in `Resolved`, `Closed`). Avoids loading entire ticket tables into application memory or firing N queries per agent candidate.
- **Database Schema & Audit Immutability**:
  - Table `TicketEscalations` records before/after priority, before/after agent ID, before/after due dates, actor, reason, and UTC timestamp.
  - Foreign keys to `Agents` (`FromAgentId`, `ToAgentId`) use `DeleteBehavior.Restrict` so historical audit entries are never accidentally deleted if an agent record is removed.
  - Foreign key to `Tickets` (`TicketId`) uses `DeleteBehavior.Cascade` ensuring audit cleanup if a parent ticket is removed.
  - Composite index `IX_TicketEscalations_TicketId_EscalatedAtUtc` optimizes ticket-specific newest-first history retrieval.
- **Atomic Unit of Work**:
  - `Ticket.Escalate(...)` attaches the new `TicketEscalation` entity directly to the `Ticket` aggregate. `IUnitOfWork.SaveChangesAsync` persists both the updated ticket columns and the new escalation row in a single database transaction.
- **SQL SLA Filter Translation Without In-Memory Materialization**:
  - `SlaEvaluator` rules for `AtRisk` ($\le 25\%$ remaining) translate to SQL via `.Ticks` date difference arithmetic:
    `remainingTicks * 4 <= totalWindowTicks` $\iff$ `(DueAtUtc.Ticks - nowTicks) * 4 <= (DueAtUtc.Ticks - CreatedAtUtc.Ticks)`.
  - Translates seamlessly on both SQL Server (via `DATEDIFF_BIG`) and SQLite (via `julianday` ticks conversion), avoiding client-side evaluation warnings.

#### 3. Verification & Testing
- **Backend Unit & Integration Tests (`SupportDesk.UnitTests`)**: 126 passed, 0 failed, 0 skipped.
  - `RaiseTicketCommandHandlerTests`: Priority fallback, forced-Critical categories, SLA standard/premium calculations, minimum floor, specialist qualification, lowest workload selection, deterministic tie-breaking, unassigned triage response.
  - `EscalateTicketCommandHandlerTests`: Valid transitions (`Low -> Med -> High -> Critical`), Critical/Resolved/Closed conflicts (`409`), missing/invalid reason and actor validation (`400`), unknown ticket (`404`), owner retention when eligible, owner reassignment when ineligible, atomic persistence.
  - `GetTicketEscalationsQueryHandlerTests`: Unknown ticket (`404`), empty history, and populated newest-first list.
  - `TicketEscalationPersistenceTests`: SQLite in-memory persistence verifying schema constraints, nullable FKs/due dates, and cascade delete behavior.
  - `TicketQueriesTests`: All 5 SLA filter statuses evaluated in SQL, combined with status/priority/search, verified filtered counts, pagination, and newest-first escalation retrieval with ticket isolation.
- **Architecture Tests (`SupportDesk.ArchitectureTests`)**: 4 passed, 0 failed.
  - Confirmed Clean Architecture layer dependencies are maintained.
  - Confirmed EF Core persistence model matches the latest migration against SQL Server provider without pending changes.
- **Frontend Tests (`apps/web`)**: 6 passed across 2 test suites, verifying API contract compatibility.

---

## Task 2.3 Frontend Business Rules Integration

### Overview & Delivered Features

Integrated the backend Task 2.2 business rules, API contracts, and triage/escalation workflows into the React 19 / TypeScript frontend under `apps/web/src/features/tickets`:

| Requirement | Component / Hook | Delivered Behavior |
|---|---|---|
| **Req A: SLA Indicator** | `SlaIndicator.tsx`, `SlaCell.tsx`, `TicketSummary.tsx` | One reusable, fully typed component representing all 5 SLA states (`WithinSla`, `AtRisk`, `Breached`, `Met`, `NotApplicable`). Used in both ticket list and ticket detail summary. Conveys status using symbols, text labels, and color badges with accessible `aria-label`s. |
| **Req B: SLA Status Filter** | `TicketFilters.tsx`, `useTicketList.ts`, `api/tickets.ts` | Filter dropdown with friendly labels ("All SLA statuses", "Within SLA", "At risk", "Breached", "Met", "No SLA"). Sends `slaStatus` as query parameter, combines with existing filters via AND semantics, resets pagination to page 1 on filter changes, and omits the parameter when cleared. |
| **Req C: Escalation Panel** | `EscalationPanel.tsx`, `TicketDetailPage.tsx` | Submits `POST /api/tickets/{id}/escalate` with `reason` and `escalatedBy`. Client-side validation: reason 5–500 characters, actor non-empty $\le 100$ characters. Disables button while pending, prevents double submissions, handles HTTP 400/404/409 errors, and updates ticket in place on success. |
| **Req D: Escalation History** | `EscalationHistory.tsx`, `useTicketEscalations.ts` | Fetches `GET /api/tickets/{id}/escalations` newest-first. Renders before/after priority transitions, resolved agent names (or "Unassigned"), formatted due dates (or "No due date"), reason, actor, and formatted timestamps. Includes loading, error, and empty states. |
| **Req E: Creation Triage Result** | `CreateTicketPage.tsx` | Post-creation confirmation card displaying structured triage decision: applied priority + reason, calculated SLA due date + reason, assigned agent + reason (or unassigned explanation). |
| **Req F: Escalation Availability** | `EscalationPanel.tsx` | Disables/hides escalation controls when ticket is Critical, Resolved, or Closed with explanatory messages. Robustly handles server-side 409 responses for stale UI states. |
| **Req G: Strict Type Safety** | `apps/web/src/types/api.ts` | Fully typed models matching backend DTOs (`TicketTriage`, `TicketEscalation`, `EscalateTicketPayload`, `EscalationResponse`). Strictly zero `any` and zero `@ts-ignore` across all touched code and test files. |

### Architecture & Design Decisions

- **Single Reusable SLA Component**: `SlaIndicator` standardizes SLA badge rendering across both list rows (`SlaCell`) and the detail panel (`TicketSummary`), ensuring visual consistency and centralized accessibility attributes.
- **Backend-Driven Filtering & Pagination**: The SLA filter passes directly through `buildQuery` as `slaStatus` to the backend SQL query, letting SQL Server compute accurate filtered totals and pagination slices without in-memory evaluation.
- **In-Place Reactive Refresh**: Successful escalation invokes `setData(response.ticket)` and `escalations.reload()`, updating priority, due date, SLA badge, and escalation history without full-page reloads.
- **Accessibility & Contrast**: Badges combine distinct tones with textual status and symbols (`✓ Within SLA`, `⏳ At risk`, `✕ Breached`, `★ Met`, `— No SLA`), satisfying WCAG requirements where color is not the sole information channel.

### Verification & Testing Summary

1. **Vitest Unit & Integration Tests (`apps/web`)**: 41 passed, 0 failed across 9 test files.
   - `SlaIndicator.test.tsx` (5 tests): Verifies all 5 statuses, visible text, accessibility labels, and badge tone classes.
   - `useTicketList.test.ts` (7 tests): Debounce behavior, immediate non-search filter updates, pagination reset to page 1 on `slaStatus` change, and multi-filter combination.
   - `TicketFilters.test.tsx` (4 tests): Dropdown rendering, friendly labels, selection callback, clearing callback (undefined), and filter combination.
   - `EscalationPanel.test.tsx` (11 tests): Client-side length validation (5-500 chars), missing actor validation, pending state and double-click prevention, HTTP 400/404/409 error rendering, network failure handling, and Critical/Resolved/Closed availability rules.
   - `EscalationHistory.test.tsx` (4 tests): Loading spinner, error retry callback, empty history notice, and before/after transition display with null agent/date handling.
   - `CreateTicketPage.test.tsx` (3 tests): Assigned ticket triage decision display, unassigned explanation display, and reset flow.
   - `TicketDetailPage.test.tsx` (2 tests): Detail rendering and post-escalation refresh without full page reload.
   - `api/tickets.test.ts` (4 tests): Query parameter serialization and HTTP endpoint contracts.
   - `TicketTable.test.tsx` (1 test): List table row rendering with SLA status.
2. **TypeScript Compilation (`tsc -b --noEmit`)**: 0 errors.
3. **ESLint (`eslint .`)**: 0 errors, 0 warnings.
4. **Vite Production Bundle (`npm run build`)**: Succeeded cleanly (built in 710ms).

