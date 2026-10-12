// Statement tests: reading the sample income statement
using BudgetApi.Services;

namespace BudgetApi.Tests;

public class StatementTests
{
    [Fact]
    public void Read_SampleStatement_FindsEveryYearMatchesEveryLineAndAddsUp()
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "samples", "Karoo_Outdoor_IS_FY2021-FY2025.xlsx"));

        var preview = StatementImporter.Read(file, "Karoo_Outdoor_IS_FY2021-FY2025.xlsx", new Dictionary<string, MappingPart[]>());

        Assert.Equal(["FY2021", "FY2022", "FY2023", "FY2024", "FY2025"], preview.Years);
        var lines = preview.Rows.Where(r => r.Kind == "line").ToList();
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.NotNull(line.Mapping));
        Assert.NotNull(preview.Statement);
        Assert.NotEmpty(preview.Statement.Checks!);
        Assert.All(preview.Statement.Checks!.Values, check => Assert.True(check.Matches));
    }
}
