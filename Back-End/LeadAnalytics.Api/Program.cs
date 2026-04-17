using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Options;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Variavel de ambiente não encontrada para conexão do banco");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

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

builder.Services.AddScoped<LeadService>();
builder.Services.AddScoped<UnitService>();
builder.Services.AddScoped<AttendantService>();
builder.Services.AddScoped<IRelatorioService, RelatorioService>();
builder.Services.AddSingleton<IPdfRelatorioService, PdfRelatorioService>();
builder.Services.AddHttpClient<MetricsService>();
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

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "API V1");
    c.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseCors("AllowAll");        // ← antes de Authentication
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();