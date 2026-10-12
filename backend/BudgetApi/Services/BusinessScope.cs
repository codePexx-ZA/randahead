// Business scope: locks a business unit and checks what a business has on record
using BudgetApi.Data;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Services;

public class BusinessScope(BudgetDbContext db)
{
    // Holds a row lock until the surrounding transaction ends, so uploads and decisions for one unit run one at a time
    public Task LockUnitAsync(string unitId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT 1 FROM business_unit WHERE unit_id = {unitId} FOR UPDATE", cancellationToken);

    public Task<bool> HasApprovedHistoryAsync(string businessId, CancellationToken cancellationToken) =>
        db
            .Database.SqlQuery<bool>(
                $"""
                SELECT EXISTS (
                    SELECT 1 FROM budget_submission s
                    JOIN business_unit bu ON bu.unit_id = s.unit_id
                    WHERE bu.business_id = {businessId} AND s.status IN ('approved', 'superseded')
                ) AS "Value"
                """
            )
            .SingleAsync(cancellationToken);

    public Task<bool> HasYearDatedDataAsync(string businessId, CancellationToken cancellationToken) =>
        db
            .Database.SqlQuery<bool>(
                $"""
                SELECT EXISTS (
                    SELECT 1 FROM budget_submission s
                    JOIN business_unit bu ON bu.unit_id = s.unit_id
                    WHERE bu.business_id = {businessId} AND s.status <> 'rejected'
                ) OR EXISTS (
                    SELECT 1 FROM kpi_target k
                    JOIN business_unit bu ON bu.unit_id = k.unit_id
                    WHERE bu.business_id = {businessId}
                ) AS "Value"
                """
            )
            .SingleAsync(cancellationToken);

    public async Task<HashSet<string>> UsersOnRecordAsync(string businessId, CancellationToken cancellationToken) =>
        (
            await db
                .Database.SqlQuery<string>(
                    $"""
                    SELECT u.user_id AS "Value" FROM app_user u
                    JOIN business_unit bu ON bu.unit_id = u.unit_id
                    WHERE bu.business_id = {businessId} AND (
                        EXISTS (SELECT 1 FROM budget_submission s WHERE s.submitted_by = u.user_id)
                        OR EXISTS (SELECT 1 FROM approval_action a WHERE a.acted_by = u.user_id)
                        OR EXISTS (SELECT 1 FROM import_mapping m WHERE m.created_by = u.user_id)
                        OR EXISTS (SELECT 1 FROM budget_selection b WHERE b.chosen_by = u.user_id)
                    )
                    """
                )
                .ToListAsync(cancellationToken)
        ).ToHashSet();
}
