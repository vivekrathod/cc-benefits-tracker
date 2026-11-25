using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CardBenefits.Infrastructure.Persistence;

/// <summary>Lets "dotnet ef migrations add" build the context without the host.</summary>
internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<TrackerDbContext>
{
    public TrackerDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TrackerDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
