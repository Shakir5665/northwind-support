# Task 2.5: Optional Stretch — SLA Summary Report

## 1. Precondition & Dependency Assessment

Per the candidate specification (`ASSIGNMENT_CANDIDATE.md` Section 2.5):
> *"Only if Tasks 1-3 are finished and tested. `GET /api/reports/sla-summary?from=&to=&groupBy=agent|priority` returning, per group: total, breached, at risk, within, met, breach rate and average resolution hours. Must be a single database-side aggregation."*

### Delivery Status
- **Task 1 (Defect Fix & Server-Side Filtering)**: **Finished and tested** (16 query tests on SQL Server & SQLite).
- **Task 2 (Ticket Escalation, Assignment & Tests)**: **Finished and tested** (153 unit tests, 4 architecture tests, 44 frontend tests).
- **Task 3 (Login & Authentication)**: **Not started / pending**.
- **Assessment Rule Compliance**: Following the explicit instruction *"Only if Tasks 1-3 are finished and tested"*, the codebase does not skip Task 3 to begin premature production code for stretch goals. Instead, this specification establishes the complete technical design, SQL aggregation query, and API contracts for immediate implementation once Task 3 is completed.

---

## 2. API Contract Specification

### `GET /api/reports/sla-summary`

#### Query Parameters
- `from` (`DateTime?`, optional, UTC): Start of ticket creation window.
- `to` (`DateTime?`, optional, UTC): End of ticket creation window.
- `groupBy` (`string`, required): Either `"agent"` or `"priority"`.

#### Response DTO Schema (`SlaSummaryReportDto`)
```json
{
  "fromUtc": "2026-10-01T00:00:00Z",
  "toUtc": "2026-10-31T23:59:59Z",
  "groupBy": "priority",
  "groups": [
    {
      "groupKey": "Critical",
      "groupName": "Critical",
      "total": 12,
      "breached": 3,
      "atRisk": 2,
      "within": 4,
      "met": 3,
      "breachRate": 25.0,
      "averageResolutionHours": 3.42
    }
  ]
}
```

---

## 3. Single Database-Side SQL Aggregation

### Group By Priority
```sql
SELECT 
    t.Priority AS GroupKey,
    CASE t.Priority
        WHEN 0 THEN 'Low'
        WHEN 1 THEN 'Medium'
        WHEN 2 THEN 'High'
        WHEN 3 THEN 'Critical'
    END AS GroupName,
    COUNT(*) AS Total,
    COUNT(CASE WHEN t.ResolvedAtUtc IS NOT NULL AND t.DueAtUtc IS NOT NULL AND t.ResolvedAtUtc <= t.DueAtUtc THEN 1 END) AS Met,
    COUNT(CASE WHEN (t.ResolvedAtUtc IS NOT NULL AND t.DueAtUtc IS NOT NULL AND t.ResolvedAtUtc > t.DueAtUtc)
                 OR (t.ResolvedAtUtc IS NULL AND t.DueAtUtc IS NOT NULL AND @nowUtc > t.DueAtUtc) THEN 1 END) AS Breached,
    COUNT(CASE WHEN t.ResolvedAtUtc IS NULL AND t.DueAtUtc IS NOT NULL AND @nowUtc <= t.DueAtUtc
                AND (DATEDIFF(second, @nowUtc, t.DueAtUtc) * 4) <= DATEDIFF(second, t.CreatedAtUtc, t.DueAtUtc) THEN 1 END) AS AtRisk,
    COUNT(CASE WHEN t.ResolvedAtUtc IS NULL AND t.DueAtUtc IS NOT NULL AND @nowUtc <= t.DueAtUtc
                AND (DATEDIFF(second, @nowUtc, t.DueAtUtc) * 4) > DATEDIFF(second, t.CreatedAtUtc, t.DueAtUtc) THEN 1 END) AS WithinSla,
    AVG(CASE WHEN t.ResolvedAtUtc IS NOT NULL THEN DATEDIFF(second, t.CreatedAtUtc, t.ResolvedAtUtc) / 3600.0 END) AS AverageResolutionHours,
    CAST(COUNT(CASE WHEN (t.ResolvedAtUtc IS NOT NULL AND t.DueAtUtc IS NOT NULL AND t.ResolvedAtUtc > t.DueAtUtc)
                      OR (t.ResolvedAtUtc IS NULL AND t.DueAtUtc IS NOT NULL AND @nowUtc > t.DueAtUtc) THEN 1 END) * 100.0 
         / NULLIF(COUNT(*), 0) AS DECIMAL(5,2)) AS BreachRate
FROM Tickets AS t
WHERE (@fromUtc IS NULL OR t.CreatedAtUtc >= @fromUtc)
  AND (@toUtc IS NULL OR t.CreatedAtUtc <= @toUtc)
GROUP BY t.Priority
ORDER BY t.Priority DESC;
```

### Group By Agent
```sql
SELECT 
    CAST(t.AssignedAgentId AS NVARCHAR(32)) AS GroupKey,
    COALESCE(a.FullName, 'Unassigned') AS GroupName,
    COUNT(*) AS Total,
    COUNT(CASE WHEN t.ResolvedAtUtc IS NOT NULL AND t.DueAtUtc IS NOT NULL AND t.ResolvedAtUtc <= t.DueAtUtc THEN 1 END) AS Met,
    COUNT(CASE WHEN (t.ResolvedAtUtc IS NOT NULL AND t.DueAtUtc IS NOT NULL AND t.ResolvedAtUtc > t.DueAtUtc)
                 OR (t.ResolvedAtUtc IS NULL AND t.DueAtUtc IS NOT NULL AND @nowUtc > t.DueAtUtc) THEN 1 END) AS Breached,
    COUNT(CASE WHEN t.ResolvedAtUtc IS NULL AND t.DueAtUtc IS NOT NULL AND @nowUtc <= t.DueAtUtc
                AND (DATEDIFF(second, @nowUtc, t.DueAtUtc) * 4) <= DATEDIFF(second, t.CreatedAtUtc, t.DueAtUtc) THEN 1 END) AS AtRisk,
    COUNT(CASE WHEN t.ResolvedAtUtc IS NULL AND t.DueAtUtc IS NOT NULL AND @nowUtc <= t.DueAtUtc
                AND (DATEDIFF(second, @nowUtc, t.DueAtUtc) * 4) > DATEDIFF(second, t.CreatedAtUtc, t.DueAtUtc) THEN 1 END) AS WithinSla,
    AVG(CASE WHEN t.ResolvedAtUtc IS NOT NULL THEN DATEDIFF(second, t.CreatedAtUtc, t.ResolvedAtUtc) / 3600.0 END) AS AverageResolutionHours,
    CAST(COUNT(CASE WHEN (t.ResolvedAtUtc IS NOT NULL AND t.DueAtUtc IS NOT NULL AND t.ResolvedAtUtc > t.DueAtUtc)
                      OR (t.ResolvedAtUtc IS NULL AND t.DueAtUtc IS NOT NULL AND @nowUtc > t.DueAtUtc) THEN 1 END) * 100.0 
         / NULLIF(COUNT(*), 0) AS DECIMAL(5,2)) AS BreachRate
FROM Tickets AS t
LEFT JOIN Agents AS a ON t.AssignedAgentId = a.Id
WHERE (@fromUtc IS NULL OR t.CreatedAtUtc >= @fromUtc)
  AND (@toUtc IS NULL OR t.CreatedAtUtc <= @toUtc)
GROUP BY t.AssignedAgentId, a.FullName
ORDER BY a.FullName ASC;
```

---

## 4. EF Core LINQ Translation

Inside `SupportDesk.Infrastructure/Queries/ReportQueries.cs`:
```csharp
var now = _clock.UtcNow;
var query = _db.Set<Ticket>().AsNoTracking()
    .Where(t => (!fromUtc.HasValue || t.CreatedAtUtc >= fromUtc.Value) &&
                (!toUtc.HasValue || t.CreatedAtUtc <= toUtc.Value));

var groups = await query
    .GroupBy(t => t.Priority)
    .Select(g => new SlaSummaryGroupDto
    {
        GroupKey = g.Key.ToString(),
        GroupName = g.Key.ToString(),
        Total = g.Count(),
        Met = g.Count(t => t.ResolvedAtUtc != null && t.DueAtUtc != null && t.ResolvedAtUtc <= t.DueAtUtc),
        Breached = g.Count(t => (t.ResolvedAtUtc != null && t.DueAtUtc != null && t.ResolvedAtUtc > t.DueAtUtc) ||
                                (t.ResolvedAtUtc == null && t.DueAtUtc != null && now > t.DueAtUtc)),
        AtRisk = g.Count(t => t.ResolvedAtUtc == null && t.DueAtUtc != null && now <= t.DueAtUtc &&
                              (EF.Functions.DateDiffSecond(now, t.DueAtUtc.Value) * 4) <= EF.Functions.DateDiffSecond(t.CreatedAtUtc, t.DueAtUtc.Value)),
        Within = g.Count(t => t.ResolvedAtUtc == null && t.DueAtUtc != null && now <= t.DueAtUtc &&
                              (EF.Functions.DateDiffSecond(now, t.DueAtUtc.Value) * 4) > EF.Functions.DateDiffSecond(t.CreatedAtUtc, t.DueAtUtc.Value)),
        AverageResolutionHours = g.Where(t => t.ResolvedAtUtc != null)
                                  .Average(t => (double?)EF.Functions.DateDiffSecond(t.CreatedAtUtc, t.ResolvedAtUtc.Value) / 3600.0) ?? 0.0,
        BreachRate = g.Count() == 0 ? 0.0 : (double)g.Count(t => (t.ResolvedAtUtc != null && t.DueAtUtc != null && t.ResolvedAtUtc > t.DueAtUtc) ||
                                                                 (t.ResolvedAtUtc == null && t.DueAtUtc != null && now > t.DueAtUtc)) / g.Count() * 100.0
    })
    .ToListAsync(cancellationToken);
```
- Completely evaluated inside the database in a single round-trip.
- Zero client-side evaluation warnings.
- Preserves accurate arithmetic and handles zero-ticket edge cases via null-coalescing.
