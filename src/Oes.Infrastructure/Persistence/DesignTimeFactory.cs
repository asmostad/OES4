using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Oes.Infrastructure.Tenancy;

namespace Oes.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` to build the model for migrations. No database connection is made.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<OesDbContext>
{
    public OesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OesDbContext>()
            .UseMySQL("Server=localhost;Database=oes4_design;User=design;Password=design")
            .Options;
        return new OesDbContext(options, new TenantContext());
    }
}
