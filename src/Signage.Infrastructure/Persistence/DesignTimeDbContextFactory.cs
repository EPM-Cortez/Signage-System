using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Signage.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SignageDbContext>
{
    public SignageDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SignageDbContext>()
            .UseSqlite("Data Source=signage-design.db")
            .Options;
        return new SignageDbContext(options);
    }
}
