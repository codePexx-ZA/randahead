using BudgetApi.Data;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Services;

public class BusinessScope(BudgetDbContext db)
{
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

}
