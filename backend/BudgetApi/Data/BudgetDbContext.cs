// Database context: maps classes to database tables (column names come from the snake_case naming convention in Program.cs)
using System.Text.RegularExpressions;
using BudgetApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Data;

public partial class BudgetDbContext(DbContextOptions<BudgetDbContext> options) : DbContext(options)
{
    public DbSet<DataSource> DataSources => Set<DataSource>();
    public DbSet<ExternalIndicator> ExternalIndicators => Set<ExternalIndicator>();
    public DbSet<RuleDefinition> RuleDefinitions => Set<RuleDefinition>();
    public DbSet<RuleAlert> RuleAlerts => Set<RuleAlert>();
    public DbSet<LineCategory> LineCategories => Set<LineCategory>();
    public DbSet<IndicatorType> IndicatorTypes => Set<IndicatorType>();
    public DbSet<IndicatorCategoryLink> IndicatorCategoryLinks => Set<IndicatorCategoryLink>();
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<BusinessUnit> BusinessUnits => Set<BusinessUnit>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<BudgetSubmission> BudgetSubmissions => Set<BudgetSubmission>();
    public DbSet<FinancialLineItem> FinancialLineItems => Set<FinancialLineItem>();
    public DbSet<SubmissionRow> SubmissionRows => Set<SubmissionRow>();
    public DbSet<ApprovalAction> ApprovalActions => Set<ApprovalAction>();
    public DbSet<ImportMapping> ImportMappings => Set<ImportMapping>();
    public DbSet<ForecastRun> ForecastRuns => Set<ForecastRun>();
    public DbSet<ForecastValue> ForecastValues => Set<ForecastValue>();
    public DbSet<BudgetSelection> BudgetSelections => Set<BudgetSelection>();
    public DbSet<KpiTarget> KpiTargets => Set<KpiTarget>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();

    [GeneratedRegex("(?<!^)(?=[A-Z])")]
    private static partial Regex WordStart();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Tables are named after the class, not the DbSet
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(WordStart().Replace(entity.ClrType.Name, "_").ToLowerInvariant());
        }
        modelBuilder.Entity<AuditEntry>().ToTable("audit_log");

        modelBuilder.Entity<AppUser>().HasKey(e => e.UserId);
        modelBuilder.Entity<ApprovalAction>().HasKey(e => e.ApprovalId);
        modelBuilder.Entity<AuditEntry>().HasKey(e => e.LogId);
        modelBuilder.Entity<BudgetSelection>().HasKey(e => e.BudgetId);
        modelBuilder.Entity<BudgetSubmission>().HasKey(e => e.SubmissionId);
        modelBuilder.Entity<BusinessUnit>().HasKey(e => e.UnitId);
        modelBuilder.Entity<DataSource>().HasKey(e => e.SourceId);
        modelBuilder.Entity<ExternalIndicator>().HasKey(e => e.IndicatorId);
        modelBuilder.Entity<FinancialLineItem>().HasKey(e => e.LineItemId);
        modelBuilder.Entity<ForecastRun>().HasKey(e => e.RunId);
        modelBuilder.Entity<ForecastValue>().HasKey(e => e.ValueId);
        modelBuilder.Entity<ImportMapping>().HasKey(e => e.MappingId);
        modelBuilder.Entity<IndicatorCategoryLink>().HasKey(e => new { e.IndicatorCode, e.CategoryCode });
        modelBuilder.Entity<IndicatorType>().HasKey(e => e.IndicatorCode);
        modelBuilder.Entity<KpiTarget>().HasKey(e => e.TargetId);
        modelBuilder.Entity<LineCategory>().HasKey(e => e.CategoryCode);
        modelBuilder.Entity<PasswordReset>().HasKey(e => e.ResetId);
        modelBuilder.Entity<RuleAlert>().HasKey(e => e.AlertId);
        modelBuilder.Entity<RuleDefinition>().HasKey(e => e.RuleId);
        modelBuilder.Entity<SubmissionRow>().HasKey(e => e.RowId);

        modelBuilder.Entity<SubmissionRow>().Property(e => e.Amounts).HasColumnType("numeric(20,6)[]");
        modelBuilder.Entity<SubmissionRow>().Property(e => e.Mapping).HasColumnType("jsonb");
        modelBuilder.Entity<ForecastRun>().Property(e => e.Parameters).HasColumnType("jsonb");
        modelBuilder.Entity<AuditEntry>().Property(e => e.Details).HasColumnType("jsonb");

        modelBuilder.Entity<BudgetSubmission>(entity =>
        {
            entity.HasOne(e => e.Submitter).WithMany().HasForeignKey(e => e.SubmittedBy).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.LineItems).WithOne().HasForeignKey(i => i.SubmissionId);
            entity.HasMany(e => e.Rows).WithOne().HasForeignKey(r => r.SubmissionId);
            entity.HasMany(e => e.Approvals).WithOne().HasForeignKey(a => a.SubmissionId);
        });
        modelBuilder.Entity<ForecastRun>().HasMany(e => e.Values).WithOne().HasForeignKey(v => v.RunId);
        modelBuilder.Entity<ApprovalAction>().HasOne(e => e.Actor).WithMany().HasForeignKey(e => e.ActedBy).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BudgetSelection>(entity =>
        {
            entity.HasOne(e => e.Chooser).WithMany().HasForeignKey(e => e.ChosenBy).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.MaRun).WithMany().HasForeignKey(e => e.MaRunId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.EsRun).WithMany().HasForeignKey(e => e.EsRunId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.Approvals).WithOne().HasForeignKey(a => a.BudgetId);
        });
        modelBuilder.Entity<AuditEntry>().HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<BusinessUnit>().HasOne(e => e.Business).WithMany(b => b.Units).HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AppUser>().HasOne(e => e.Unit).WithMany().HasForeignKey(e => e.UnitId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ExternalIndicator>().HasIndex(e => new { e.IndicatorCode, e.RelevantPeriod });
        modelBuilder.Entity<RuleAlert>().HasOne(e => e.Rule).WithMany(r => r.Alerts).HasForeignKey(e => e.RuleId).OnDelete(DeleteBehavior.Restrict);
    }
}
