// Database context: maps classes to database tables
using BudgetApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Data;

public class BudgetDbContext(DbContextOptions<BudgetDbContext> options) : DbContext(options)
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BudgetSubmission>(entity =>
        {
            entity.ToTable("budget_submission");
            entity.HasKey(e => e.SubmissionId);
            entity.Property(e => e.SubmissionId).HasColumnName("submission_id").HasMaxLength(32);
            entity.Property(e => e.UnitId).HasColumnName("unit_id").HasMaxLength(32);
            entity.Property(e => e.SubmittedBy).HasColumnName("submitted_by").HasMaxLength(32);
            entity.Property(e => e.PeriodStart).HasColumnName("period_start");
            entity.Property(e => e.PeriodEnd).HasColumnName("period_end");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(e => e.SubmittedAt).HasColumnName("submitted_at");
            entity
                .Property(e => e.ReplacesSubmissionId)
                .HasColumnName("replaces_submission_id")
                .HasMaxLength(32);
            entity.Property(e => e.ImportMethod).HasColumnName("import_method").HasMaxLength(20);
            entity.Property(e => e.SourceFileName).HasColumnName("source_file_name").HasMaxLength(255);
            entity.Property(e => e.SourceScale).HasColumnName("source_scale");

            entity
                .HasOne(e => e.Submitter)
                .WithMany()
                .HasForeignKey(e => e.SubmittedBy)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.LineItems).WithOne().HasForeignKey(i => i.SubmissionId);
            entity.HasMany(e => e.Rows).WithOne().HasForeignKey(r => r.SubmissionId);
            entity.HasMany(e => e.Approvals).WithOne().HasForeignKey(a => a.SubmissionId);
        });

        modelBuilder.Entity<FinancialLineItem>(entity =>
        {
            entity.ToTable("financial_line_item");
            entity.HasKey(e => e.LineItemId);
            entity.Property(e => e.LineItemId).HasColumnName("line_item_id").HasMaxLength(32);
            entity.Property(e => e.SubmissionId).HasColumnName("submission_id").HasMaxLength(32);
            entity.Property(e => e.Category).HasColumnName("category").HasMaxLength(50);
            entity.Property(e => e.PeriodStart).HasColumnName("period_start");
            entity.Property(e => e.Amount).HasColumnName("amount").HasPrecision(18, 2);
            entity.Property(e => e.Frequency).HasColumnName("frequency").HasMaxLength(20);
        });

        modelBuilder.Entity<SubmissionRow>(entity =>
        {
            entity.ToTable("submission_row");
            entity.HasKey(e => e.RowId);
            entity.Property(e => e.RowId).HasColumnName("row_id").HasMaxLength(32);
            entity.Property(e => e.SubmissionId).HasColumnName("submission_id").HasMaxLength(32);
            entity.Property(e => e.RowOrder).HasColumnName("row_order");
            entity.Property(e => e.RowKind).HasColumnName("row_kind").HasMaxLength(20);
            entity.Property(e => e.SourceLabel).HasColumnName("source_label").HasMaxLength(200);
            entity.Property(e => e.SubtotalCode).HasColumnName("subtotal_code").HasMaxLength(30);
            entity.Property(e => e.Amounts).HasColumnName("amounts").HasColumnType("numeric(20,6)[]");
            entity.Property(e => e.Mapping).HasColumnName("mapping").HasColumnType("jsonb");
            entity.Property(e => e.HandMapped).HasColumnName("hand_mapped");
        });

        modelBuilder.Entity<ApprovalAction>(entity =>
        {
            entity.ToTable("approval_action");
            entity.HasKey(e => e.ApprovalId);
            entity.Property(e => e.ApprovalId).HasColumnName("approval_id").HasMaxLength(32);
            entity.Property(e => e.SubmissionId).HasColumnName("submission_id").HasMaxLength(32);
            entity.Property(e => e.BudgetId).HasColumnName("budget_id").HasMaxLength(32);
            entity.Property(e => e.ActedBy).HasColumnName("acted_by").HasMaxLength(32);
            entity.Property(e => e.Decision).HasColumnName("decision").HasMaxLength(20);
            entity.Property(e => e.Comments).HasColumnName("comments").HasMaxLength(2000);
            entity.Property(e => e.ActedAt).HasColumnName("acted_at");

            entity
                .HasOne(e => e.Actor)
                .WithMany()
                .HasForeignKey(e => e.ActedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ImportMapping>(entity =>
        {
            entity.ToTable("import_mapping");
            entity.HasKey(e => e.MappingId);
            entity.Property(e => e.MappingId).HasColumnName("mapping_id").HasMaxLength(32);
            entity.Property(e => e.BusinessId).HasColumnName("business_id").HasMaxLength(32);
            entity.Property(e => e.SourceLabel).HasColumnName("source_label").HasMaxLength(200);
            entity.Property(e => e.LabelKey).HasColumnName("label_key").HasMaxLength(200);
            entity.Property(e => e.CategoryCode).HasColumnName("category_code").HasMaxLength(50);
            entity.Property(e => e.SplitPercent).HasColumnName("split_percent").HasPrecision(5, 2);
            entity.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(32);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<ForecastRun>(entity =>
        {
            entity.ToTable("forecast_run");
            entity.HasKey(e => e.RunId);
            entity.Property(e => e.RunId).HasColumnName("run_id").HasMaxLength(32);
            entity.Property(e => e.UnitId).HasColumnName("unit_id").HasMaxLength(32);
            entity.Property(e => e.ForecastingMethod).HasColumnName("forecasting_method").HasMaxLength(30);
            entity.Property(e => e.HistoryStart).HasColumnName("history_start");
            entity.Property(e => e.HistoryEnd).HasColumnName("history_end");
            entity.Property(e => e.Parameters).HasColumnName("parameters").HasColumnType("jsonb");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasMany(e => e.Values).WithOne().HasForeignKey(v => v.RunId);
        });

        modelBuilder.Entity<ForecastValue>(entity =>
        {
            entity.ToTable("forecast_value");
            entity.HasKey(e => e.ValueId);
            entity.Property(e => e.ValueId).HasColumnName("value_id").HasMaxLength(32);
            entity.Property(e => e.RunId).HasColumnName("run_id").HasMaxLength(32);
            entity.Property(e => e.Category).HasColumnName("category").HasMaxLength(50);
            entity.Property(e => e.ForecastPeriod).HasColumnName("forecast_period");
            entity.Property(e => e.BaselineAmount).HasColumnName("baseline_amount").HasPrecision(18, 2);
            entity.Property(e => e.AdjustedAmount).HasColumnName("adjusted_amount").HasPrecision(18, 2);
        });

        modelBuilder.Entity<BudgetSelection>(entity =>
        {
            entity.ToTable("budget_selection");
            entity.HasKey(e => e.BudgetId);
            entity.Property(e => e.BudgetId).HasColumnName("budget_id").HasMaxLength(32);
            entity.Property(e => e.UnitId).HasColumnName("unit_id").HasMaxLength(32);
            entity.Property(e => e.MaRunId).HasColumnName("ma_run_id").HasMaxLength(32);
            entity.Property(e => e.EsRunId).HasColumnName("es_run_id").HasMaxLength(32);
            entity.Property(e => e.BudgetMethod).HasColumnName("budget_method").HasMaxLength(30);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(e => e.ChosenBy).HasColumnName("chosen_by").HasMaxLength(32);
            entity.Property(e => e.ChosenAt).HasColumnName("chosen_at");

            entity
                .HasOne(e => e.Chooser)
                .WithMany()
                .HasForeignKey(e => e.ChosenBy)
                .OnDelete(DeleteBehavior.Restrict);
            entity
                .HasOne(e => e.MaRun)
                .WithMany()
                .HasForeignKey(e => e.MaRunId)
                .OnDelete(DeleteBehavior.Restrict);
            entity
                .HasOne(e => e.EsRun)
                .WithMany()
                .HasForeignKey(e => e.EsRunId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.Approvals).WithOne().HasForeignKey(a => a.BudgetId);
        });

        modelBuilder.Entity<KpiTarget>(entity =>
        {
            entity.ToTable("kpi_target");
            entity.HasKey(e => e.TargetId);
            entity.Property(e => e.TargetId).HasColumnName("target_id").HasMaxLength(32);
            entity.Property(e => e.UnitId).HasColumnName("unit_id").HasMaxLength(32);
            entity.Property(e => e.MetricName).HasColumnName("metric_name").HasMaxLength(100);
            entity.Property(e => e.TargetValue).HasColumnName("target_value").HasPrecision(18, 2);
            entity.Property(e => e.MeasurementUnit).HasColumnName("measurement_unit").HasMaxLength(50);
            entity.Property(e => e.TargetPeriod).HasColumnName("target_period");
        });

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("audit_log");
            entity.HasKey(e => e.LogId);
            entity.Property(e => e.LogId).HasColumnName("log_id").HasMaxLength(32);
            entity.Property(e => e.BusinessId).HasColumnName("business_id").HasMaxLength(32);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(32);
            entity.Property(e => e.UserName).HasColumnName("user_name").HasMaxLength(200);
            entity.Property(e => e.Area).HasColumnName("area").HasMaxLength(20);
            entity.Property(e => e.Action).HasColumnName("action").HasMaxLength(20);
            entity.Property(e => e.Summary).HasColumnName("summary").HasMaxLength(500);
            entity.Property(e => e.Details).HasColumnName("details").HasColumnType("jsonb");
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at");

            entity
                .HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Business>(entity =>
        {
            entity.ToTable("business");
            entity.HasKey(e => e.BusinessId);
            entity.Property(e => e.BusinessId).HasColumnName("business_id").HasMaxLength(32);
            entity.Property(e => e.LegalName).HasColumnName("legal_name").HasMaxLength(200);
            entity.Property(e => e.Industry).HasColumnName("industry").HasMaxLength(100);
            entity.Property(e => e.Country).HasColumnName("country").HasMaxLength(3);
            entity.Property(e => e.YearEndMonth).HasColumnName("year_end_month");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<BusinessUnit>(entity =>
        {
            entity.ToTable("business_unit");
            entity.HasKey(e => e.UnitId);
            entity.Property(e => e.UnitId).HasColumnName("unit_id").HasMaxLength(32);
            entity.Property(e => e.BusinessId).HasColumnName("business_id").HasMaxLength(32);
            entity.Property(e => e.UnitName).HasColumnName("unit_name").HasMaxLength(200);
            entity.Property(e => e.UnitType).HasColumnName("unit_type").HasMaxLength(20);
            entity.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(3);

            entity
                .HasOne(e => e.Business)
                .WithMany(b => b.Units)
                .HasForeignKey(e => e.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("app_user");
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(32);
            entity.Property(e => e.UnitId).HasColumnName("unit_id").HasMaxLength(32);
            entity.Property(e => e.FullName).HasColumnName("full_name").HasMaxLength(200);
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(254);
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash").HasMaxLength(255);
            entity.Property(e => e.Role).HasColumnName("role").HasMaxLength(20);
            entity.Property(e => e.ActiveStatus).HasColumnName("active_status").HasMaxLength(20);
            entity.Property(e => e.IsAdmin).HasColumnName("is_admin");
            entity.Property(e => e.MustChangePassword).HasColumnName("must_change_password");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");

            entity
                .HasOne(e => e.Unit)
                .WithMany()
                .HasForeignKey(e => e.UnitId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PasswordReset>(entity =>
        {
            entity.ToTable("password_reset");
            entity.HasKey(e => e.ResetId);
            entity.Property(e => e.ResetId).HasColumnName("reset_id").HasMaxLength(32);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(32);
            entity.Property(e => e.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.UsedAt).HasColumnName("used_at");

            entity
                .HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LineCategory>(entity =>
        {
            entity.ToTable("line_category");
            entity.HasKey(e => e.CategoryCode);
            entity.Property(e => e.CategoryCode).HasColumnName("category_code").HasMaxLength(50);
            entity.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(100);
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.TeamManaged).HasColumnName("team_managed");
        });

        modelBuilder.Entity<IndicatorType>(entity =>
        {
            entity.ToTable("indicator_type");
            entity.HasKey(e => e.IndicatorCode);
            entity.Property(e => e.IndicatorCode).HasColumnName("indicator_code").HasMaxLength(50);
            entity.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(200);
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(1000);
            entity
                .Property(e => e.MeasurementUnit)
                .HasColumnName("measurement_unit")
                .HasMaxLength(50);
            entity
                .Property(e => e.OfficialSource)
                .HasColumnName("official_source")
                .HasMaxLength(500);
        });

        modelBuilder.Entity<IndicatorCategoryLink>(entity =>
        {
            entity.ToTable("indicator_category_link");
            entity.HasKey(e => new { e.IndicatorCode, e.CategoryCode });
            entity.Property(e => e.IndicatorCode).HasColumnName("indicator_code").HasMaxLength(50);
            entity.Property(e => e.CategoryCode).HasColumnName("category_code").HasMaxLength(50);

            entity
                .HasOne(e => e.IndicatorType)
                .WithMany(t => t.CategoryLinks)
                .HasForeignKey(e => e.IndicatorCode);

            entity.HasOne(e => e.Category).WithMany().HasForeignKey(e => e.CategoryCode);
        });

        modelBuilder.Entity<DataSource>(entity =>
        {
            entity.ToTable("data_source");
            entity.HasKey(e => e.SourceId);
            entity.Property(e => e.SourceId).HasColumnName("source_id");
            entity.Property(e => e.SourceName).HasColumnName("source_name").HasMaxLength(200);
            entity
                .Property(e => e.SourceIdentifier)
                .HasColumnName("source_identifier")
                .HasMaxLength(500);
            entity.Property(e => e.SourceType).HasColumnName("source_type").HasMaxLength(50);
            entity.Property(e => e.TrustedStatus).HasColumnName("trusted_status").HasMaxLength(50);
        });

        modelBuilder.Entity<ExternalIndicator>(entity =>
        {
            entity.ToTable("external_indicator");
            entity.HasKey(e => e.IndicatorId);
            entity.Property(e => e.IndicatorId).HasColumnName("indicator_id");
            entity.Property(e => e.SourceId).HasColumnName("source_id");
            entity.Property(e => e.IndicatorCode).HasColumnName("indicator_code").HasMaxLength(50);
            entity.Property(e => e.IndicatorName).HasColumnName("indicator_name").HasMaxLength(200);
            entity
                .Property(e => e.IndicatorValue)
                .HasColumnName("indicator_value")
                .HasPrecision(18, 6);
            entity
                .Property(e => e.MeasurementUnit)
                .HasColumnName("measurement_unit")
                .HasMaxLength(50);
            entity
                .Property(e => e.RelevantPeriod)
                .HasColumnName("relevant_period")
                .HasMaxLength(20);
            entity
                .Property(e => e.ValidationStatus)
                .HasColumnName("validation_status")
                .HasMaxLength(50);
            entity.Property(e => e.CollectedAt).HasColumnName("collected_at");

            entity
                .HasOne(e => e.Source)
                .WithMany(s => s.Indicators)
                .HasForeignKey(e => e.SourceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.IndicatorCode, e.RelevantPeriod });
        });

        modelBuilder.Entity<RuleDefinition>(entity =>
        {
            entity.ToTable("rule_definition");
            entity.HasKey(e => e.RuleId);
            entity.Property(e => e.RuleId).HasColumnName("rule_id");
            entity.Property(e => e.BusinessId).HasColumnName("business_id").HasMaxLength(32);
            entity.Property(e => e.RuleName).HasColumnName("rule_name").HasMaxLength(200);
            entity.Property(e => e.Condition).HasColumnName("condition").HasMaxLength(500);
            entity.Property(e => e.Action).HasColumnName("action").HasMaxLength(500);
            entity.Property(e => e.IndicatorCode).HasColumnName("indicator_code").HasMaxLength(50);
            entity
                .Property(e => e.ThresholdValue)
                .HasColumnName("threshold_value")
                .HasPrecision(18, 6);
            entity.Property(e => e.ActiveStatus).HasColumnName("active_status").HasMaxLength(50);
        });

        modelBuilder.Entity<RuleAlert>(entity =>
        {
            entity.ToTable("rule_alert");
            entity.HasKey(e => e.AlertId);
            entity.Property(e => e.AlertId).HasColumnName("alert_id");
            entity.Property(e => e.RuleId).HasColumnName("rule_id");
            entity.Property(e => e.UnitId).HasColumnName("unit_id");
            entity.Property(e => e.RunId).HasColumnName("run_id");
            entity.Property(e => e.Severity).HasColumnName("severity").HasMaxLength(50);
            entity.Property(e => e.Message).HasColumnName("message").HasMaxLength(1000);
            entity
                .Property(e => e.ResolutionStatus)
                .HasColumnName("resolution_status")
                .HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity
                .HasOne(e => e.Rule)
                .WithMany(r => r.Alerts)
                .HasForeignKey(e => e.RuleId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
