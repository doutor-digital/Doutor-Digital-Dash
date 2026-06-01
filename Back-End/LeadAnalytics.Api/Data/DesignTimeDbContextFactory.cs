using LeadAnalytics.Api.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LeadAnalytics.Api.Data;

/// <summary>
/// Usado apenas pelas ferramentas do EF (<c>dotnet ef migrations add</c> /
/// <c>database update</c>). O <see cref="AppDbContext"/> exige um
/// <see cref="ICurrentUser"/> no construtor; em design-time fornecemos um stub
/// (não há request HTTP). A connection string vem do ambiente quando presente,
/// senão um placeholder — só o modelo é necessário para gerar migrações.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=designtime;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(cs)
            .Options;

        return new AppDbContext(options, new NoOpCurrentUser());
    }

    private sealed class NoOpCurrentUser : ICurrentUser
    {
        public int? UserId => null;
        public int? TenantId => null;
        public string? Role => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsAdminLevel => false;
        public bool IsReadOnly => false;
        public bool IsAuthenticated => false;
        public long? SessionId => null;
    }
}
