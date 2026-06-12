using LeadAnalytics.Api.Adapters;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Jobs;
using LeadAnalytics.Api.Middleware;
using LeadAnalytics.Api.Options;
using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Insights;
using LeadAnalytics.Api.Swagger;
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
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));

var jwtSection = builder.Configuration.GetSection("Jwt");

var jwtSecret = jwtSection["Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret não foi configurado.");

var keyBytes = Convert.FromBase64String(jwtSecret);

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "LeadAnalytics API",
        Version = "v1",
    });

    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "JWT. Cole no formato: Bearer {seu_token}",
        In = Microsoft.OpenApi.ParameterLocation.Header,
        Type = Microsoft.OpenApi.SecuritySchemeType.ApiKey,
    });

    options.DocumentFilter<AuthorizeOperationFilter>();
});

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

        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtAuth");
                logger.LogWarning(
                    "JWT auth falhou em {Path}: {Message}",
                    ctx.Request.Path, ctx.Exception.Message);
                return Task.CompletedTask;
            },
            OnChallenge = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtAuth");
                logger.LogWarning(
                    "JWT challenge 401 em {Path}: error={Error}, description={Desc}",
                    ctx.Request.Path, ctx.Error, ctx.ErrorDescription);
                return Task.CompletedTask;
            },
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
builder.Services.AddScoped<SyncN8N>();
builder.Services.AddScoped<DailyRelatoryService>();
builder.Services.AddScoped<LeadAttributionService>();
builder.Services.AddScoped<MetaWebhookService>();
builder.Services.AddScoped<ConfigurationService>();
builder.Services.AddScoped<LeadAnalyticsService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddHttpClient<ResendClient>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddHttpClient<GoogleAuthService>();
builder.Services.AddScoped<InvitationService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<LoginSessionService>();
builder.Services.AddScoped<AdminLogService>();
builder.Services.AddHttpClient<GeoIpService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddScoped<AuthService>();  // ← apenas uma vez
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ContactService>();
builder.Services.AddScoped<ContactImportService>();
builder.Services.AddScoped<CloudiaCsvImportService>();
builder.Services.AddScoped<CloudiaKommoPatchService>();
builder.Services.AddSingleton<ICloudiaKommoPatchJobQueue, InMemoryCloudiaKommoPatchJobQueue>();
builder.Services.AddHostedService<CloudiaKommoPatchJobWorker>();
builder.Services.AddHttpClient("kommo");
builder.Services.AddScoped<PaymentService>();

builder.Services.AddScoped<LeadEventService>();
builder.Services.AddScoped<LeadTimelineService>();
builder.Services.AddScoped<KpiConfigService>();
builder.Services.AddScoped<DuplicateContactService>();

// ── Insights (CAPI mockado + analytics agregadas) ────────────────────────────
builder.Services.AddScoped<MetaCapiService>();
builder.Services.AddScoped<InsightsService>();

// ── Background jobs: delete em lote de contatos duplicados ───────────────────
builder.Services.AddSingleton<IDuplicateDeleteJobQueue, InMemoryDuplicateDeleteJobQueue>();
builder.Services.AddScoped<DuplicateDeleteJobStore>();
builder.Services.AddHostedService<DuplicateDeleteJobWorker>();

// Deduplicação de LEADS (mantém o mais avançado; tagueia "DUPLICADO" na Kommo + apaga)
builder.Services.AddScoped<DuplicateLeadService>();
builder.Services.AddSingleton<ILeadDuplicateDeleteJobQueue, InMemoryLeadDuplicateDeleteJobQueue>();
builder.Services.AddScoped<LeadDuplicateDeleteJobStore>();
builder.Services.AddHostedService<LeadDuplicateDeleteJobWorker>();

// Deduplicação DIRETO na Kommo (lê a API ao vivo e marca a tag DUPLICADO lá)
builder.Services.AddScoped<KommoDedupService>();
builder.Services.AddSingleton<IKommoDedupJobQueue, InMemoryKommoDedupJobQueue>();
builder.Services.AddScoped<KommoDedupJobStore>();
builder.Services.AddHostedService<KommoDedupJobWorker>();

// ── Background jobs: bulk delete genérico de contatos (por IDs ou filtros) ───
builder.Services.AddSingleton<IContactsBulkDeleteJobQueue, InMemoryContactsBulkDeleteJobQueue>();
builder.Services.AddScoped<ContactsBulkDeleteJobStore>();
builder.Services.AddHostedService<ContactsBulkDeleteJobWorker>();

// ── Background jobs ──────────────────────────────────────────────────────────
builder.Services.AddHostedService<LeadAnalytics.Api.Jobs.AlertaPreenchimentoPendenteJob>();
builder.Services.AddHostedService<LeadAnalytics.Api.Jobs.AlertaPagamentoAtrasadoJob>();
builder.Services.AddHostedService<LeadAnalytics.Api.Jobs.RecalculoKpisJob>();
builder.Services.AddHostedService<LeadAnalytics.Api.Jobs.KommoSyncPeriodicJob>();
builder.Services.AddHostedService<LeadAnalytics.Api.Jobs.KommoNightlySyncJob>();
builder.Services.AddHostedService<LeadAnalytics.Api.Jobs.KommoStageBackfillJob>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<TenantUnitGuard>();
builder.Services.AddScoped<KommoAdapter>();
builder.Services.AddScoped<LeadAnalytics.Api.Service.Stages.KommoStageProcessor>();
builder.Services.AddScoped<KommoIngestionService>();
builder.Services.AddScoped<KommoSyncService>();
builder.Services.AddScoped<KommoStageHistoryBackfillService>();
builder.Services.AddScoped<ResgateAttemptBackfillService>();
builder.Services.AddScoped<ConsultasBackfillService>();
builder.Services.AddScoped<KommoConversationsImporter>();
builder.Services.AddScoped<AgentIngestionService>();
builder.Services.AddHttpClient<KommoApiClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<WebhookExecutionLogger>();

// ── I.A. (OpenAI GPT-4o-mini + Whisper) ──────────────────────────────────────
builder.Services.AddHttpClient<LeadAnalytics.Api.Service.Ai.OpenAiClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddScoped<LeadAnalytics.Api.Service.Ai.AiKeyStorage>();
builder.Services.AddScoped<LeadAnalytics.Api.Service.Ai.UnitEntryStageConfig>();
builder.Services.AddScoped<LeadAnalytics.Api.Service.Ai.AiAnalyticsService>();
builder.Services.AddScoped<LeadAnalytics.Api.Service.Ai.AiToolRegistry>();
builder.Services.AddScoped<LeadAnalytics.Api.Service.Ai.KommoStagesResolver>();
builder.Services.AddScoped<LeadAnalytics.Api.Service.Ai.LeadSearchService>();

// ── Central de Integrações (Meta / Google Ads) ───────────────────────────────
builder.Services.AddScoped<ProtectedTokenService>();
builder.Services.AddScoped<LeadAnalytics.Api.Service.Ads.AdsCredentialsService>();
builder.Services.AddScoped<LeadAnalytics.Api.Service.Ads.AdsSpendSyncService>();
builder.Services.AddHttpClient<LeadAnalytics.Api.Service.Ads.MetaAdsProvider>(c =>
    c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<LeadAnalytics.Api.Service.Ads.GoogleAdsProvider>(c =>
    c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<LeadAnalytics.Api.Service.Ads.IAdsProvider>(
    sp => sp.GetRequiredService<LeadAnalytics.Api.Service.Ads.MetaAdsProvider>());
builder.Services.AddScoped<LeadAnalytics.Api.Service.Ads.IAdsProvider>(
    sp => sp.GetRequiredService<LeadAnalytics.Api.Service.Ads.GoogleAdsProvider>());
builder.Services.AddHostedService<LeadAnalytics.Api.Jobs.AdsSpendSyncJob>();

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
        // Timeout generoso: o default (30s) estoura em migrations que mexem em tabelas
        // grandes (ADD COLUMN com rewrite, CREATE INDEX, UPDATE em massa) e a exceção
        // é engolida abaixo → app sobe com schema incompleto → 500 nas queries novas.
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        db.Database.Migrate();
        startupLogger.LogInformation("Migrations aplicadas com sucesso no startup.");
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Falha ao aplicar migrations no startup.");
    }
}

// Bootstrap: garante que os emails em Auth:SuperAdminEmails têm
// User com Role = "super_admin". Sem isso ninguém faria o primeiro login.
try
{
    await SuperAdminSeedService.EnsureAsync(app.Services);
}
catch (Exception ex)
{
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    startupLogger.LogError(ex, "Falha no seed de super-admin");
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

// Bloqueia escrita para papéis somente-leitura (trafego_pago) antes de auditar.
app.UseMiddleware<ReadOnlyRoleMiddleware>();

app.UseMiddleware<AuditLogMiddleware>();

app.MapControllers();

app.Run();