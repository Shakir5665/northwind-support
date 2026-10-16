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
