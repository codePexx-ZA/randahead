// Submission row model: one row of the uploaded file as it was, with the categories it was mapped to
namespace BudgetApi.Models;

public class SubmissionRow
{
    public string RowId { get; set; } = Guid.NewGuid().ToString("N");
    public string SubmissionId { get; set; } = string.Empty;
    public short RowOrder { get; set; }
    public string RowKind { get; set; } = "line";
    public string SourceLabel { get; set; } = string.Empty;
    public string? SubtotalCode { get; set; }
    public decimal?[] Amounts { get; set; } = [];
    public string? Mapping { get; set; }
    public bool HandMapped { get; set; }
}
