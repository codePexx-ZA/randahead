// Permissions tests: what each role and an admin may do, and which roles they may manage
using BudgetApi.Models;
using BudgetApi.Services;

namespace BudgetApi.Tests;

public class PermissionsTests
{
    private static readonly string[] Everything =
    [
        Permissions.Upload,
        Permissions.ChooseBudget,
        Permissions.Decide,
        Permissions.EditTargets,
        Permissions.EditRules,
        Permissions.ManageUsers,
        Permissions.EditBusiness,
        Permissions.ViewReports,
        Permissions.DeleteAnyUser,
        Permissions.RunIngestion,
        Permissions.ViewStatement,
    ];

    [Fact]
    public void PermissionNames_MatchWhatTheWebAppChecks()
    {
        Assert.Equal(
            [
                "upload", "chooseBudget", "decide", "editTargets", "editRules", "manageUsers", "editBusiness", "viewReports", "deleteAnyUser",
                "runIngestion", "viewStatement",
            ],
            Everything
        );
    }

    [Theory]
    [InlineData(Roles.Submitter, new[] { Permissions.Upload, Permissions.ViewStatement })]
    [InlineData(
        Roles.Approver,
        new[] { Permissions.Decide, Permissions.EditRules, Permissions.ManageUsers, Permissions.RunIngestion, Permissions.ViewStatement }
    )]
    [InlineData(
        Roles.DecisionMaker,
        new[]
        {
            Permissions.ChooseBudget,
            Permissions.EditTargets,
            Permissions.EditRules,
            Permissions.ManageUsers,
            Permissions.EditBusiness,
            Permissions.ViewReports,
            Permissions.RunIngestion,
            Permissions.ViewStatement,
        }
    )]
    public void For_EachRole_GetsOnlyItsPermissions(string role, string[] expected)
    {
        var user = User(role);

        Assert.Equal(expected, Permissions.For(user));
        Assert.All(Everything, p => Assert.Equal(expected.Contains(p), Permissions.Allows(user, p)));
    }

    [Theory]
    [InlineData(Roles.Submitter)]
    [InlineData(Roles.Approver)]
    [InlineData(Roles.DecisionMaker)]
    public void For_Admin_GetsEveryPermissionWhateverTheRole(string role)
    {
        var admin = User(role, isAdmin: true);

        Assert.Equal(Everything, Permissions.For(admin));
        Assert.All(Everything, p => Assert.True(Permissions.Allows(admin, p)));
    }

    [Fact]
    public void Allows_UnknownPermission_IsRefusedEvenForAdmin()
    {
        Assert.False(Permissions.Allows(User(Roles.DecisionMaker, isAdmin: true), "doAnything"));
    }

    [Theory]
    [InlineData(Roles.Submitter, false, new string[0])]
    [InlineData(Roles.Approver, false, new[] { Roles.Submitter })]
    [InlineData(Roles.DecisionMaker, false, new[] { Roles.Submitter, Roles.Approver, Roles.DecisionMaker })]
    [InlineData(Roles.Submitter, true, new[] { Roles.Submitter, Roles.Approver, Roles.DecisionMaker })]
    [InlineData(Roles.Approver, true, new[] { Roles.Submitter, Roles.Approver, Roles.DecisionMaker })]
    public void CanManageAndManageableRoles_Agree(string role, bool isAdmin, string[] expected)
    {
        var caller = User(role, isAdmin);

        Assert.Equal(expected, Permissions.ManageableRoles(caller));
        Assert.All(Roles.All, target => Assert.Equal(expected.Contains(target), Permissions.CanManage(caller, target)));
    }

    private static AppUser User(string role, bool isAdmin = false) => new() { Role = role, IsAdmin = isAdmin };
}
