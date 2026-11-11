// ============================================================================
// DesignTimeDbContextFactory.cs
// ----------------------------------------------------------------------------
// This class is NOT used when the program runs. It is only used by the
// command-line tool "dotnet ef" (for example "dotnet ef migrations add ...").
// The tool needs to create an AppDbContext to look at our model, but it doesn't
// start our Program.cs, so this class tells it how to build one: read the
// connection string from appsettings.json and use SQL Server.
// ============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SmartAssetTracking.Data;

/// <summary>Used by the "dotnet ef" tooling to create the DbContext when adding migrations.</summary>
// ": IDesignTimeDbContextFactory<AppDbContext>" means this class "implements an interface".
// An interface is a contract: a list of methods a class promises to have. The EF
// tool searches our project for a class that fulfils this contract and calls CreateDbContext.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Read settings from appsettings.json in the current folder.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        // "var" lets the compiler work out the type for us (here DbContextOptions<AppDbContext>).
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
            .Options;

        return new AppDbContext(options);
    }
}
