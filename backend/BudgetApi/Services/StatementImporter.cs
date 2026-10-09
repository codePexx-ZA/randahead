// Statement importer: reads an uploaded Excel or CSV income statement and suggests a category for every line
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace BudgetApi.Services;

public record MappingPart(string Category, decimal Percent);

public record PreviewMapping(string Type, string? Category = null, MappingPart[]? Parts = null);

public record PreviewRow(
    int Id,
    string Label,
    string Key,
    decimal?[] Values,
    string Kind,
    string? Subtotal,
    string? Status,
    PreviewMapping? Mapping,
    string[]? Candidates
);

public record StatementPreview(
    string FileName,
    string Frequency,
    string[] Years,
    int Scale,
    string? ScaleHint,
    PreviewRow[] Rows,
    string[] Notes
);

public class StatementImportException(string message) : Exception(message);

public static partial class StatementImporter
{
    public const int MaxYears = 5;
    public const int MaxRows = 500;
    private const int MaxGridRows = 2000;
    private const int MaxGridColumns = 60;
    private const int HeaderSearchRows = 40;

    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    private static readonly (string Category, string[] Words)[] Keywords =
    [
        ("revenue", ["sales", "revenue", "turnover", "income", "income from services", "service income", "fees earned", "takings"]),
        ("other_income", ["other income", "interest received", "interest income", "sundry income", "rental income", "dividends received", "dividend income", "commission received", "commission income"]),
        ("cost_of_sales", ["cost of sales", "cost of goods sold", "cost of goods", "cogs", "purchases", "direct costs"]),
        ("employee_costs", ["salaries", "salary", "wages", "staff costs", "employee", "employee costs", "payroll", "uif", "sdl", "paye", "bonuses", "pension"]),
        ("occupancy", ["rent", "rental", "rates", "lease", "occupancy", "property"]),
        ("electricity", ["electricity", "eskom", "prepaid electricity"]),
        ("other_utilities", ["water", "telephone", "internet", "telecoms", "telecommunications", "cellphone", "utilities", "other utilities", "wifi"]),
        ("transport_fuel", ["fuel", "petrol", "diesel", "delivery", "motor vehicle", "vehicle", "transport", "courier", "travel"]),
        ("marketing", ["advertising", "marketing", "promotions", "promotion", "social media"]),
        ("other_operating", ["other operating expenses", "admin", "administration", "bank charges", "repairs", "maintenance", "insurance", "stationery", "accounting fees", "audit fees", "legal fees", "professional fees", "cleaning", "security", "consumables", "licences", "subscriptions", "sundry expenses", "general expenses"]),
        ("depreciation", ["depreciation", "amortisation", "amortization"]),
        ("finance_costs", ["interest paid", "interest expense", "finance costs", "finance charges", "interest on overdraft", "interest on loan"]),
        ("income_tax", ["income tax", "tax", "taxation", "sars", "company tax"]),
    ];

    private static readonly (string Subtotal, Regex Pattern)[] SubtotalPatterns =
    [
        ("profit_before_tax", new Regex(@"\b(profit|loss)\b.*\bbefore (income )?tax")),
        ("gross_profit", new Regex(@"^gross (profit|loss|margin)")),
        ("operating_profit", new Regex(@"^(operating (profit|loss|income)|(profit|loss) from operations|ebit)\b")),
        ("net_profit", new Regex(@"^(net (profit|loss|income)|(profit|loss)( loss)? (for the (year|period)|after tax))")),
    ];

    private readonly record struct Cell(string Text, double? Number);

    private record Header(int Row, List<(int Col, int Year)> Years, bool Monthly);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\s+and\s+|[/,+;]")]
    private static partial Regex PartSeparator();

    [GeneratedRegex(@"^total\b|\btotals?$")]
    private static partial Regex TotalLabel();

    [GeneratedRegex(@"^[-–—]+$")]
    private static partial Regex Dashes();

    [GeneratedRegex(@"^R", RegexOptions.IgnoreCase)]
    private static partial Regex RandSign();

    [GeneratedRegex(@"^\d{1,3}(,\d{3})+(\.\d+)?$")]
    private static partial Regex ThousandsCommas();

    [GeneratedRegex(@"^\d+,\d+$")]
    private static partial Regex DecimalComma();

    [GeneratedRegex(@"^\d+(\.\d+)?$")]
    private static partial Regex PlainNumber();

    [GeneratedRegex(@"^FY\s*'?(\d{4}|\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex FyHeader();

    [GeneratedRegex(@"^(\d{4})\s*[/-]\s*(\d{2}|\d{4})$")]
    private static partial Regex YearRangeHeader();

    [GeneratedRegex(@"^(?:\d{1,2}\s+)?([a-z]{3,9})\.?[\s-]+'?(\d{4}|\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex MonthHeader();

    [GeneratedRegex(@"year\s+end(?:ed|ing)?.*?\b(\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex YearEndHeader();

    [GeneratedRegex(@"^(\d{4})\b(.*)$")]
    private static partial Regex PlainYearHeader();

    [GeneratedRegex(@"R\s*'?\s*000|'000|thousands", RegexOptions.IgnoreCase)]
    private static partial Regex ThousandsScale();

    [GeneratedRegex(@"\bR\s*'?\s*m(illion)?s?\b|millions", RegexOptions.IgnoreCase)]
    private static partial Regex MillionsScale();

    public static string LabelKey(string label) => Spaces().Replace(label.Trim().ToLowerInvariant(), " ");

    public static StatementPreview Read(Stream stream, string fileName, IReadOnlyDictionary<string, MappingPart[]> remembered)
    {
        var grids = Path.GetExtension(fileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? [ParseCsv(ReadText(stream))]
            : ReadWorkbook(stream);
        return BuildPreview(grids, fileName, remembered);
    }

    private static List<List<Cell[]>> ReadWorkbook(Stream stream)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception)
        {
            throw new StatementImportException("We couldn't open this Excel file. Save it again as .xlsx (or .csv) and try again.");
        }
        using (workbook)
        {
            return workbook.Worksheets.Select(ReadSheet).ToList();
        }
    }

    private static List<Cell[]> ReadSheet(IXLWorksheet sheet)
    {
        if (sheet.RangeUsed() is not { } used)
        {
            return [];
        }
        var lastRow = Math.Min(used.LastRow().RowNumber(), MaxGridRows);
        var lastColumn = Math.Min(used.LastColumn().ColumnNumber(), MaxGridColumns);
        var grid = new List<Cell[]>(lastRow);
        for (var r = 1; r <= lastRow; r++)
        {
            var row = new Cell[lastColumn];
            for (var c = 1; c <= lastColumn; c++)
            {
                row[c - 1] = ReadCell(sheet.Cell(r, c));
            }
            grid.Add(row);
        }
        return grid;
    }

    private static Cell ReadCell(IXLCell cell)
    {
        XLCellValue value;
        try
        {
            value = cell.HasFormula && !cell.CachedValue.IsBlank ? cell.CachedValue : cell.Value;
        }
        catch (Exception)
        {
            return default;
        }
        return value.Type switch
        {
            XLDataType.Number => new Cell(NumberText(value.GetNumber()), value.GetNumber()),
            XLDataType.Text => new Cell(value.GetText(), null),
            XLDataType.DateTime => new Cell(cell.GetFormattedString(), null),
            _ => default,
        };
    }

    private static string NumberText(double n) =>
        n == Math.Floor(n) && Math.Abs(n) < 1e15
            ? ((long)n).ToString(CultureInfo.InvariantCulture)
            : n.ToString(CultureInfo.InvariantCulture);

    private static string ReadText(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static List<Cell[]> ParseCsv(string text)
    {
        var sample = string.Join("\n", text.Split('\n').Take(15));
        var delimiter = new[] { ',', ';', '\t' }.Select(d => (d, count: sample.Count(ch => ch == d))).Aggregate((a, b) => b.count > a.count ? b : a).d;

        var rows = new List<Cell[]>();
        var row = new List<Cell>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(ch);
                }
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else if (ch == delimiter)
            {
                row.Add(new Cell(cell.ToString(), null));
                cell.Clear();
            }
            else if (ch is '\n' or '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                row.Add(new Cell(cell.ToString(), null));
                rows.Add([.. row]);
                row.Clear();
                cell.Clear();
            }
            else
            {
                cell.Append(ch);
            }
            if (rows.Count >= MaxGridRows)
            {
                return rows;
            }
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(new Cell(cell.ToString(), null));
            rows.Add([.. row]);
        }
        return rows;
    }

    private static decimal? ParseAmount(Cell cell, out bool isText)
    {
        isText = false;
        if (cell.Number is { } number)
        {
            return Math.Abs(number) < 1e15 ? (decimal)number : Fail(out isText);
        }
        var s = (cell.Text ?? "").Trim();
        if (s.Length == 0)
        {
            return null;
        }
        if (Dashes().IsMatch(s))
        {
            return 0;
        }

        var negative = false;
        if (s.Length >= 2 && s[0] == '(' && s[^1] == ')')
        {
            negative = true;
            s = s[1..^1];
        }
        s = Spaces().Replace(RandSign().Replace(s, ""), "");
        if (s.StartsWith('-'))
        {
            negative = !negative;
            s = s[1..];
        }
        if (ThousandsCommas().IsMatch(s))
        {
            s = s.Replace(",", "");
        }
        else if (DecimalComma().IsMatch(s))
        {
            s = s.Replace(',', '.');
        }
        if (!PlainNumber().IsMatch(s) || !decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n))
        {
            return Fail(out isText);
        }
        return negative ? -n : n;
    }

    private static decimal? Fail(out bool isText)
    {
        isText = true;
        return null;
    }

    private static decimal? Amount(Cell[] row, int col) => col < row.Length ? ParseAmount(row[col], out _) : null;

    private static bool IsText(Cell cell) => ParseAmount(cell, out var isText) is null && isText;

    private static int FullYear(string text) => text.Length == 2 ? 2000 + int.Parse(text) : int.Parse(text);

    private static (int Year, int? Month)? ParsePeriodHeader(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.Length is 0 or > 40)
        {
            return null;
        }
        static (int, int?)? Valid(int year) => year is >= 1990 and <= 2100 ? (year, null) : null;

        if (FyHeader().Match(s) is { Success: true } fy)
        {
            return Valid(FullYear(fy.Groups[1].Value));
        }
        if (YearRangeHeader().Match(s) is { Success: true } range)
        {
            var start = int.Parse(range.Groups[1].Value);
            var end = range.Groups[2].Value;
            return Valid(end.Length == 2 ? start / 100 * 100 + int.Parse(end) : int.Parse(end));
        }
        if (MonthHeader().Match(s) is { Success: true } month)
        {
            var index = Array.IndexOf(Months, month.Groups[1].Value[..3].ToLowerInvariant());
            if (index != -1)
            {
                return (FullYear(month.Groups[2].Value), index + 1);
            }
        }
        if (YearEndHeader().Match(s) is { Success: true } yearEnd)
        {
            return Valid(int.Parse(yearEnd.Groups[1].Value));
        }
        if (PlainYearHeader().Match(s) is { Success: true } plain && !plain.Groups[2].Value.Any(char.IsDigit) && plain.Groups[2].Value.Length <= 12)
        {
            return Valid(int.Parse(plain.Groups[1].Value));
        }
        return null;
    }

    private static Header? FindHeader(List<Cell[]> grid)
    {
        var limit = Math.Min(grid.Count, HeaderSearchRows);
        for (var r = 0; r < limit; r++)
        {
            var years = new List<(int Col, int Year)>();
            var months = new List<(int Col, int Year, int Month)>();
            for (var col = 0; col < grid[r].Length; col++)
            {
                if (ParsePeriodHeader(grid[r][col].Text) is not { } period)
                {
                    continue;
                }
                if (period.Month is { } m)
                {
                    months.Add((col, period.Year, m));
                }
                else
                {
                    years.Add((col, period.Year));
                }
            }
            if (months.Count >= 2)
            {
                return months.Select(p => p.Month).Distinct().Count() > 1
                    ? new Header(r, [], Monthly: true)
                    : new Header(r, months.Select(p => (p.Col, p.Year)).ToList(), Monthly: false);
            }
            if (years.Count >= 2)
            {
                return new Header(r, years, Monthly: false);
            }
        }

        for (var r = 0; r < limit; r++)
        {
            var hits = new List<(int Col, int Year)>();
            for (var col = 1; col < grid[r].Length; col++)
            {
                if (ParsePeriodHeader(grid[r][col].Text) is { } period)
                {
                    hits.Add((col, period.Year));
                }
            }
            if (hits.Count != 1)
            {
                continue;
            }
            var hitCol = hits[0].Col;
            if (grid.Skip(r + 1).Take(HeaderSearchRows).Any(row => Amount(row, hitCol) is not null))
            {
                return new Header(r, hits, Monthly: false);
            }
        }
        return null;
    }

    private static (int Scale, string? Hint) DetectScale(List<Cell[]> grid, int headerRow)
    {
        var text = string.Join(" | ", grid.Take(headerRow + 3).SelectMany(row => row.Select(c => c.Text ?? "")));
        if (ThousandsScale().Match(text) is { Success: true } thousands)
        {
            return (1000, thousands.Value);
        }
        if (MillionsScale().Match(text) is { Success: true } millions)
        {
            return (1000000, millions.Value);
        }
        return (1, null);
    }

    private static string CleanText(string text) =>
        NonWord().Replace(text.ToLowerInvariant().Replace("&", " and "), " ").Trim();

    private static string? BestKeyword(string text)
    {
        var padded = $" {text} ";
        string? best = null;
        var bestLength = 0;
        foreach (var (category, words) in Keywords)
        {
            foreach (var word in words)
            {
                if (word.Length > bestLength && padded.Contains($" {word} "))
                {
                    best = category;
                    bestLength = word.Length;
                }
            }
        }
        return best;
    }

    private static string[]? MatchCategory(string key)
    {
        var whole = CleanText(key);
        if (Keywords.FirstOrDefault(k => k.Words.Contains(whole)).Category is { } exact)
        {
            return [exact];
        }
        var parts = PartSeparator().Split(key.Replace("&", " and ")).Select(CleanText).Where(p => p.Length > 0).ToList();
        var found = parts.Select(BestKeyword).ToList();
        if (found.Count == 0 || found.Any(c => c is null))
        {
            return null;
        }
        return found.Select(c => c!).Distinct().ToArray();
    }

    private static PreviewMapping MappingFromParts(MappingPart[] parts) =>
        parts.Length switch
        {
            0 => new PreviewMapping("ignore"),
            1 => new PreviewMapping("single", Category: parts[0].Category),
            _ => new PreviewMapping("split", Parts: parts),
        };

    private static PreviewRow ClassifyRow(int id, string label, decimal?[] values, IReadOnlyDictionary<string, MappingPart[]> remembered)
    {
        var key = LabelKey(label);
        PreviewRow Row(string kind, string? subtotal = null, string? status = null, PreviewMapping? mapping = null, string[]? candidates = null) =>
            new(id, label, key, values, kind, subtotal, status, mapping, candidates);

        if (values.All(v => v is null))
        {
            return Row("heading");
        }
        var clean = CleanText(key);
        if (SubtotalPatterns.FirstOrDefault(p => p.Pattern.IsMatch(clean)).Subtotal is { } subtotal)
        {
            return Row("subtotal", subtotal);
        }
        if (TotalLabel().IsMatch(clean))
        {
            return Row("total");
        }
        if (remembered.TryGetValue(key, out var parts))
        {
            return Row("line", status: "remembered", mapping: MappingFromParts(parts));
        }
        return MatchCategory(key) switch
        {
            null => Row("line", status: "unknown"),
            [var single] => Row("line", status: "auto", mapping: new PreviewMapping("single", Category: single)),
            var several => Row("line", status: "cross", candidates: several),
        };
    }

    private static StatementPreview BuildPreview(List<List<Cell[]>> grids, string fileName, IReadOnlyDictionary<string, MappingPart[]> remembered)
    {
        List<Cell[]>? grid = null;
        Header? header = null;
        foreach (var g in grids)
        {
            header = FindHeader(g);
            if (header is not null)
            {
                grid = g;
                break;
            }
        }

        if (header is null || grid is null)
        {
            throw new StatementImportException("We couldn't find the year columns. Put the line names in the first column and one financial year per column (e.g. FY2025, FY2026), or a single year (e.g. FY2026).");
        }
        if (header.Monthly)
        {
            throw new StatementImportException("This looks like a monthly file. Monthly uploads are coming soon; for now please upload annual figures (one column per financial year).");
        }

        var cols = header.Years.OrderBy(c => c.Year).ToList();
        var repeated = cols.Where((c, i) => i > 0 && c.Year == cols[i - 1].Year).Select(c => $"FY{c.Year}").Distinct().ToList();
        if (repeated.Count > 0)
        {
            throw new StatementImportException($"The file has more than one column for {string.Join(", ", repeated)}. Keep one column per financial year.");
        }

        var notes = new List<string>();
        if (cols.Count > MaxYears)
        {
            var kept = cols.TakeLast(MaxYears).ToList();
            notes.Add($"The file has {cols.Count} years; only the latest {MaxYears} (FY{kept[0].Year}–FY{kept[^1].Year}) are used.");
            cols = kept;
        }

        var firstYearCol = header.Years.Min(c => c.Col);
        var rows = new List<PreviewRow>();
        foreach (var cells in grid.Skip(header.Row + 1))
        {
            var label = string.Join(" ", cells.Take(firstYearCol).Where(IsText).Select(c => c.Text.Trim()));
            if (label.Length == 0)
            {
                continue;
            }
            if (rows.Count == MaxRows)
            {
                throw new StatementImportException($"The statement has more than {MaxRows} rows. Keep only the income statement on the sheet.");
            }
            var values = cols.Select(c => Amount(cells, c.Col)).ToArray();
            rows.Add(ClassifyRow(rows.Count, label, values, remembered));
        }

        while (rows.Count > 0 && rows[^1].Kind == "heading")
        {
            rows.RemoveAt(rows.Count - 1);
        }
        if (!rows.Any(r => r.Kind == "line"))
        {
            throw new StatementImportException("No amounts found under the year columns. Fill in the figures and upload again.");
        }

        var (scale, hint) = DetectScale(grid, header.Row);
        return new StatementPreview(fileName, "annual", cols.Select(c => $"FY{c.Year}").ToArray(), scale, hint, [.. rows], [.. notes]);
    }
}
