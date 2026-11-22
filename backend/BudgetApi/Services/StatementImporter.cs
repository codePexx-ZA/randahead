using System.Globalization;
using System.IO.Compression;
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
    string[] Notes,
    StatementTable? Statement,
    SheetChoice[]? Sheets = null,
    string[]? Info = null,
    string? Title = null
);

public class StatementImportException(string message) : Exception(message);

public static partial class StatementImporter
{
    public const int MaxYears = 5;
    public const int MaxRows = 500;
    private const int MaxGridRows = 2000;
    private const int MaxGridColumns = 60;
    private const long MaxUnzippedBytes = 20 * 1024 * 1024;
    private const int HeaderSearchRows = 40;

    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    private static readonly (string Category, string[] Words)[] Keywords =
    [
        ("revenue", ["sales", "revenue", "turnover", "income", "income from services", "service income", "fees earned", "takings"]),
        ("other_income", ["other income", "interest received", "interest income", "sundry income", "rental income", "dividends received", "dividend income", "commission received", "commission income", "discount received", "bad debts recovered"]),
        ("cost_of_sales", ["cost of sales", "cost of goods sold", "cost of goods", "cogs", "purchases", "direct costs"]),
        ("employee_costs", ["salaries", "salary", "wages", "staff costs", "employee", "employee costs", "payroll", "uif", "sdl", "paye", "bonuses", "pension", "staff training", "staff welfare", "staff wellfare", "directors fees", "remuneration", "human resources"]),
        ("occupancy", ["rent", "rental", "rates", "lease", "occupancy", "property"]),
        ("electricity", ["electricity", "eskom", "prepaid electricity"]),
        ("other_utilities", ["water", "telephone", "internet", "telecoms", "telecommunications", "cellphone", "utilities", "other utilities", "wifi", "fax"]),
        ("transport_fuel", ["fuel", "petrol", "diesel", "delivery", "motor vehicle", "vehicle", "transport", "courier", "travel"]),
        ("marketing", ["advertising", "marketing", "promotions", "promotion", "social media"]),
        ("other_operating", ["other operating expenses", "admin", "administration", "bank charges", "repairs", "maintenance", "insurance", "stationery", "printing", "postage", "accounting", "accounting fees", "audit fees", "legal fees", "professional fees", "consulting fees", "secretarial fees", "computer expenses", "cleaning", "security", "consumables", "licence", "license", "licences", "subscriptions", "sundry expenses", "general expenses", "bad debts", "donations", "entertainment", "levies", "leasing charges", "discount allowed"]),
        ("depreciation", ["depreciation", "amortisation", "amortization"]),
        ("finance_costs", ["interest paid", "interest expense", "finance costs", "finance charges", "interest on overdraft", "interest on loan"]),
        ("income_tax", ["income tax", "tax", "taxation", "sars", "company tax"]),
    ];

    private static readonly (string Subtotal, Regex Pattern)[] SubtotalPatterns =
    [
        (IncomeStatement.ProfitBeforeTax, new Regex(@"\b(profit|loss)\b.*\bbefore (income )?tax")),
        (IncomeStatement.GrossProfit, new Regex(@"^gross (profit|loss|margin)")),
        (IncomeStatement.OperatingProfit, new Regex(@"^(operating (profit|loss|income)|(profit|loss) from operations|ebit)\b")),
        (IncomeStatement.NetProfit, new Regex(@"^(net (profit|loss|income)|(profit|loss)( loss)? (for the (year|period)|after tax))")),
    ];

    private readonly record struct Cell(string Text, double? Number, DateOnly? Date = null);

    private record Sheet(string Name, bool Hidden, List<Cell[]> Grid);

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

    public static StatementPreview Read(
        Stream stream,
        string fileName,
        IReadOnlyDictionary<string, MappingPart[]> remembered,
        int yearEndMonth = 2
    )
    {
        List<Sheet> sheets = Path.GetExtension(fileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? [new Sheet(fileName, false, ParseCsv(ReadText(stream)))]
            : ReadWorkbook(stream);

        // Visible sheets first: a hidden working sheet should never win over the statement the business sees.
        var ordered = sheets.Where(s => !s.Hidden).Concat(sheets.Where(s => s.Hidden)).ToList();
        var first = ordered.Select(s => (Sheet: s, Header: FindHeader(s.Grid))).FirstOrDefault(x => x.Header is not null);
        if (first.Header is null || first.Header.Monthly)
        {
            if (ReadMonthly(sheets, fileName, remembered, yearEndMonth) is { } monthly)
            {
                return monthly;
            }
        }
        return BuildPreview(ordered.Select(s => s.Grid).ToList(), fileName, remembered);
    }

    private static List<Sheet> ReadWorkbook(Stream stream)
    {
        const string unreadable = "We couldn't open this Excel file. Save it again as .xlsx (or .csv) and try again.";
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Sum(e => e.Length) > MaxUnzippedBytes)
            {
                throw new StatementImportException("This workbook is too large once opened. Keep only the income statement in it and try again.");
            }
        }
        catch (InvalidDataException)
        {
            throw new StatementImportException(unreadable);
        }
        stream.Position = 0;

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception)
        {
            throw new StatementImportException(unreadable);
        }
        using (workbook)
        {
            return workbook
                .Worksheets.Select(s => new Sheet(s.Name.Trim(), s.Visibility != XLWorksheetVisibility.Visible, ReadSheet(s)))
                .ToList();
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
            XLDataType.Number when IsDateFormat(cell) && ToDate(value.GetNumber()) is { } date => new Cell(FormattedText(cell), null, date),
            XLDataType.Number => new Cell(NumberText(value.GetNumber()), value.GetNumber()),
            XLDataType.Text => new Cell(value.GetText(), null),
            XLDataType.DateTime => new Cell(FormattedText(cell), null, DateOnly.FromDateTime(value.GetDateTime())),
            _ => default,
        };
    }

    [GeneratedRegex("\"[^\"]*\"|\\[[^\\]]*\\]")]
    private static partial Regex FormatLiterals();

    // Month headers are often formulas (=C3+31) whose cached value is a plain number with a date format.
    private static bool IsDateFormat(IXLCell cell)
    {
        try
        {
            var format = cell.Style.NumberFormat;
            if (format.NumberFormatId is >= 14 and <= 22 or >= 45 and <= 47)
            {
                return true;
            }
            var code = FormatLiterals().Replace(format.Format ?? "", "").ToLowerInvariant();
            return code.Contains('d') || code.Contains('y') || code.Contains("mmm");
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static DateOnly? ToDate(double serial)
    {
        if (serial is < 32874 or > 73051) // 1990-01-01 .. 2100-01-01
        {
            return null;
        }
        return DateOnly.FromDateTime(DateTime.FromOADate(serial));
    }

    private static string FormattedText(IXLCell cell)
    {
        try
        {
            return cell.GetFormattedString();
        }
        catch (Exception)
        {
            return "";
        }
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
                if (row.Count < MaxGridColumns)
                {
                    row.Add(new Cell(cell.ToString(), null));
                }
                cell.Clear();
            }
            else if (ch is '\n' or '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                if (row.Count < MaxGridColumns)
                {
                    row.Add(new Cell(cell.ToString(), null));
                }
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
            if (row.Count < MaxGridColumns)
            {
                row.Add(new Cell(cell.ToString(), null));
            }
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

    private static (int Year, int? Month)? Period(Cell cell) =>
        cell.Date is { } date ? (date.Year, date.Month) : ParsePeriodHeader(cell.Text);

    private static Header? FindHeader(List<Cell[]> grid)
    {
        var limit = Math.Min(grid.Count, HeaderSearchRows);
        for (var r = 0; r < limit; r++)
        {
            var years = new List<(int Col, int Year)>();
            var months = new List<(int Col, int Year, int Month)>();
            for (var col = 0; col < grid[r].Length; col++)
            {
                if (Period(grid[r][col]) is not { } period)
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
                if (Period(grid[r][col]) is { } period)
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
        // Accounting packages name accounts "Group - Detail" ("Rent Paid - Rates & Taxes"): a clear group decides.
        var dash = key.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0 && MatchCategory(key[..dash]) is [var group])
        {
            return [group];
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

    private static IEnumerable<MappingPart>? PartsOf(PreviewMapping? mapping) =>
        mapping?.Type switch
        {
            null => null,
            "single" => [new MappingPart(mapping.Category!, 100)],
            "split" => mapping.Parts!,
            _ => [],
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
        return LineRow(id, label, values, remembered);
    }

    [GeneratedRegex(@"\bdividends?\b")]
    private static partial Regex Dividend();

    // Dividends declared or paid are a distribution of profit, not a cost, so they stay out of the statement.
    private static bool IsDividendPaid(string clean) =>
        Dividend().IsMatch(clean) && !clean.Contains("received") && !clean.Contains("income");

    /// <param name="hint">Category of the section the line sits in (from the sheet's own heading), used when the label alone is unknown or ambiguous.</param>
    private static PreviewRow LineRow(
        int id,
        string label,
        decimal?[] values,
        IReadOnlyDictionary<string, MappingPart[]> remembered,
        string? hint = null
    )
    {
        var key = LabelKey(label);
        PreviewRow Row(string status, PreviewMapping? mapping = null, string[]? candidates = null) =>
            new(id, label, key, values, "line", null, status, mapping, candidates);
        PreviewMapping Mapping(string category) =>
            category == "ignore" ? new PreviewMapping("ignore") : new PreviewMapping("single", Category: category);

        if (remembered.TryGetValue(key, out var parts))
        {
            return Row("remembered", MappingFromParts(parts));
        }
        if (IsDividendPaid(CleanText(key)))
        {
            return Row("auto", Mapping("ignore"));
        }
        return MatchCategory(key) switch
        {
            null when hint is not null => Row("auto", Mapping(hint)),
            null => Row("unknown"),
            [var single] => Row("auto", Mapping(single)),
            var several when hint is not null && several.Contains(hint) => Row("auto", Mapping(hint)),
            var several => Row("cross", candidates: several),
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
        var years = cols.Select(c => $"FY{c.Year}").ToArray();
        var statement = rows.Any(r => r.Kind == "line" && r.Mapping is null)
            ? null
            : IncomeStatement.FromRows(years, [.. rows.Select(r => (r.Kind, r.Subtotal, r.Values, PartsOf(r.Mapping)))], scale);
        return new StatementPreview(fileName, "annual", years, scale, hint, [.. rows], [.. notes], statement);
    }
}
