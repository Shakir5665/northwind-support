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

#### 4. Known Limitations
- Task 2 SLA status filter (`slaStatus`) is not yet implemented.
- Search uses standard database pattern matching; full-text indexing and fuzzy matching are not part of this scope.
