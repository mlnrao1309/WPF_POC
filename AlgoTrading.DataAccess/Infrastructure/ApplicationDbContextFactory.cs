using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AlgoTrading.DataAccess.Infrastructure.DatabaseContext
{
    /// <summary>
    /// Design-time factory so <c>dotnet ef migrations</c> can build the model without starting the WPF host.
    /// The placeholder connection string is never opened while scaffolding migrations.
    /// </summary>
    public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            DbContextOptionsBuilder<ApplicationDbContext> builder = new DbContextOptionsBuilder<ApplicationDbContext>();
            builder.UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=Algo_Trading_CFCore_Design;Trusted_Connection=True;TrustServerCertificate=True;");
            return new ApplicationDbContext(builder.Options);
        }
    }
}
