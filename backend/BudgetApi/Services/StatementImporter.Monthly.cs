using System.Globalization;
using System.Text.RegularExpressions;

namespace BudgetApi.Services;

/// <summary>One sheet of the workbook and what the importer did with it.</summary>
public record SheetChoice(string Name, bool Used, string Detail);

/// <summary>
/// Reads management-accounts workbooks: one sheet per year (in any tab order), twelve month columns,
/// account codes beside the line names, and other content (prior-year summaries, ratios, balance sheet,
/// loan schedules) around the income statement. The months are added up into the business's financial
/// years, so the result is the same annual preview a one-sheet upload produces.
/// </summary>
public static partial class StatementImporter
{
    private const int MonthHeaderSearchRows = 120;
    private const int MinStatementLines = 3;
    private const int MinCodedLines = 3;
    private const int FirstBalanceSheetAccount = 5000;

    private record MonthColumn(int Col, int Year, int Month);

    private record Block(int HeaderRow, int EndRow, List<MonthColumn> Months);

    private record ParsedRow(string? Code, string Label, Dictionary<(int Year, int Month), decimal?> Values)
    {
        public bool HasAmounts => Values.Values.Any(v => v is { } n && n != 0);
    }

    private record SourceRow(string Key, string Label, string Kind, string? Subtotal, string? Hint, Dictionary<(int Year, int Month), decimal?> Values);

    private record SheetStatement(Sheet Sheet, Block Block, List<SourceRow> Rows, HashSet<(int Year, int Month)> Filled, int Skipped)
    {
        public int Lines => Rows.Count(r => r.Kind == "line");
    }

    [GeneratedRegex(@"^\d{3,8}(?:[/.\-]\d{1,4})?$")]
    private static partial Regex AccountCode();

    [GeneratedRegex(@"%|\bpercent|\bincrease\b|\bdecrease\b|\bratio\b|\bper (square|sq|m2)")]
    private static partial Regex RatioLabel();

    [GeneratedRegex(@"\b(inc|incl|including|inclusive)\b")]
    private static partial Regex VatInclusive();

    [GeneratedRegex(@"\b(sales|revenue|turnover|takings)\b")]
    private static partial Regex SalesWord();

    [GeneratedRegex(@"\b(cost of sales|expenses?|cost of goods)\b")]
    private static partial Regex CostWord();

    private static StatementPreview? ReadMonthly(
        List<Sheet> sheets,
        string fileName,
        IReadOnlyDictionary<string, MappingPart[]> remembered,
        int yearEndMonth
    )
    {
        var choices = new Dictionary<Sheet, SheetChoice>(ReferenceEqualityComparer.Instance);
        var visible = new List<SheetStatement>();
        var hidden = new List<SheetStatement>();
        foreach (var sheet in sheets)
        {
            var blocks = MonthBlocks(sheet.Grid);
            if (blocks.Count == 0)
            {
                choices[sheet] = new SheetChoice(sheet.Name, false, "no month columns");
                continue;
            }
            var best = blocks.Select(b => ReadBlock(sheet, b)).OfType<SheetStatement>().MaxBy(s => s.Lines);
            if (best is null || best.Lines < MinStatementLines || !LooksLikeIncomeStatement(best))
            {
                choices[sheet] = new SheetChoice(sheet.Name, false, "month columns, but no income statement (sales, costs and profit lines)");
                continue;
            }
            (sheet.Hidden ? hidden : visible).Add(best);
        }

        var statements = visible.Count > 0 ? visible : hidden;
        foreach (var st in visible.Count > 0 ? hidden : new List<SheetStatement>())
        {
            choices[st.Sheet] = new SheetChoice(st.Sheet.Name, false, "hidden sheet (a visible sheet has the statement)");
        }
        if (statements.Count == 0)
        {
            return null;
        }

        // The pack's Data tab is a working summary. The year sheets hold the income statement.
        var working = statements.Where(s => s.Sheet.Name.Trim().Equals("Data", StringComparison.OrdinalIgnoreCase)).ToList();
        if (working.Count > 0 && statements.Count > working.Count)
        {
            foreach (var st in working)
            {
                choices[st.Sheet] = new SheetChoice(st.Sheet.Name, false, "working sheet; the year sheets have the income statement");
            }
            statements = statements.Except(working).ToList();
        }

        // A one-page summary must not take months from the detailed year sheets.
        var richest = statements.Max(s => s.Lines);
        if (richest >= 12)
        {
            var detailed = statements.Where(s => s.Lines * 2 >= richest).ToList();
            foreach (var st in statements.Where(s => !detailed.Contains(s)))
            {
                choices[st.Sheet] = new SheetChoice(st.Sheet.Name, false, "summary sheet; a more detailed income statement is in this workbook");
            }
            statements = detailed;
        }

        // Newest sheet first: when two sheets carry the same month, the newer one wins.
        statements = statements.OrderByDescending(s => s.Filled.Count > 0 ? s.Filled.Max() : (0, 0)).ToList();
        var owner = new Dictionary<(int Year, int Month), SheetStatement>();
        foreach (var st in statements)
        {
            foreach (var month in st.Filled)
            {
                owner.TryAdd(month, st);
            }
        }

        int FiscalYear((int Year, int Month) m) => yearEndMonth == 12 || m.Month <= yearEndMonth ? m.Year : m.Year + 1;
        var byYear = owner.Keys.GroupBy(FiscalYear).ToDictionary(g => g.Key, g => g.OrderBy(m => m).ToList());
        var complete = byYear.Where(y => y.Value.Count == 12).Select(y => y.Key).Order().ToList();
        var notes = new List<string>();
        var info = new List<string>();

        foreach (var (year, months) in byYear.Where(y => y.Value.Count < 12).OrderBy(y => y.Key))
        {
            notes.Add(
                $"FY{year} is left out: only {MonthSpan(months)} have figures ({months.Count} of 12 months). Forecasts use full years only."
            );
        }
        if (complete.Count == 0)
        {
            var all = owner.Keys.Order().ToList();
            throw new StatementImportException(
                all.Count == 0
                    ? "The sheets have month columns but no amounts under them. Fill in the figures and upload again."
                    : $"No complete financial year found. The sheets have figures for {MonthSpan(all)} only, and RandAhead needs at least one full year ({MonthName(yearEndMonth % 12 + 1)} to {MonthName(yearEndMonth)})."
            );
        }
        if (complete.Count > MaxYears)
        {
            notes.Add($"The file has {complete.Count} full years; only the latest {MaxYears} (FY{complete[^MaxYears]}–FY{complete[^1]}) are used.");
            complete = complete.TakeLast(MaxYears).ToList();
        }
        var yearIndex = complete.Select((y, i) => (y, i)).ToDictionary(x => x.y, x => x.i);

        // Combine rows across sheets: same account code (or same label when there are no codes) = same line.
        var order = new List<string>();
        var meta = new Dictionary<string, SourceRow>();
        var totals = new Dictionary<string, decimal?[]>();
        foreach (var st in statements)
        {
            var anchor = -1;
            foreach (var row in st.Rows)
            {
                var at = order.IndexOf(row.Key);
                if (at == -1)
                {
                    order.Insert(anchor + 1, row.Key);
                    at = anchor + 1;
                    meta[row.Key] = row;
                    totals[row.Key] = new decimal?[complete.Count];
                }
                anchor = at;
                var sums = totals[row.Key];
                foreach (var (month, value) in row.Values)
                {
                    if (value is { } v && ReferenceEquals(owner.GetValueOrDefault(month), st) && yearIndex.TryGetValue(FiscalYear(month), out var k))
                    {
                        sums[k] = (sums[k] ?? 0) + v;
                    }
                }
            }
        }

        var rows = new List<PreviewRow>();
        var empty = 0;
        foreach (var key in order)
        {
            var source = meta[key];
            var values = totals[key];
            if (source.Kind == "subtotal")
            {
                rows.Add(new PreviewRow(0, source.Label, LabelKey(source.Label), values, "subtotal", source.Subtotal, null, null, null));
            }
            else if (values.All(v => v is null or 0))
            {
                empty++;
            }
            else
            {
                rows.Add(LineRow(0, source.Label, values, remembered, source.Hint));
            }
        }

        // A net profit after dividends can't be checked against profit after tax, so don't flag it as a mismatch.
        if (rows.Any(r => r.Kind == "line" && r.Mapping?.Type == "ignore" && IsDividendPaid(CleanText(r.Key)) && r.Values.Any(v => v is not (null or 0))))
        {
            if (rows.RemoveAll(r => r.Kind == "subtotal" && r.Subtotal == IncomeStatement.NetProfit) > 0)
            {
                info.Add("Net profit in the sheets is after dividends, so only the profit before tax is cross-checked.");
            }
        }
        rows = rows.Select((r, i) => r with { Id = i }).ToList();
        if (rows.Count > MaxRows)
        {
            throw new StatementImportException($"The statement has more than {MaxRows} rows. Keep only the income statement on the sheet.");
        }
        if (!rows.Any(r => r.Kind == "line"))
        {
            throw new StatementImportException("No amounts found under the month columns. Fill in the figures and upload again.");
        }

        foreach (var st in statements)
        {
            var mine = owner.Where(o => ReferenceEquals(o.Value, st)).Select(o => o.Key).ToList();
            var years = mine.Select(FiscalYear).Distinct().Where(yearIndex.ContainsKey).Order().Select(y => $"FY{y}").ToList();
            choices[st.Sheet] = years.Count > 0
                ? new SheetChoice(st.Sheet.Name, true, $"{string.Join(", ", years)} ({MonthSpan(mine.Where(m => yearIndex.ContainsKey(FiscalYear(m))).ToList())})")
                : mine.Count > 0
                    ? new SheetChoice(st.Sheet.Name, false, $"only part of a year ({MonthSpan(mine)})")
                    : new SheetChoice(st.Sheet.Name, false, "same months as a newer sheet");
        }

        var skipped = statements.Sum(s => s.Skipped);
        info.Insert(0, $"Monthly figures were added up into financial years ending in {MonthName(yearEndMonth)}.");
        if (skipped > 0)
        {
            info.Add($"{skipped} calculated row{(skipped == 1 ? " was" : "s were")} skipped (VAT-inclusive sales, totals and percentages); the sheets' own profit rows are used to cross-check.");
        }
        if (empty > 0)
        {
            info.Add($"{empty} account{(empty == 1 ? "" : "s")} with no amounts in these years {(empty == 1 ? "was" : "were")} left out.");
        }
        var lastMonths = statements.Select(s => s.Block.Months.Max(m => (m.Year, m.Month)).Month).ToList();
        var sheetYearEnd = lastMonths.GroupBy(m => m).MaxBy(g => g.Count())!.Key;
        if (sheetYearEnd != yearEndMonth)
        {
            notes.Add(
                $"The sheets run to {MonthName(sheetYearEnd)}, but your business year ends in {MonthName(yearEndMonth)}; the months were regrouped into your financial years. Change the year-end on the Business page if that's wrong."
            );
        }

        var first = statements[0];
        var (scale, hint) = DetectScale(first.Sheet.Grid, first.Block.HeaderRow);
        var yearLabels = complete.Select(y => $"FY{y}").ToArray();
        var statement = rows.Any(r => r.Kind == "line" && r.Mapping is null)
            ? null
            : IncomeStatement.FromRows(yearLabels, [.. rows.Select(r => (r.Kind, r.Subtotal, r.Values, PartsOf(r.Mapping)))], scale);
        return new StatementPreview(
            fileName,
            "annual",
            yearLabels,
            scale,
            hint,
            [.. rows],
            [.. notes],
            statement,
            [.. sheets.Where(choices.ContainsKey).Select(s => choices[s])],
            [.. info],
            Title(first.Sheet.Grid)
        );
    }

    /// <summary>Rows with month headings across them (at least three different months), each starting a block that runs to the next one.</summary>
    private static List<Block> MonthBlocks(List<Cell[]> grid)
    {
        var headers = new List<(int Row, List<MonthColumn> Months)>();
        for (var r = 0; r < Math.Min(grid.Count, MonthHeaderSearchRows); r++)
        {
            var months = new List<MonthColumn>();
            for (var c = 0; c < grid[r].Length; c++)
            {
                if (Period(grid[r][c]) is { Month: { } m } p && !months.Any(x => x.Year == p.Year && x.Month == m))
                {
                    months.Add(new MonthColumn(c, p.Year, m));
                }
            }
            if (months.Select(m => m.Month).Distinct().Count() >= 3)
            {
                headers.Add((r, months));
            }
        }
        return headers.Select((h, i) => new Block(h.Row, i + 1 < headers.Count ? headers[i + 1].Row : grid.Count, h.Months)).ToList();
    }

    private static SheetStatement? ReadBlock(Sheet sheet, Block block)
    {
        var firstMonthCol = block.Months.Min(m => m.Col);
        var parsed = new List<ParsedRow>();
        for (var r = block.HeaderRow + 1; r < block.EndRow; r++)
        {
            var cells = sheet.Grid[r];
            string? code = null;
            var label = new List<string>();
            foreach (var cell in cells.Take(firstMonthCol))
            {
                var text = (cell.Text ?? "").Trim();
                if (code is null && AccountCode().IsMatch(text))
                {
                    code = text;
                }
                else if (IsText(cell))
                {
                    label.Add(text);
                }
            }
            var values = block.Months.ToDictionary(m => (m.Year, m.Month), m => Amount(cells, m.Col));
            parsed.Add(new ParsedRow(code, string.Join(" ", label), values));
        }

        var coded = parsed.Count(p => p.Code is not null && p.HasAmounts) >= MinCodedLines;
        var rows = new List<SourceRow>();
        var skipped = 0;
        string? hint = null;
        var seenIncomeAccount = false;
        for (var i = 0; i < parsed.Count; i++)
        {
            var p = parsed[i];
            if (p.Label.Length == 0)
            {
                continue;
            }
            var key = LabelKey(p.Label);
            var clean = CleanText(key);

            if (coded && p.Code is not null)
            {
                // Pastel/Sage-style charts put balance-sheet accounts from 5000 up; reaching them ends the income statement.
                var account = int.TryParse(new string(p.Code.Where(char.IsDigit).Take(4).ToArray()), out var n) ? n : 0;
                if (account >= FirstBalanceSheetAccount && seenIncomeAccount)
                {
                    break;
                }
                seenIncomeAccount |= account < FirstBalanceSheetAccount;
                rows.Add(new SourceRow($"code:{p.Code}", p.Label, "line", null, hint, p.Values));
                continue;
            }

            if (RatioLabel().IsMatch(p.Label.ToLowerInvariant()) || (!coded && VatInclusive().IsMatch(clean)))
            {
                skipped += p.HasAmounts ? 1 : 0;
                continue;
            }
            if (SubtotalPatterns.FirstOrDefault(s => s.Pattern.IsMatch(clean)).Subtotal is { } subtotal)
            {
                var subtotalKey = $"subtotal:{subtotal}";
                if (rows.Any(r => r.Key == subtotalKey))
                {
                    skipped += p.HasAmounts ? 1 : 0;
                }
                else
                {
                    rows.Add(new SourceRow(subtotalKey, p.Label, "subtotal", subtotal, null, p.Values));
                }
                hint = null;
                if (subtotal == IncomeStatement.NetProfit)
                {
                    break;
                }
                continue;
            }

            if (coded)
            {
                // The run of accounts next to this row; any uncoded row with a label or amounts (even an unlabelled total) ends it.
                static List<ParsedRow> Run(IEnumerable<ParsedRow> side) =>
                    side.Where(x => x.Label.Length > 0 || x.HasAmounts).TakeWhile(x => x.Code is not null).ToList();
                var below = Run(parsed.Skip(i + 1));
                var above = Run(parsed.Take(i).Reverse());
                var sectionHint = IsDividendPaid(clean) ? "ignore" : MatchCategory(key) is [var single] ? single : null;

                if (above.Count == 0 && below.Count > 0)
                {
                    // Uncoded row opening a run of accounts: a section heading, often carrying the section total.
                    hint = sectionHint;
                    if (p.HasAmounts && !below.Any(c => c.HasAmounts))
                    {
                        // The accounts under it are empty, so the heading's own figures are the line (e.g. "Cost of Sales").
                        rows.Add(new SourceRow($"label:{key}", p.Label, "line", null, hint, p.Values));
                    }
                    else
                    {
                        skipped += p.HasAmounts ? 1 : 0;
                    }
                    continue;
                }
                if (above.Count > 0 && p.HasAmounts)
                {
                    // Inside a run of accounts: a total of the accounts next to it, or an account without a code.
                    if ((below.Count > 0 && SameAmounts(p, below)) || SameAmounts(p, above))
                    {
                        skipped++;
                    }
                    else
                    {
                        rows.Add(new SourceRow($"label:{key}", p.Label, "line", null, hint, p.Values));
                    }
                    continue;
                }
                skipped += p.HasAmounts ? 1 : 0;
                continue;
            }

            if (TotalLabel().IsMatch(clean))
            {
                skipped += p.HasAmounts ? 1 : 0;
                continue;
            }
            if (p.Values.Values.All(v => v is null))
            {
                continue;
            }
            rows.Add(new SourceRow($"label:{key}", p.Label, "line", null, null, p.Values));
        }

        if (rows.Count == 0)
        {
            return null;
        }
        var filled = block
            .Months.Select(m => (m.Year, m.Month))
            .Where(m => rows.Any(r => r.Kind == "line" && r.Values.GetValueOrDefault(m) is { } v && v != 0))
            .ToHashSet();
        return new SheetStatement(sheet, block, rows, filled, skipped);
    }

    /// <summary>True when the row's amounts are the sum of the given accounts in every month (to within R1).</summary>
    private static bool SameAmounts(ParsedRow total, List<ParsedRow> accounts) =>
        total.Values.All(month => Math.Abs((month.Value ?? 0) - accounts.Sum(a => a.Values.GetValueOrDefault(month.Key) ?? 0)) <= 1);

    private static bool LooksLikeIncomeStatement(SheetStatement st)
    {
        var lines = st.Rows.Where(r => r.Kind == "line").ToList();
        var sales = lines.Any(r => r.Hint == IncomeStatement.Revenue || SalesWord().IsMatch(CleanText(r.Label)));
        var costsOrProfit = st.Rows.Any(r => r.Kind == "subtotal") || lines.Any(r => CostWord().IsMatch(CleanText(r.Label)));
        return sales && costsOrProfit;
    }

    private static string? Title(List<Cell[]> grid) =>
        grid.Take(3).SelectMany(r => r).Select(c => (c.Text ?? "").Trim()).FirstOrDefault(t => t.Length is > 0 and <= 80 && IsText(new Cell(t, null)));

    private static string MonthName(int month) => CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);

    private static string MonthSpan(List<(int Year, int Month)> months)
    {
        static string Short((int Year, int Month) m) =>
            $"{CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(m.Month)} {m.Year}";
        var sorted = months.Order().ToList();
        return sorted.Count == 1 ? Short(sorted[0]) : $"{Short(sorted[0])}–{Short(sorted[^1])}";
    }
}
