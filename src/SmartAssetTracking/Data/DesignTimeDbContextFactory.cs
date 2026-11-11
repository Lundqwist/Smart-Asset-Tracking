using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SmartAssetTracking.Data;

/// <summary>Used by the "dotnet ef" tooling to create the DbContext when adding migrations.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
            .Options;

        return new AppDbContext(options);
    }
}
