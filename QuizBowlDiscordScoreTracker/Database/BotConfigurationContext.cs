using System;
using System.IO;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace QuizBowlDiscordScoreTracker.Database
{
    // To perform migrations with the CLI:
    // (Make sure you have run this first: dotnet tool install --global dotnet-ef)
    // dotnet add package Microsoft.EntityFrameworkCore.Design
    // dotnet ef migrations add InitialCreate
    // dotnet ef database update
    //
    // To perform migrations with Visual Studio (Package Manager Console)
    // Add-Migration InitialCreate
    // Update-Database
    //
    // When you migrate, consult the site below to make sure migrations are safe.
    // https://docs.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing?tabs=dotnet-core-cli

    // Using EF Core for now. If we need more speed, then switch to Dapper.
    public class BotConfigurationContext : DbContext
    {
        public const string DefaultDatabase = "botConfiguration.db";

        // Needed for migrations
        public BotConfigurationContext() : this((string)null)
        {
        }

        public BotConfigurationContext(string dataSource)
        {
            this.DataSource = dataSource ?? Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), DefaultDatabase);
        }

        public BotConfigurationContext(DbContextOptions<BotConfigurationContext> options) : base(options)
        {
        }

        public DbSet<GuildSetting> Guilds { get; set; }

        public DbSet<TextChannelSetting> TextChannels { get; set; }

        private string DataSource { get; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (optionsBuilder == null)
            {
                throw new ArgumentNullException(nameof(optionsBuilder));
            }

            if (!optionsBuilder.IsConfigured)
            {
                // Use SQL Lite for now, since traffic should be low and write operations should be infrequent.
                SqliteConnectionStringBuilder connectionStringBuilder = new SqliteConnectionStringBuilder()
                {
                    DataSource = this.DataSource
                };
                optionsBuilder.UseSqlite(connectionStringBuilder.ToString());
            }
        }
    }
}
