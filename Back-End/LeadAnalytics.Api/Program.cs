using LeadAnalytics.Api.Adapters;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Options;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Variavel de ambiente não encontrada para conexão do banco");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddDataProtection()
    .SetApplicationName("LeadAnalytics.Api")
    .PersistKeysToDbContext<AppDbContext>();

builder.Services.AddControllers();

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));

var jwtSection = builder.Configuration.GetSection("Jwt");

var jwtSecret = jwtSection["Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret não foi configurado.");

var keyBytes = Convert.FromBase64String(jwtSecret);

builder.Services.AddSwaggerGen();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"] ?? "LeadAnalytics.Api",
            ValidAudience = jwtSection["Audience"] ?? "LeadAnalytics.Frontend",
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

// ── Cache distribuído (Redis no Railway; fallback em memória se não configurado) ──
var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? builder.Configuration["Redis:ConnectionString"]
    ?? Environment.GetEnvironmentVariable("REDIS_URL");

if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = "LeadAnalytics:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

// ── Log viewer em memória (buffer circular dos últimos 5000 logs) ──
builder.Services.AddSingleton<InMemoryLogStore>(_ => new InMemoryLogStore(capacity: 5000));
builder.Services.AddSingleton<ILoggerProvider>(sp => new InMemoryLoggerProvider(
    sp.GetRequiredService<InMemoryLogStore>(),
    sp.GetRequiredService<IHttpContextAccessor>()));

builder.Services.Configure<LogsAuthOptions>(builder.Configuration.GetSection(LogsAuthOptions.SectionName));
builder.Services.AddSingleton<LogsAuthService>();

builder.Services.AddScoped<LeadService>();
builder.Services.AddScoped<UnitService>();
builder.Services.AddScoped<AttendantService>();
builder.Services.AddScoped<IRelatorioService, RelatorioService>();
builder.Services.AddSingleton<IPdfRelatorioService, PdfRelatorioService>();
builder.Services.AddHttpClient<MetricsService>();
builder.Services.AddSingleton<CloudiaTokenProvider>();
builder.Services.AddScoped<MetricsService>();
builder.Services.AddScoped<SyncN8N>();
builder.Services.AddScoped<DailyRelatoryService>();
builder.Services.AddScoped<LeadAttributionService>();
builder.Services.AddScoped<MetaWebhookService>();
builder.Services.AddScoped<ConfigurationService>();
builder.Services.AddScoped<LeadAnalyticsService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<AuthService>();  // ← apenas uma vez
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ContactService>();
builder.Services.AddScoped<ContactImportService>();
builder.Services.AddScoped<PaymentService>();

builder.Services.AddScoped<LeadEventService>();
builder.Services.AddScoped<LeadTimelineService>();
builder.Services.AddScoped<DuplicateContactService>();
builder.Services.AddScoped<CloudiaAdapter>();
builder.Services.AddScoped<KommoAdapter>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy
            .SetIsOriginAllowed(_ => true)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("Content-Disposition")
            .AllowCredentials());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        db.Database.Migrate();
        startupLogger.LogInformation("Migrations aplicadas com sucesso no startup.");
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Falha ao aplicar migrations no startup.");
    }
}

app.UseCors("AllowAll");

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Unhandled exception em {Path}", context.Request.Path);

        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var origin = context.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin))
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = origin;
                context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
                context.Response.Headers["Vary"] = "Origin";
            }

            await context.Response.WriteAsJsonAsync(new
            {
                type = "about:blank",
                title = "Internal Server Error",
                status = 500,
                detail = app.Environment.IsDevelopment() ? ex.Message : "Erro interno no servidor."
            });
        }
    }
});

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "API V1");
    c.RoutePrefix = "swagger";
});

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();