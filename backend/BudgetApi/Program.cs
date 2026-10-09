// App startup: registers services and starts the API
using BudgetApi.Data;
using BudgetApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
if (jwt.Key.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key must be at least 32 characters. Set JWT_KEY in .env (Docker) or the Jwt:Key user secret (dotnet run)."
    );
}

builder.Services.AddControllers().AddValidationResponses();
builder.Services.AddErrorResponses();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste the token from POST /api/auth/login.",
        }
    );
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
    });
});

builder.Services.AddDbContext<BudgetDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(builder.Configuration.GetSection("Mail").Get<MailSettings>() ?? new MailSettings());
builder.Services.AddSingleton<Mailer>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<BusinessScope>();
builder.Services.AddScoped<ForecastRuns>();
builder.Services.AddScoped<AuditLog>();
builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = jwt.SigningKey(),
            NameClaimType = "name",
            RoleClaimType = "role",
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddAuthRateLimits();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()
    )
);

builder.Services.AddHttpClient<WorldBankClient>();
builder.Services.AddHttpClient<OecdClient>();
builder.Services.AddHttpClient<ForecastClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ForecastService:BaseUrl"] ?? "http://localhost:5090/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<RuleEngine>();
builder.Services.AddScoped<IngestionService>();

var app = builder.Build();

app.UseErrorResponses();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
