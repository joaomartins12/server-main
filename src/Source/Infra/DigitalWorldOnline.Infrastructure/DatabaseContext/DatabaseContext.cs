using DigitalWorldOnline.Commons.Utils;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DigitalWorldOnline.Infrastructure
{
    public partial class DatabaseContext : DbContext
    {
        private const string DatabaseConnectionString = "Database:Connection";
        private readonly IConfiguration _configuration;
        private readonly bool _cliInitialization;

        public DatabaseContext()
        {
            _cliInitialization = true;
        }

        public DatabaseContext(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var connectionString = _configuration?.GetConnectionString("Default")
                    ?? "Server=38.143.19.42,1433;Database=dmo;User Id=sa;Password=teste12!;Encrypt=False;TrustServerCertificate=True;Pooling=true;Min Pool Size=50;Max Pool Size=1000;Connection Lifetime=180;";

                optionsBuilder.UseSqlServer(connectionString, sqlServerOptions =>
                {
                    sqlServerOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(3), null);
                });

                // Add command interceptor to log each DB command and its duration
                optionsBuilder.AddInterceptors(new DbCommandLoggerInterceptor(_configuration));
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            SharedEntityConfiguration(modelBuilder);
            AccountEntityConfiguration(modelBuilder);
            AssetsEntityConfiguration(modelBuilder);
            CharacterEntityConfiguration(modelBuilder);
            ConfigEntityConfiguration(modelBuilder);
            DigimonEntityConfiguration(modelBuilder);
            EventEntityConfiguration(modelBuilder);
            SecurityEntityConfiguration(modelBuilder);
            ShopEntityConfiguration(modelBuilder);
            MechanicsEntityConfiguration(modelBuilder);
            RoutineEntityConfiguration(modelBuilder);
            ArenaEntityConfiguration(modelBuilder);
        }
    }
}