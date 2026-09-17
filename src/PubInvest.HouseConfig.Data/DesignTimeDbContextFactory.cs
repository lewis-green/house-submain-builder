using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PubInvest.HouseConfig.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HouseConfigDbContext>
{
    public HouseConfigDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<HouseConfigDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=HouseConfig;Username=postgres;Password=postgres")
            .Options);
}
