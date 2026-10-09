// Business scope: finds the signed-in user's account and business, and what that business has on record
using System.Security.Claims;
using BudgetApi.Data;
using BudgetApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Services;

public class BusinessScope(BudgetDbContext db)
{
    public Task<AppUser?> CallerAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userId = principal.FindFirst("sub")?.Value;
        return db
            .AppUsers.Include(u => u.Unit!)
            .ThenInclude(unit => unit.Business)
            .FirstOrDefaultAsync(
                u => u.UserId == userId && u.ActiveStatus == "active" && u.DeletedAt == null,
                cancellationToken
            );
    }

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
