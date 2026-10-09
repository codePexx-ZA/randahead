// Financial year: converts between labels like "FY2025" and the date the year starts, for a given year-end month
namespace BudgetApi.Services;

public static class FinancialYear
{
    public static DateOnly Start(string label, int yearEndMonth)
    {
        var year = int.Parse(label[2..]);
        return new DateOnly(yearEndMonth == 12 ? year : year - 1, yearEndMonth % 12 + 1, 1);
    }

    public static string Label(DateOnly start, int yearEndMonth) =>
        $"FY{(yearEndMonth == 12 ? start.Year : start.Year + 1)}";
}
