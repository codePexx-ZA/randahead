namespace BudgetApi.Tests;

public class RowLevelSecurityTests
{
    [Fact]
    public void TenantTablesForceRowLevelSecurity()
    {
        var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schema", "04_row_level_security.sql"));
        string[] tables =
        [
            "business", "business_unit", "app_user", "budget_submission", "financial_line_item",
            "submission_row", "import_mapping", "forecast_run", "forecast_value", "budget_selection",
            "forecast_input", "kpi_target", "rule_definition", "rule_alert", "audit_log",
        ];
        foreach (var table in tables)
        {
            Assert.Contains("CREATE ROLE randahead_app NOLOGIN;", sql);
            Assert.Contains($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;", sql);
            Assert.Contains($"CREATE POLICY tenant_isolation ON {table}", sql);
        }
    }
}
