// Statement template: builds the standard Excel income statement (categories down, the last five financial years across)
using ClosedXML.Excel;

namespace BudgetApi.Services;

public static class StatementTemplate
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const int HeaderRow = 4;
    private const string AmountFormat = "#,##0;(#,##0);-";
    private static readonly XLColor Blue = XLColor.FromHtml("#3368A0");
    private static readonly XLColor Mint = XLColor.FromHtml("#C8DFDB");
    private static readonly XLColor Cream = XLColor.FromHtml("#F2EFE7");

    private static readonly string[] OperatingExpenses =
    [
        "employee_costs", "occupancy", "electricity", "other_utilities", "transport_fuel", "marketing", "other_operating", "depreciation",
    ];

    public static byte[] Build(string businessName, int yearEndMonth, IReadOnlyDictionary<string, string> categoryNames, DateOnly today)
    {
        var lastYear = today.Month > yearEndMonth ? today.Year : today.Year - 1;
        var years = Enumerable.Range(lastYear - StatementImporter.MaxYears + 1, StatementImporter.MaxYears).ToArray();
        var lastColumn = years.Length + 1;

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Income statement");
        sheet.Cell(1, 1).Value = $"{businessName}: income statement";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(Blue);
        sheet.Cell(2, 1).Value = "Amounts in R'000. Type income and expenses as positive numbers; the subtotal rows add up by themselves.";
        sheet.Cell(2, 1).Style.Font.SetItalic();

        sheet.Cell(HeaderRow, 1).Value = "Line";
        for (var i = 0; i < years.Length; i++)
        {
            sheet.Cell(HeaderRow, i + 2).Value = $"FY{years[i]}";
        }
        var header = sheet.Range(HeaderRow, 1, HeaderRow, lastColumn);
        header.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(Blue);
        header.Range(1, 2, 1, years.Length + 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

        var row = HeaderRow;
        var at = new Dictionary<string, int>();

        void Line(string code)
        {
            row++;
            sheet.Cell(row, 1).Value = categoryNames[code];
            sheet.Range(row, 2, row, lastColumn).Style.Fill.SetBackgroundColor(Cream);
            at[code] = row;
        }

        void Subtotal(string key, string label, Func<string, string> formula)
        {
            row++;
            sheet.Cell(row, 1).Value = label;
            for (var col = 2; col <= lastColumn; col++)
            {
                sheet.Cell(row, col).FormulaA1 = formula(XLHelper.GetColumnLetterFromNumber(col));
            }
            sheet.Range(row, 1, row, lastColumn).Style.Font.SetBold().Fill.SetBackgroundColor(Mint);
            at[key] = row;
        }

        Line("revenue");
        Line("cost_of_sales");
        Subtotal("gross_profit", "Gross profit", c => $"{c}{at["revenue"]}-{c}{at["cost_of_sales"]}");
        Line("other_income");
        row++;
        sheet.Cell(row, 1).Value = "Operating expenses";
        sheet.Cell(row, 1).Style.Font.SetItalic();
        foreach (var code in OperatingExpenses)
        {
            Line(code);
        }
        Subtotal(
            "operating_profit",
            "Operating profit",
            c => $"{c}{at["gross_profit"]}+{c}{at["other_income"]}-SUM({c}{at[OperatingExpenses[0]]}:{c}{at[OperatingExpenses[^1]]})"
        );
        Line("finance_costs");
        Subtotal("profit_before_tax", "Profit before tax", c => $"{c}{at["operating_profit"]}-{c}{at["finance_costs"]}");
        Line("income_tax");
        Subtotal("net_profit", "Net profit", c => $"{c}{at["profit_before_tax"]}-{c}{at["income_tax"]}");

        sheet.Range(HeaderRow + 1, 2, row, lastColumn).Style.NumberFormat.SetFormat(AmountFormat);
        sheet.Column(1).Width = 40;
        sheet.Columns(2, lastColumn).Width = 14;
        sheet.SheetView.FreezeRows(HeaderRow);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream, new SaveOptions { EvaluateFormulasBeforeSaving = true });
        return stream.ToArray();
    }
}
