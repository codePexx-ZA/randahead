// Schema tests: every table, column, column type and primary key EF Core maps exists in database/init/01_schema.sql
using System.Text.RegularExpressions;
using BudgetApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BudgetApi.Tests;

public partial class SchemaTests
{
    private record SqlTable(Dictionary<string, string> Columns, string[] Key);

    [GeneratedRegex(@"CREATE TABLE (\w+) \((.*?)\n\);", RegexOptions.Singleline)]
    private static partial Regex CreateTable();

    [GeneratedRegex(@"^(\w+)\s+([A-Z]+(?:\(\d+(?:,\d+)?\))?(?:\[\])?)(.*)$")]
    private static partial Regex ColumnLine();

    [GeneratedRegex(@"^PRIMARY KEY \((.*)\)")]
    private static partial Regex KeyLine();

    [Fact]
    public void EfModel_MatchesSchemaSql()
    {
        var tables = ReadSchema(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schema", "01_schema.sql")));
        using var db = new BudgetDbContext(
            new DbContextOptionsBuilder<BudgetDbContext>().UseNpgsql("Host=unused").UseSnakeCaseNamingConvention().Options
        );
        var problems = new List<string>();

        foreach (var entity in db.Model.GetEntityTypes())
        {
            var name = entity.GetTableName()!;
            if (!tables.TryGetValue(name, out var table))
            {
                problems.Add($"{entity.ClrType.Name}: no table {name}");
                continue;
            }
            var store = StoreObjectIdentifier.Table(name);
            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName(store)!;
                if (!table.Columns.TryGetValue(column, out var sqlType))
                {
                    problems.Add($"{name}.{column}: not in the schema");
                }
                else if (EfType(sqlType) != property.GetColumnType())
                {
                    problems.Add($"{name}.{column}: {sqlType} in the schema, {property.GetColumnType()} in EF");
                }
            }
            var key = entity.FindPrimaryKey()!.Properties.Select(p => p.GetColumnName(store)!).ToArray();
            if (!key.SequenceEqual(table.Key))
            {
                problems.Add($"{name}: primary key ({string.Join(", ", key)}) in EF, ({string.Join(", ", table.Key)}) in the schema");
            }
        }

        Assert.Empty(problems);
    }

    private static Dictionary<string, SqlTable> ReadSchema(string sql) =>
        CreateTable()
            .Matches(sql.Replace("\r\n", "\n"))
            .ToDictionary(
                match => match.Groups[1].Value,
                match =>
                {
                    var lines = match.Groups[2].Value.Split('\n').Select(line => line.Trim()).ToList();
                    var columns = lines.Select(line => ColumnLine().Match(line)).Where(m => m.Success && m.Groups[1].Value is not ("CHECK" or "UNIQUE" or "PRIMARY"));
                    var key = columns.Where(m => m.Groups[3].Value.Contains("PRIMARY KEY")).Select(m => m.Groups[1].Value).ToArray();
                    if (key.Length == 0 && lines.Select(line => KeyLine().Match(line)).FirstOrDefault(m => m.Success) is { } keyLine)
                    {
                        key = keyLine.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries);
                    }
                    return new SqlTable(columns.ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value), key);
                }
            );

    // Lengths and precisions only document the schema here, so EF maps them as the bare type
    private static string EfType(string sqlType) =>
        sqlType switch
        {
            _ when sqlType.EndsWith("[]") => sqlType.ToLowerInvariant(),
            _ when sqlType.StartsWith("VARCHAR") || sqlType.StartsWith("CHAR") => "text",
            _ when sqlType.StartsWith("NUMERIC") => "numeric",
            "TIMESTAMPTZ" => "timestamp with time zone",
            _ => sqlType.ToLowerInvariant(),
        };
}
