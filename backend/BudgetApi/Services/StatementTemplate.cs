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
        // A term over several lines must list them in layout order, as one SUM over their rows
        string Term(string column, string[] codes) =>
            codes.Length == 1 ? $"{column}{at[codes[0]]}" : $"SUM({column}{at[codes[0]]}:{column}{at[codes[^1]]})";

        foreach (var item in IncomeStatement.Layout)
        {
            row++;
            if (item.Heading is { } heading)
            {
                sheet.Cell(row, 1).Value = heading;
                sheet.Cell(row, 1).Style.Font.SetItalic();
            }
            else if (item.Category is { } code)
            {
                sheet.Cell(row, 1).Value = categoryNames[code];
                sheet.Range(row, 2, row, lastColumn).Style.Fill.SetBackgroundColor(Cream);
                at[code] = row;
            }
            else
            {
                var subtotal = IncomeStatement.Subtotals.First(s => s.Code == item.Subtotal);
                sheet.Cell(row, 1).Value = subtotal.Name;
                for (var col = 2; col <= lastColumn; col++)
                {
                    var c = XLHelper.GetColumnLetterFromNumber(col);
                    var formula = string.Concat(subtotal.Terms.Select((t, i) => (t.Sign < 0 ? "-" : i == 0 ? "" : "+") + Term(c, t.Codes)));
                    sheet.Cell(row, col).FormulaA1 = formula;
                }
                sheet.Range(row, 1, row, lastColumn).Style.Font.SetBold().Fill.SetBackgroundColor(Mint);
                at[subtotal.Code] = row;
            }
        }

        sheet.Range(HeaderRow + 1, 2, row, lastColumn).Style.NumberFormat.SetFormat(AmountFormat);
        sheet.Column(1).Width = 40;
        sheet.Columns(2, lastColumn).Width = 14;
        sheet.SheetView.FreezeRows(HeaderRow);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream, new SaveOptions { EvaluateFormulasBeforeSaving = true });
        return stream.ToArray();
    }
}
