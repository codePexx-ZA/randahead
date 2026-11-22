using BudgetApi.Services;
using ClosedXML.Excel;

namespace BudgetApi.Tests;

/// <summary>
/// Management-accounts workbooks: one sheet per financial year in any tab order, month columns,
/// account codes, and clutter around the income statement. All figures here are made up.
/// </summary>
public class StoreWorkbookTests
{
    private static readonly Dictionary<string, MappingPart[]> NoMappings = new();

    [Fact]
    public void Read_StoreWorkbook_UsesEveryFullYearSheetAndIgnoresTheClutter()
    {
        var preview = Read(StoreWorkbook());

        Assert.Equal(["FY2024", "FY2025", "FY2026"], preview.Years);
        Assert.Equal("annual", preview.Frequency);
        Assert.Equal("Test Store Boardwalk", preview.Title);
        Assert.Contains(preview.Notes, n => n.StartsWith("FY2027 is left out") && n.Contains("Mar 2026–Aug 2026"));

        var sheets = preview.Sheets!.ToDictionary(s => s.Name);
        Assert.Equal(["Data", "summary", "Store Feb 2025", "Store Feb 2027", "Store Feb 2026", "Store Feb 2024", "FNB LOAN"], preview.Sheets!.Select(s => s.Name));
        Assert.False(sheets["Data"].Used);
        Assert.Contains("hidden", sheets["Data"].Detail);
        Assert.Equal("no month columns", sheets["summary"].Detail);
        Assert.Equal("no month columns", sheets["FNB LOAN"].Detail);
        Assert.False(sheets["Store Feb 2027"].Used);
        Assert.StartsWith("only part of a year", sheets["Store Feb 2027"].Detail);
        Assert.True(sheets["Store Feb 2026"].Used);
        Assert.StartsWith("FY2026 (Mar 2025–Feb 2026)", sheets["Store Feb 2026"].Detail);
        Assert.True(sheets["Store Feb 2024"].Used);
    }

    [Fact]
    public void Read_StoreWorkbook_KeepsTheAccountsAndSkipsCalculatedAndBalanceSheetRows()
    {
        var preview = Read(StoreWorkbook());
        var lines = preview.Rows.Where(r => r.Kind == "line").ToDictionary(r => r.Label);

        Assert.Equal(
            ["Sales - Bank", "Sales - Cash", "Cost of Sales", "Accounting Fees - Bma", "Rent Paid - Basic Rental", "Salaries & Wages - Salaries", "Interest Received", "Normal Taxation", "Dividends Declared / Paid"],
            lines.Keys
        );
        Assert.Equal([720m, 720m, 720m], lines["Sales - Bank"].Values);
        Assert.Equal("cost_of_sales", lines["Cost of Sales"].Mapping!.Category);
        Assert.Equal("income_tax", lines["Normal Taxation"].Mapping!.Category);
        Assert.Equal("ignore", lines["Dividends Declared / Paid"].Mapping!.Type);
        Assert.All(lines.Values, line => Assert.NotNull(line.Mapping));
        Assert.Contains(preview.Info!, i => i.Contains("calculated rows were skipped"));
        Assert.Contains(preview.Info!, i => i.Contains("3 accounts with no amounts"));
    }

    [Fact]
    public void Read_StoreWorkbook_AddsUpAndMatchesTheSheetsOwnProfitRows()
    {
        var preview = Read(StoreWorkbook());

        Assert.NotNull(preview.Statement);
        Assert.Equal([1200m, 1200m, 1200m], preview.Statement.Totals["revenue"]);
        Assert.Equal([600m, 600m, 600m], preview.Statement.Totals["cost_of_sales"]);
        Assert.Equal([264m, 264m, 264m], preview.Statement.Subtotals["profit_before_tax"]);
        Assert.True(preview.Statement.Checks!["gross_profit"].Matches);
        Assert.True(preview.Statement.Checks["profit_before_tax"].Matches);
        Assert.False(preview.Statement.Checks.ContainsKey("net_profit"));
        Assert.Contains(preview.Info!, i => i.Contains("after dividends"));
    }

    [Fact]
    public void Read_CalendarYearSheetsWithTextMonths_FollowTheBusinessYearEnd()
    {
        var dec = Read(CalendarWorkbook(), yearEndMonth: 12);
        Assert.Equal(["FY2024", "FY2025"], dec.Years);
        Assert.DoesNotContain(dec.Notes, n => n.Contains("regrouped"));
        Assert.Equal([1200m, 1320m], dec.Statement!.Totals["revenue"]);

        var february = Read(CalendarWorkbook(), yearEndMonth: 2);
        Assert.Equal(["FY2025"], february.Years);
        Assert.Contains(february.Notes, n => n.Contains("run to December") && n.Contains("regrouped"));
        Assert.Contains(february.Notes, n => n.StartsWith("FY2024 is left out"));
        Assert.Contains(february.Notes, n => n.StartsWith("FY2026 is left out"));
        Assert.Equal([100m * 10 + 110m * 2], february.Statement!.Totals["revenue"]);
    }

    [Fact]
    public void Read_OnlyPartOfAYear_SaysWhichMonthsAreThere()
    {
        using var workbook = new XLWorkbook();
        StoreSheet(workbook, "Store Feb 2027", 2027, label: "Accounting Fees - Bma", filledMonths: 4, numericDates: false);

        var error = Assert.Throws<StatementImportException>(() => Read(Save(workbook)));

        Assert.Contains("No complete financial year found", error.Message);
        Assert.Contains("Mar 2026–Jun 2026", error.Message);
    }

    [Fact]
    public void Read_AnnualStatement_StillUsesTheYearColumns()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("IS");
        sheet.Cell(1, 2).Value = "FY2024";
        sheet.Cell(1, 3).Value = "FY2025";
        (string Label, double A, double B)[] rows = [("Sales", 1000, 1100), ("Cost of sales", 400, 450), ("Gross profit", 600, 650)];
        for (var r = 0; r < rows.Length; r++)
        {
            sheet.Cell(r + 2, 1).Value = rows[r].Label;
            sheet.Cell(r + 2, 2).Value = rows[r].A;
            sheet.Cell(r + 2, 3).Value = rows[r].B;
        }

        var preview = Read(Save(workbook));

        Assert.Equal(["FY2024", "FY2025"], preview.Years);
        Assert.Null(preview.Sheets);
        Assert.True(preview.Statement!.Checks!["gross_profit"].Matches);
    }

    [Theory]
    [InlineData("Rent Paid - Rates & Taxes", "occupancy")]
    [InlineData("Rent Paid - Security", "occupancy")]
    [InlineData("Staff Welfare - Uniforms & Name Badges", "employee_costs")]
    [InlineData("Staff Wellfare - Accommodation", "employee_costs")]
    [InlineData("Motor Vehicle - Insurance & Licence", "transport_fuel")]
    [InlineData("Dividends Declared / Paid", "ignore")]
    [InlineData("Interest Received", "other_income")]
    public void Read_AccountNamesGroupedWithADash_TakeTheGroupsCategory(string label, string expected)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("IS");
        sheet.Cell(1, 2).Value = "FY2024";
        sheet.Cell(1, 3).Value = "FY2025";
        sheet.Cell(2, 1).Value = "Sales";
        sheet.Cell(3, 1).Value = label;
        foreach (var row in new[] { 2, 3 })
        {
            sheet.Cell(row, 2).Value = 100;
            sheet.Cell(row, 3).Value = 100;
        }

        var line = Read(Save(workbook)).Rows.Single(r => r.Label == label);

        Assert.Equal(expected, line.Mapping!.Category ?? line.Mapping.Type);
    }

    private static StatementPreview Read(MemoryStream file, int yearEndMonth = 2) =>
        StatementImporter.Read(file, "store.xlsx", NoMappings, yearEndMonth);

    private static MemoryStream Save(XLWorkbook workbook)
    {
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream StoreWorkbook()
    {
        using var workbook = new XLWorkbook();
        var data = StoreSheet(workbook, "Data", 2026, label: "Accounting Fees - Bma", filledMonths: 12, numericDates: false);
        data.Hide();
        var summary = workbook.AddWorksheet("summary ");
        summary.Cell(3, 1).Value = "Net Profit Margin";
        summary.Cell(3, 2).Value = 0.1;
        summary.Hide();
        StoreSheet(workbook, "Store Feb 2025", 2025, label: "Accounting Fees - Bruce Morrison", filledMonths: 12, numericDates: true);
        StoreSheet(workbook, "Store Feb 2027", 2027, label: "Accounting Fees - Bma", filledMonths: 6, numericDates: false);
        StoreSheet(workbook, "Store Feb 2026", 2026, label: "Accounting Fees - Bma", filledMonths: 12, numericDates: false);
        StoreSheet(workbook, "Store Feb 2024", 2024, label: "Accounting Fees - Bruce Morrison", filledMonths: 12, numericDates: true);
        var loan = workbook.AddWorksheet("FNB LOAN");
        loan.Cell(4, 2).Value = "Date";
        loan.Cell(4, 3).Value = "Opening balance";
        for (var i = 0; i < 12; i++)
        {
            loan.Cell(6 + i, 2).Value = new DateTime(2025, 3, 1).AddMonths(i);
            loan.Cell(6 + i, 3).Value = 5000 - i * 100;
        }
        return Save(workbook);
    }

    /// <summary>A year sheet laid out like a management-accounts pack: title, prior-year summary, then the statement for Mar–Feb.</summary>
    private static IXLWorksheet StoreSheet(XLWorkbook workbook, string name, int fiscalYear, string label, int filledMonths, bool numericDates)
    {
        var sheet = workbook.AddWorksheet(name);
        var first = new DateTime(fiscalYear - 1, 3, 1);
        sheet.Cell(1, 1).Value = "Test Store Boardwalk";

        void MonthHeader(int row, DateTime start)
        {
            for (var i = 0; i < 12; i++)
            {
                var cell = sheet.Cell(row, 3 + i);
                if (numericDates)
                {
                    cell.Value = start.AddMonths(i).ToOADate();
                    cell.Style.NumberFormat.Format = "mmm-yy";
                }
                else
                {
                    cell.Value = start.AddMonths(i);
                }
            }
            sheet.Cell(row, 15).Value = "Total";
        }

        // Prior-year summary: same months as the previous sheet, different numbers. Must never be used.
        MonthHeader(3, first.AddYears(-1));
        sheet.Cell(7, 1).Value = "Prior year Figures for analysis";
        object?[][] prior = [[null, "Sales Including", 777], [null, "Sales Excluding", 666], [null, "GROSS PROFIT / (LOSS)", 333]];

        sheet.Cell(15, 1).Value = $"Mar {fiscalYear - 1} - Feb {fiscalYear} - Figures";
        MonthHeader(17, first);
        for (var i = 0; i < 12; i++)
        {
            sheet.Cell(18, 3 + i).Value = "Actual";
        }

        object?[][] rows =
        [
            [null, "Sales Including", 115],
            [null, "Sales Excluding", 100],
            ["1000000", "Sales - Bank", 60],
            ["1000010", "Sales - Cash", 40],
            ["1000020", "Sales - IBT (Other)", null],
            [null, "Total sales (Excluding Vat)", 100],
            [null, "Monthly Sales % Increase / (Decrease)", 0.05],
            [null, "Cost of Sales", 50],
            ["2000000", "Operating Costs / Cost of Sales", null],
            ["2000010", "Purchase Variance", 0],
            [null, "GROSS PROFIT / (LOSS)", 50],
            [null, "GROSS PROFIT %", 0.5],
            [null, "Expenses", 30],
            ["3000000", label, 5],
            ["3000010", "Rent Paid - Basic Rental", 15],
            ["3000020", "Salaries & Wages - Salaries", 10],
            [null, null, 2],
            ["4000000", "Interest Received", 2],
            [null, "NET PROFIT / (LOSS) BEFORE TAX", 22],
            [null, "Tax", 6],
            ["4500000", "Normal Taxation", 6],
            [null, "Dividends", 1],
            ["4600000", "Dividends Declared / Paid", 1],
            [null, "NET PROFIT / (LOSS) AFTER TAX", 15],
            [null, "Stock On Hand Ex", 999],
            ["5200/000", "Retained Income / (Accumulated Loss)", 999],
            ["8400/000", "First National Bank", 999],
            [null, "Net Profit Margin", 0.1],
        ];

        void Write(int startRow, object?[][] table, int months)
        {
            for (var r = 0; r < table.Length; r++)
            {
                var (code, text, amount) = (table[r][0], table[r][1], table[r][2]);
                if (code is not null)
                {
                    sheet.Cell(startRow + r, 1).Value = (string)code;
                }
                if (text is not null)
                {
                    sheet.Cell(startRow + r, 2).Value = (string)text;
                }
                for (var i = 0; i < 12 && amount is not null; i++)
                {
                    sheet.Cell(startRow + r, 3 + i).Value = i < months ? Convert.ToDouble(amount) : 0;
                }
            }
        }

        Write(8, prior, 12);
        Write(19, rows, filledMonths);
        return sheet;
    }

    private static MemoryStream CalendarWorkbook()
    {
        using var workbook = new XLWorkbook();
        foreach (var (year, sales) in new[] { (2024, 100), (2025, 110) })
        {
            var sheet = workbook.AddWorksheet(year.ToString());
            string[] months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
            for (var i = 0; i < 12; i++)
            {
                sheet.Cell(2, 2 + i).Value = $"{months[i]}-{year % 100:00}";
            }
            object[][] rows = [["Revenue", sales], ["Cost of sales", 40], ["Gross profit", sales - 40], ["Rent", 20], ["Rent % of sales", 0.2]];
            for (var r = 0; r < rows.Length; r++)
            {
                sheet.Cell(3 + r, 1).Value = (string)rows[r][0];
                for (var i = 0; i < 12; i++)
                {
                    sheet.Cell(3 + r, 2 + i).Value = Convert.ToDouble(rows[r][1]);
                }
            }
        }
        return Save(workbook);
    }
}
