// Permissions: the roles, what each one may do (an admin may do everything) and the attribute that guards an endpoint with them
using BudgetApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BudgetApi.Services;

public static class Roles
{
    public const string Submitter = "submitter";
    public const string Approver = "approver";
    public const string DecisionMaker = "decision_maker";

    public static readonly string[] All = [Submitter, Approver, DecisionMaker];
}

public static class Permissions
{
    public const string Upload = "upload";
    public const string ChooseBudget = "chooseBudget";
    public const string Decide = "decide";
    public const string EditTargets = "editTargets";
    public const string EditRules = "editRules";
    public const string ManageUsers = "manageUsers";
    public const string EditBusiness = "editBusiness";
    public const string ViewReports = "viewReports";
    public const string DeleteAnyUser = "deleteAnyUser";
    public const string RunIngestion = "runIngestion";
    public const string ViewStatement = "viewStatement";

    private static readonly (string Permission, string[] Roles)[] Grants =
    [
        (Upload, [Roles.Submitter]),
        (ChooseBudget, [Roles.DecisionMaker]),
        (Decide, [Roles.Approver]),
        (EditTargets, [Roles.DecisionMaker]),
        (EditRules, [Roles.Approver, Roles.DecisionMaker]),
        (ManageUsers, [Roles.Approver, Roles.DecisionMaker]),
        (EditBusiness, [Roles.DecisionMaker]),
        (ViewReports, [Roles.DecisionMaker]),
        (DeleteAnyUser, []),
        (RunIngestion, [Roles.Approver, Roles.DecisionMaker]),
        (ViewStatement, Roles.All),
    ];

    public static bool Allows(AppUser user, string permission) =>
        Grants.Any(g => g.Permission == permission && (user.IsAdmin || g.Roles.Contains(user.Role)));

    public static string[] For(AppUser user) => Grants.Select(g => g.Permission).Where(p => Allows(user, p)).ToArray();

    // Approvers manage Submitters only, so one person can't control both choosing and approving a budget
    public static bool CanManage(AppUser caller, string targetRole) =>
        Allows(caller, ManageUsers) && (caller.IsAdmin || caller.Role == Roles.DecisionMaker || targetRole == Roles.Submitter);

    public static string[] ManageableRoles(AppUser user) => Roles.All.Where(role => CanManage(user, role)).ToArray();
}

// Runs before model binding, so a caller without the permission gets 403 even when the body is invalid
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequirePermissionAttribute(string permission, string message = "Your role can't do this.")
    : Attribute,
        IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.FindCaller() is not { } caller || !Permissions.Allows(caller, permission))
        {
            context.Result = new ObjectResult(new { message }) { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
