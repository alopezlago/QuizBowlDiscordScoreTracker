using System.IO;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace QuizBowlDiscordScoreTracker.Database
{
    // Approach taken from https://www.meziantou.net/testing-ef-core-in-memory-using-sqlite.htm
    public class SqliteDatabaseActionFactory : IDatabaseActionFactory
    {
        public SqliteDatabaseActionFactory(string dataSource)
        {
            dataSource = dataSource ?? Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                BotConfigurationContext.DefaultDatabase);

            SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder
            {
                DataSource = dataSource
            };
            this.ConnectionString = builder.ConnectionString;
        }

        private string ConnectionString { get; set; }

        public DatabaseAction Create()
        {
            DbContextOptionsBuilder<BotConfigurationContext> optionsBuilder = new DbContextOptionsBuilder<BotConfigurationContext>()
                .UseSqlite(this.ConnectionString);

            return new DatabaseAction(new BotConfigurationContext(optionsBuilder.Options));
        }
    }
}
