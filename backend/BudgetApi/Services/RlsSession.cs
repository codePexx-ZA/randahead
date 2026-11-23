using System.Data;
using System.Data.Common;
using BudgetApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BudgetApi.Services;

public static class RlsSession
{
    public static Task SetTenantAsync(BudgetDbContext db, string userId, string unitId, string businessId, CancellationToken cancellationToken) =>
        SetAsync(db, userId, unitId, businessId, "", "", cancellationToken);

    public static Task SetLoginAsync(BudgetDbContext db, string username, CancellationToken cancellationToken) =>
        SetAsync(db, "", "", "", username, "", cancellationToken);

    public static Task SetBypassAsync(BudgetDbContext db, CancellationToken cancellationToken) =>
        SetAsync(db, "", "", "", "", "on", cancellationToken);

    public static Task ClearAsync(DbConnection connection, CancellationToken cancellationToken) =>
        ApplyAsync(connection, "", "", "", "", "", cancellationToken);

    private static async Task SetAsync(
        BudgetDbContext db,
        string userId,
        string unitId,
        string businessId,
        string loginUsername,
        string bypass,
        CancellationToken cancellationToken
    )
    {
        if (!db.Database.IsRelational())
        {
            return;
        }
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
        await ApplyAsync(connection, userId, unitId, businessId, loginUsername, bypass, cancellationToken);
    }

    private static async Task ApplyAsync(
        DbConnection connection,
        string userId,
        string unitId,
        string businessId,
        string loginUsername,
        string bypass,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT set_config('app.user_id', @user_id, false),
                   set_config('app.unit_id', @unit_id, false),
                   set_config('app.business_id', @business_id, false),
                   set_config('app.login_username', @login_username, false),
                   set_config('app.bypass', @bypass, false)
            """;
        Add(command, "@user_id", userId);
        Add(command, "@unit_id", unitId);
        Add(command, "@business_id", businessId);
        Add(command, "@login_username", loginUsername);
        Add(command, "@bypass", bypass);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static void Add(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

public sealed class RlsConnectionInterceptor : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await UseApplicationRoleAsync(connection, cancellationToken);
        await RlsSession.ClearAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        UseApplicationRoleAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
        RlsSession.ClearAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
        base.ConnectionOpened(connection, eventData);
    }

    private static async Task UseApplicationRoleAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SET ROLE randahead_app";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
