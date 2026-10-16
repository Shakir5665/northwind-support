# Northwind Support Desk

A support ticket management system: raise tickets, search and filter them, assign them to agents, and move them through their lifecycle.

**.NET 10 Web API** + **React 19 / TypeScript** + **SQL Server**, in one monorepo.

---

## Candidate Assessment Summary & Report

This section provides the complete implementation report required by [`ASSIGNMENT_CANDIDATE.md`](ASSIGNMENT_CANDIDATE.md), structured across the 11 evaluation categories:

1. [What was implemented and the rules as understood](#1-what-was-implemented-and-the-rules-as-understood)
2. [Design decisions: what was chosen, rejected and why](#2-design-decisions-what-was-chosen-rejected-and-why-the-most-carefully-read-section)
3. [The defect: root cause, fix, how to verify](#3-the-defect-root-cause-fix-how-to-verify)
4. [Filtering: where applied, how the SQL was checked, how the next filter is added](#4-filtering-where-applied-how-the-sql-was-checked-how-the-next-filter-is-added)
5. [Authentication: credential storage, hashing, token issue and storage, lifetime, production changes](#5-authentication-credential-storage-hashing-token-issue-and-storage-lifetime-production-changes)
6. [Development login credentials](#6-development-login-credentials)
7. [Docker: build and run, environment variables, image size, production migration strategy](#7-docker-build-and-run-environment-variables-image-size-production-migration-strategy)
8. [Database: what was added and which index and why](#8-database-what-was-added-and-which-index-and-why)
9. [Testing: how to run, what was tested and what deliberately was not](#9-testing-how-to-run-what-was-tested-and-what-deliberately-was-not)
10. [AI usage: tools and purpose, workflows, accepted/rejected, bugs found, verification](#10-ai-usage)
11. [Known limitations](#11-known-limitations)

---

### 1. What was implemented and the rules as understood

#### Task 1: Defect Fix and Server-Side Filtering
- **Task 1.1 (Defect Fix)**: Fixed the client-side reactivity defect in `useTicketList.ts` where non-search filter updates (`status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `sortBy`, `sortDirection`) failed to trigger data-fetching when on page 1.
- **Task 1.2 (Server-Side Filtering)**: Implemented server-side query filtering on `IQueryable<TicketWithLabels>` in EF Core for all filter parameters and multi-field substring search (`Title`, `Reference`, `Customer.Name`). Ensured `totalCount` and paged slices derive from the exact same filtered query.

#### Task 2: Ticket Escalation, Assignment & SLA Rules
Implemented complete domain rules (**BR-1 through BR-8**):
- **BR-1 Priority Determination**: Applies requested priority, defaulting to `Medium`. A category flagged `ForcesCriticalPriority` (Security, Outage) always produces `Critical` priority, overriding user selection.
- **BR-2 SLA Window Calculation**: Base resolution windows configured as Critical: 4h, High: 8h, Medium: 24h, Low: 72h from ticket creation.
- **BR-3 Premium Multiplier**: Premium customer tier halves the base window (`0.5x`), strictly subject to a 1-hour minimum floor (`MinimumWindowHours`).
- **BR-4 Specialist Requirements**: Categories flagged `RequiresSpecialist` (Billing, Outage, Security) restrict assignment to agents possessing matching specialization.
- **BR-5 Workload Assignment**: Eligible agents (active, specialized if required, strictly below `MaxOpenTickets`) are selected by fewest open tickets (`Resolved` and `Closed` tickets excluded). Ties are broken deterministically by `Agent.Id` ascending. If no agent qualifies, the ticket remains unassigned with a structured diagnostic reason.
- **BR-6 Configurable Settings**: Base windows, premium multiplier, floor, and at-risk threshold are bound from `IConfiguration` (`"Sla"` section) without code modification.
- **BR-7 Ticket Escalation**: Steps priority one level (`Low -> Medium -> High -> Critical`). Recalculates due date from the escalation moment (`nowUtc`). Re-evaluates assignee (retains owner if still eligible; reassigns per BR-5 otherwise). Records immutable `TicketEscalations` audit history. Enforces 5–500 character reason and non-empty actor. Invariant validation rejects escalating Critical, Resolved, or Closed tickets.
- **BR-8 SLA Status Derivation**: Derived dynamically in memory and SQL (never persisted): `NotApplicable` (null due date), `Met` (resolved on/before due), `Breached` (resolved late or open past due), `AtRisk` (unresolved with $\le 25\%$ window remaining), and `WithinSla` (otherwise).
- **Frontend Integration**: Reusable `SlaIndicator` badge with accessibility attributes, SLA filter dropdown, creation triage card, escalation panel with validation and 409 handling, and newest-first escalation history.

#### Task 3: Login and Authentication
- **Task 3.1 Backend Authentication**: PBKDF2/HMAC-SHA512 password hashing (`PasswordHasher<string>`). Direct `PasswordHash` column on `Agent`. JWT token issuance (`POST /api/auth/login`) and current user claims (`GET /api/auth/me`). Global fallback authorization policy protecting all endpoints by default. Escalation audit attribution derived from authenticated JWT claims. Idempotent development seeding with `DevPassword123!`.
- **Task 3.2 Frontend Authentication**: Dedicated `/login` route with field validation and error handling. Isolated `sessionStorage` token management. Automatic Bearer token attachment on internal requests. Protected route layout with open-redirect safe return navigation (`getSafeReturnUrl`). Navigation header agent identity and synchronous logout. Centralized HTTP 401 handling with debounce protection.

#### Task 4: Dockerise the API
- Multi-stage build (`apps/api/Dockerfile`) using .NET 10 SDK and ASP.NET 10 runtime.
- Non-root user `app` (UID 1654) on HTTP port `8080`.
- Nuget restore layer caching.
- Root `.dockerignore`.
- Docker Compose orchestration with SQL Server health gating (`condition: service_healthy`).
- Anonymous `GET /health` database connectivity check.
- Frontend proxy compatibility via `VITE_API_PROXY_TARGET=http://localhost:5080`.

---

### 2. Design decisions: what was chosen, rejected and why (the most carefully read section)

#### A. Frontend Reactivity & Search Debounce (Task 1.1)
- **Chosen**: Decoupled search debouncing into its own `useEffect` updating `debouncedSearch` after 300 ms, while all discrete dropdown/filter changes trigger data-fetching synchronously.
- **Rejected**: Keeping a blanket `setTimeout` on the whole fetch effect, or debouncing dropdown selections.
- **Why**: Dropdown selections represent explicit user commitments that should feel instantaneous. Wrapping all filters in a debounce introduces artificial lag, degrades UX responsiveness, and creates race conditions when clicking dropdowns in rapid succession.

#### B. Database Query Filtering via `IQueryable` (Task 1.2)
- **Chosen**: Composing `.Where()` clauses on `IQueryable<TicketWithLabels>` before executing `CountAsync` and `Skip().Take().ToListAsync()`.
- **Rejected**: In-memory filtering via `IEnumerable` or `.ToList()`.
- **Why**: In-memory filtering would load every ticket row into application memory, completely bypassing database indexing and causing memory exhaustion under production ticket volumes. Deriving `totalCount` and slices from the exact same query ensures pagination numbers remain synchronized.

#### C. Domain Policy Separation & Aggregate Invariants (Task 2.1 & 2.2)
- **Chosen**: Pure, stateless domain policies (`PriorityPolicy`, `SlaCalculator`, `AgentAssignmentPolicy`, `SlaEvaluator`) combined with aggregate-enforced invariants on `Ticket.Escalate`.
- **Rejected**: Placing business logic inside API controllers, EF Core query methods, or database triggers/stored procedures.
- **Why**: Preserves Clean Architecture domain purity, enables isolated unit testing with simulated clocks (`FixedClock`), and prevents code duplication between create triage and escalation.

#### D. Identity Model & Credential Storage (Task 3.1)
- **Chosen**: Adding a nullable `PasswordHash` column directly to the existing `Agent` domain aggregate, hashed with ASP.NET Core Identity `PasswordHasher<string>`.
- **Rejected**: Implementing full ASP.NET Core Identity (`IdentityDbContext`, `AspNetUsers`, `AspNetRoles`, `AspNetUserClaims`, etc.).
- **Why**: Full Identity DB introduces 8+ boilerplate tables, foreign key indirection, and heavy abstractions that are completely redundant when support agents are already 1:1 domain entities.
- **Rejected**: Plaintext passwords, reversible encryption, or fast un-salted hashes (MD5/SHA256).
- **Why**: Violates basic security standards. `PasswordHasher` uses PBKDF2 with HMAC-SHA512, 100,000 iterations, cryptographically secure 128-bit random salt, and versioned headers (`0x01`).

#### E. Authorization Architecture (Task 3.1)
- **Chosen**: Centralized fallback authorization policy (`RequireAuthenticatedUser()`) in `Program.cs`, explicitly exempting public endpoints (`POST /api/auth/login`, `GET /health`, Swagger) with `[AllowAnonymous]`.
- **Rejected**: Decorating every individual controller and action with `[Authorize]`.
- **Why**: "Secure by default" eliminates human error; developers cannot inadvertently expose newly created endpoints when adding future features.

#### F. Frontend Token Storage & Session Management (Task 3.2)
- **Chosen**: Browser `sessionStorage` (`auth_token`, `auth_user`) with manual `Authorization: Bearer` attachment in `client.ts`.
- **Rejected**: `localStorage`.
- **Why**: `localStorage` persists indefinitely across browser tabs and sessions, increasing credential exposure on shared operator workstations. `sessionStorage` provides tab-isolated sessions that automatically terminate when the tab is closed.
- **Rejected**: Cookie-based authentication with CSRF tokens for this assessment.
- **Why**: Avoids cookie domain/SameSite synchronization challenges across different host/container ports (`5173` vs `5080`) while remaining completely immune to Cross-Site Request Forgery (CSRF).

#### G. Container Security & Multi-Stage Architecture (Task 4)
- **Chosen**: Multi-stage build with non-root `app` user (UID 1654), HTTP port `8080`, runtime-injected environment variables, and layer caching.
- **Rejected**: Running container as `root`.
- **Why**: Container security best practice to prevent container breakout and host compromise.
- **Rejected**: Baking HTTPS certificates or `.env` secrets into the Docker image.
- **Why**: Violates Twelve-Factor App principles and leaks secrets to image registries. Production environments terminate TLS at ingress/reverse proxies.

---

### 3. The defect: root cause, fix, how to verify

#### Root Cause
In `apps/web/src/features/tickets/hooks/useTicketList.ts`, the data-fetching `useEffect` listed only `[filters.search, filters.page, filters.pageSize]` in its dependency array. Non-search filters (`status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `sortBy`, `sortDirection`) were completely omitted. Because changing any filter resets `page` to `1`, modifying a filter while already on page 1 left all watched dependencies unchanged, preventing the effect from firing. Furthermore, the effect wrapped the request in an unconditional 300 ms `setTimeout`, which would have delayed non-search filters even if they were watched.

#### Fix
1. **Decoupled Search Debounce**: Added a dedicated `useEffect` that listens exclusively to `filters.search` and updates `debouncedSearch` after 300 ms, clearing pending timers on successive keystrokes.
2. **Immediate Non-Search Requests**: Configured the data-fetching `useEffect` to watch `debouncedSearch` alongside all individual non-search filter properties (`filters.status`, `filters.priority`, etc.). When any non-search filter changes, this effect runs synchronously without timer delay.

#### How to Verify
- **Automated Tests**:
  ```bash
  npm test --workspace apps/web
  ```
  `useTicketList.test.ts` verifies:
  1. Changing a non-search filter (`status`) triggers `getTickets` immediately without advancing timers.
  2. Changing a filter on page 2 triggers immediately and resets `page` to 1.
  3. Typing in `search` delays the request by 300 ms.
  4. Resetting filters immediately triggers a request with default filters.
- **Manual Verification**: In the browser, changing any dropdown filter updates the table instantly.

---

### 4. Filtering: where applied, how the SQL was checked, how the next filter is added

#### Where Applied
All filtering is applied directly to `IQueryable<TicketWithLabels>` in `SupportDesk.Infrastructure/Data/Queries/TicketQueries.cs` via `ApplyFilters()`:
- Applied on the database server before `CountAsync` and before `ApplySort().Skip().Take().ToListAsync()`.
- Single source of truth: ensures total count and paged items are 100% synchronized.

#### How the SQL Was Checked
- Inspected generated SQL through EF Core logging during integration tests with both in-memory SQLite and Microsoft SQL Server (`SqlServerSlaQueryTests` and `TicketQueriesTests`).
- Confirmed zero client-side evaluation warnings: all `.Where()` clauses translate to native SQL predicates (`WHERE "t"."Status" = @p... AND ...`) and pagination translates to `OFFSET @p ROWS FETCH NEXT @p ROWS ONLY`.

**Captured Generated SQL (SQL Server):**
```sql
SELECT COUNT(*)
FROM [Tickets] AS [t]
INNER JOIN [Customers] AS [c] ON [t].[CustomerId] = [c].[Id]
INNER JOIN [Categories] AS [c0] ON [t].[CategoryId] = [c0].[Id]
LEFT JOIN [Agents] AS [a] ON [t].[AssignedAgentId] = [a].[Id]
WHERE [t].[Status] = @__status_0
  AND [t].[Priority] = @__priority_1
  AND (CHARINDEX(@__search_2, [t].[Title]) > 0
       OR CHARINDEX(@__search_2, [t].[Reference]) > 0
       OR CHARINDEX(@__search_2, [c].[Name]) > 0);
```

#### How the Next Filter Is Added (5 Steps)
1. **Application Query DTO**: Add property to `TicketQuery` in `SupportDesk.Application/Tickets/Queries/TicketQuery.cs` (e.g. `DateTime? CreatedAfterUtc`).
2. **Infrastructure Queryable**: Add conditional `.Where()` clause in `TicketQueries.ApplyFilters` in `SupportDesk.Infrastructure/Data/Queries/TicketQueries.cs`:
   ```csharp
   if (filter.CreatedAfterUtc.HasValue)
   {
       query = query.Where(t => t.CreatedAtUtc >= filter.CreatedAfterUtc.Value);
   }
   ```
3. **Web Types & Client**: Add property to `GetTicketsParams` in `apps/web/src/types/api.ts` and map it in `buildQuery` in `apps/web/src/api/tickets.ts`.
4. **UI Filter Component**: Add input control in `apps/web/src/features/tickets/components/TicketFilters.tsx` and bind to `useTicketList`.
5. **Tests**: Add unit test in `TicketQueriesTests.cs` and frontend filter test in `TicketFilters.test.tsx`.

---

### 5. Authentication: credential storage, hashing, token issue and storage, lifetime, production changes

- **Credential Storage**: Nullable `PasswordHash` column (`nvarchar(500) NULL`) directly on `Agents` table.
- **Password Hashing**: `Microsoft.AspNetCore.Identity.PasswordHasher<string>` (PBKDF2 with HMAC-SHA512, 100,000 iterations, cryptographically random 128-bit per-hash salt, versioned payload).
- **Token Issuance & Storage**: Issued via `POST /api/auth/login` as signed JWT (HMAC-SHA256) containing `NameIdentifier`, `Name`, and `Email` claims. Stored in client `sessionStorage` (`auth_token` and `auth_user`). Attached as `Authorization: Bearer <token>` on all internal API calls.
- **Token Lifetime**: 30 minutes (`Jwt:LifetimeMinutes=30`) with strict expiration validation and zero clock skew.
- **Production Changes (Hardening Roadmap)**:
  1. *HttpOnly Cookies*: Switch from `sessionStorage` to backend-issued `HttpOnly`, `Secure`, `SameSite=Strict` cookies accompanied by Anti-CSRF tokens to eliminate XSS token theft risks.
  2. *Token Revocation & Refresh Tokens*: Introduce short-lived access tokens (5–15 min) paired with sliding refresh tokens stored in Redis to enable immediate session revocation.
  3. *Brute-Force Protection*: Implement rate limiting (e.g. ASP.NET Core RateLimiter) and progressive account lockout on `POST /api/auth/login`.
  4. *Enterprise Identity & MFA*: Integrate Multi-Factor Authentication (TOTP) and enterprise SSO (OIDC/SAML via Microsoft Entra ID or Okta).
  5. *Secrets Vault*: Store JWT signing keys in Azure Key Vault or AWS Secrets Manager with automated key rotation.

---

### 6. Development login credentials

> [!CAUTION]
> **Security Notice**: Development credentials are local fixtures only. They are seeded exclusively in local development environments (`ASPNETCORE_ENVIRONMENT=Development`) and must **never** be used in production.

- **Development Password**: `DevPassword123!`
- **Default Active Agent**: `alex.turner@northwind-support.example`
- **Other Active Agents**:
  - `ben.osei@northwind-support.example`
  - `priya.nair@northwind-support.example`
  - `sara.lindqvist@northwind-support.example`
  - `marcus.doyle@northwind-support.example`
- **Inactive Agent (Rejected with 401)**:
  - `yuki.tanaka@northwind-support.example`
- **Seeder Idempotency**: Password hashes are generated at startup via `IPasswordHasher`. Existing agents with populated hashes are never overwritten.

---

### 7. Docker: build and run, environment variables, image size, production migration strategy

#### Build and Run Commands

```bash
# Build and run containers in background
docker compose up --build -d

# Wipe database volume and rebuild cleanly
docker compose down -v && docker compose up --build -d

# Check container health and status
docker compose ps

# Verify API health check directly (returns 200 OK 'Healthy')
curl -i http://localhost:5080/health

# Stop containers
docker compose down
```

**Running the Web App Against the Container (Port 5080):**
- **PowerShell (Windows):**
  ```powershell
  $env:VITE_API_PROXY_TARGET="http://localhost:5080"; npm run web
  ```
- **Bash / macOS / Linux:**
  ```bash
  VITE_API_PROXY_TARGET=http://localhost:5080 npm run web
  ```
- **Windows Command Prompt (cmd.exe):**
  ```cmd
  set "VITE_API_PROXY_TARGET=http://localhost:5080" && npm run web
  ```

#### Environment Variables

| Variable | Description | Value in `docker-compose.yml` |
|---|---|---|
| `ConnectionStrings__Default` | SQL Server connection string | `Server=sqlserver,1433;Database=SupportDesk;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True` |
| `Jwt__SigningKey` | 256-bit JWT signing secret ($\ge 32$ chars) | `local-development-secret-key-at-least-32-chars-long!` |
| `Jwt__Issuer` | JWT token issuer | `SupportDesk` |
| `Jwt__Audience` | JWT token audience | `SupportDeskClient` |
| `Jwt__LifetimeMinutes` | Token expiration in minutes | `30` |
| `ASPNETCORE_ENVIRONMENT` | Runtime environment | `Development` |

#### Measured Image Sizes
- **SDK Build Stage**: `mcr.microsoft.com/dotnet/sdk:10.0` — **1.3 GB** virtual size.
- **Runtime Base Image**: `mcr.microsoft.com/dotnet/aspnet:10.0` — **340 MB** virtual size.
- **Final API Image**: `northwind-support-api:latest` — **455 MB** virtual size (~65% reduction from SDK).

#### Production Database Migration Strategy
- In development, `app.MigrateAndSeedAsync()` runs automatically on startup for zero-touch setup.
- In production, running migrations in the web application container on startup is an anti-pattern:
  1. *Race Conditions*: Horizontally scaled API replicas starting simultaneously will race to apply DDL schema changes.
  2. *Least Privilege*: The web app should connect using a database user with DML privileges only (`SELECT`, `INSERT`, `UPDATE`, `DELETE`), not DDL privileges (`CREATE TABLE`, `ALTER TABLE`).
  3. *Controlled CI/CD Gate*: Production migrations must be decoupled from application startup and executed in a dedicated, auditable deployment step (such as a GitHub Actions workflow or Kubernetes Job) using `dotnet ef database update` or idempotent SQL scripts (`dotnet ef migrations script --idempotent`).

---

### 8. Database: what was added and which index and why

#### What Was Added
- **Table `TicketEscalations`**:
  - `Id` (PK, int identity)
  - `TicketId` (FK to `Tickets`, `DeleteBehavior.Cascade` — escalation history belongs to ticket aggregate)
  - `FromPriority`, `ToPriority` (nvarchar(20))
  - `FromAgentId`, `ToAgentId` (nullable FK to `Agents`, `DeleteBehavior.Restrict` — prevents orphaned history if an agent is deleted)
  - `FromDueAtUtc`, `ToDueAtUtc` (nullable datetime2)
  - `Reason` (nvarchar(500))
  - `EscalatedBy` (nvarchar(100))
  - `EscalatedAtUtc` (datetime2)
- **Column on `Agents`**: `PasswordHash` (`nvarchar(500) NULL`).
- **Migrations Committed**:
  - `20261016165900_AddTicketEscalations`
  - `20261016191527_AddAgentPasswordHash`

#### Which Index and Why
- **Index**: `IX_TicketEscalations_TicketId_EscalatedAtUtc` on `("TicketId", "EscalatedAtUtc" DESC)`.
- **Why**: `GET /api/tickets/{id}/escalations` queries history filtered by `TicketId` ordered newest-first (`EscalatedAtUtc DESC`). A composite index on `(TicketId, EscalatedAtUtc)` allows the database engine to perform an index seek directly to the ticket's records and read them in already-sorted chronological order without a costly in-memory sort operation.

---

### 9. Testing: how to run, what was tested and what deliberately was not

#### How to Run
```bash
# Run all backend and frontend tests
npm test

# Run backend unit tests only
dotnet test apps/api/tests/SupportDesk.UnitTests

# Run backend architecture tests only
dotnet test apps/api/tests/SupportDesk.ArchitectureTests

# Run frontend tests only (Vitest)
npm test --workspace apps/web
```

#### What Was Tested
- **Backend (183 tests: 179 unit/integration + 4 architecture)**:
  - SLA windows across all priorities (`Low`, `Medium`, `High`, `Critical`) for Standard (72h, 24h, 8h, 4h) and Premium (36h, 12h, 4h, 2h).
  - Configurable `MinimumWindowHours` floor enforcement (1h and 3h floor boundaries).
  - Forced-Critical category behavior overriding requested priority.
  - Agent assignment: fewest open tickets, specialist requirements, capacity limits, inactive agent exclusion, deterministic `Agent.Id` ascending tie-breaking, and unassigned explanations.
  - Ticket escalation: one-step priority increase, due date recalculated from injected clock (`FixedClock`), agent retention vs reassignment, atomic audit trail persistence, invariant rejections (Critical, Resolved, Closed), and exact 5/500 character reason boundary validation.
  - SLA evaluation at exact boundaries (`NotApplicable`, `Met`, `Breached`, `AtRisk`, `WithinSla`, 1-tick margins, sub-second 25% threshold).
  - SQL SLA filtering and query translation across SQLite and live SQL Server.
  - Clean Architecture layer boundaries and EF Core migration model consistency.
- **Frontend (68 tests across 12 suites)**:
  - Form validation, required fields, and email regex syntax.
  - Rejected 401 credentials and error banner visibility without session creation.
  - Pending state, control disabling, and double-click prevention.
  - Successful authentication, token persistence, and return navigation.
  - Already-authenticated redirect away from `/login`.
  - Protected route redirection with state preservation.
  - Application header agent identity and immediate logout session clearing.
  - Centralized Bearer token attachment on authenticated requests.
  - Global 401 session clearing and navigation with debounce protection.
  - Client-side escalation validation (5–500 chars), 409 conflict handling without false state mutations, and in-place ticket refresh.

#### What Deliberately Was Not Tested
- **E2E / Browser Automation (Playwright/Cypress)**: Intentionally excluded to keep the test suite fast (<5s execution), self-contained, and runnable in CI without browser driver dependencies.
- **Third-Party Library Internals**: Did not test `Microsoft.AspNetCore.Identity.PasswordHasher`'s internal PBKDF2 implementation or Vitest/React Router internals.
- **Out-of-Scope Features**: Did not test password reset, OAuth, MFA, or real-time WebSockets.
- **Multi-Million Row Load Tests**: Benchmarking against 10M tickets was not included in automated unit/integration suites.

---

### 10. AI usage

- **Tools and Purpose**: Antigravity AI coding assistant used for rapid project inspection, generating comprehensive test theory matrices (parameterized SLA priority/tier combinations), drafting EF Core query migrations, and verifying test regressions across C# and TypeScript codebases.
- **A Workflow That Worked**: *Spec-to-Test-First Workflow*: Reading the task specification (`ASSIGNMENT_CANDIDATE.md`), creating unit tests defining the exact domain boundaries (`SlaCalculatorTests`, `AgentAssignmentPolicyTests`), implementing the code to turn them green, and running `dotnet test` and `npm test` after every discrete change.
- **Something Accepted As-Is**:
  - Standard ASP.NET Core Identity `PasswordHasher<string>` implementation for PBKDF2 HMAC-SHA512 hashing, which provides cryptographically sound salting and hashing without custom cryptographic code.
  - The composite index `IX_TicketEscalations_TicketId_EscalatedAtUtc` generated by EF Core for newest-first history retrieval.
- **Something Changed or Rejected and Why**:
  - *Rejected*: AI suggested using `DateTime.Ticks` for SQL Server SLA calculation. This was rejected and changed because SQL Server's EF Core provider throws an `InvalidOperationException` (HTTP 500) when translating `.Ticks` differences in LINQ queries. We changed it to provider-aware `EF.Functions.DateDiffSecond` for SQL Server while retaining ticks arithmetic for SQLite.
  - *Rejected*: AI initially proposed creating a separate `AgentCredential` entity and table. This was rejected in favor of a nullable `PasswordHash` directly on `Agent` to avoid unnecessary 1:1 table joins and preserve the existing domain model.
  - *Rejected*: Inline environment variable syntax in Windows PowerShell (`VITE_API_PROXY_TARGET=... npm run web`). We corrected this to provide PowerShell-native syntax (`$env:VITE_API_PROXY_TARGET=...; npm run web`).
- **Any Bug AI Introduced or Helped Find**:
  - AI helped identify that `TicketQueries.GetPagedAsync` was calculating `totalCount` on the unfiltered dataset, which would have reported incorrect pagination totals when filters were active.
  - AI introduced a race condition in the frontend 401 handler where simultaneous API requests returning 401 would trigger multiple concurrent navigations to `/login`. We caught this in test review and introduced the `isHandlingUnauthorized` debounce latch.
- **How Generated Code Was Verified**:
  - Every line of generated code was verified via:
    1. Automated tests (`dotnet test` and `npm test` — all 251 tests passing).
    2. Strict static analysis: C# compiler (`dotnet build`), TypeScript compiler (`npm run typecheck` with 0 errors), ESLint (`npm run lint` with 0 warnings), and `dotnet format --verify-no-changes`.
    3. Manual runtime verification against running Docker SQL Server container and browser.

---

### 11. Known limitations

The following features were deliberately left out of scope per assignment instructions:
1. **User Registration & Self-Service Password Reset**: Agents are provisioned via administrative seeding. There is no public signup or email password reset workflow.
2. **Account Lockout & Rate Limiting**: The `/api/auth/login` endpoint does not enforce rate limiting (throttling) or progressive delay / account lockout after consecutive failed attempts.
3. **Session Revocation & Refresh Tokens**: Tokens are stateless and valid for 30 minutes. Once issued, a JWT cannot be revoked before expiration without a distributed token revocation blocklist (e.g. in Redis).
4. **Multi-Factor Authentication (MFA / 2FA) & SSO**: Single-factor email/password only. Enterprise SSO (OIDC/SAML with Azure AD or Okta) is not integrated.
5. **Real-Time Push Notifications**: Ticket updates and escalation events do not push live WebSocket/SignalR notifications to other connected clients; users must refresh or navigate to see updates from other agents.
6. **Large File Attachments**: Tickets do not support file or screenshot uploads.
7. **Production Secret Management**: Development signing keys and database passwords are configured via environment variables and `.env.example`, but production deployment would require an external secret manager (e.g. Azure Key Vault or AWS Secrets Manager).
8. **Horizontal Scaling of Database Migrations**: Automatic migration on container startup is configured for development convenience only; production requires separating migration execution into a dedicated CI/CD pipeline step.

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

In Development the API applies migrations and seeds demo data on startup. **Never commit real credentials.**

**Visual Studio 2026:** run `npm run db:up`, open `SupportDesk.sln`, set **SupportDesk.Presentation** as the startup project, and press F5 on the **https** profile.

---

## Commands

| Command | What it does |
| --- | --- |
| `npm run db:up` / `db:down` / `db:reset` | Start / stop / wipe SQL Server |
| `npm run api` / `npm run web` | Run the API / the React app |
| `npm run build` | Build both |
| `npm run lint` / `typecheck` / `format` | Web lint and types / `dotnet format` |
| `npm run ef -- migrations add <Name>` | Add an EF Core migration |
| `npm test` | Run all backend and frontend tests |

---

## API at a glance

| Endpoint | Method | Auth | Description |
|---|---|---|---|
| `/api/auth/login` | `POST` | Anonymous | Authenticates agent with email/password; returns JWT and profile. |
| `/api/auth/me` | `GET` | Bearer | Returns current authenticated agent profile. |
| `/api/tickets` | `GET` | Bearer | Paged list with search, status, priority, category, customer, agent, SLA status. |
| `/api/tickets` | `POST` | Bearer | Creates ticket with automatic triage (priority, SLA due date, agent). |
| `/api/tickets/{id}` | `GET` | Bearer | Ticket detail view. |
| `/api/tickets/{id}/escalate` | `POST` | Bearer | Steps priority one level, recalculates due date, re-evaluates agent. |
| `/api/tickets/{id}/escalations` | `GET` | Bearer | Escalation audit history ordered newest-first. |
| `/api/customers` · `/api/agents` · `/api/categories` | `GET` | Bearer | Reference data. |
| `/health` | `GET` | Anonymous | Container health check verifying database connectivity. |

Errors are `ProblemDetails`: `400` validation, `401` unauthorized, `404` not found, `409` conflict or business rule violation.

**Seed data:** 5 categories, 6 customers (3 Premium), 6 agents (one inactive, one at capacity), and 40 tickets across every status, priority, and SLA state.

---

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| API cannot reach the database | `npm run db:up`, wait ~20 s, check `docker compose ps` |
| Port 1433 already in use | Change the port in `docker-compose.yml` and `.env` |
| Browser warns about the certificate | `dotnet dev-certs https --trust`, or use the web app, which proxies |
| Web app shows "Unable to load tickets" | The API is not running, or `VITE_API_PROXY_TARGET` points at the wrong port |
| PowerShell rejects `VITE_API_PROXY_TARGET=...` | Use `$env:VITE_API_PROXY_TARGET="http://localhost:5080"; npm run web` |
| SQL Server slow on Apple Silicon | Enable Rosetta in Docker Desktop → Settings → General |
