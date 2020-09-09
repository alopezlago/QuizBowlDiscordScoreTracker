using System;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    // Tests need to access the Context, but we don't want to expose it from DatabaseAction in case we move away from
    // Entity Framework.
    // Approach taken from https://www.meziantou.net/testing-ef-core-in-memory-using-sqlite.htm
    public class InMemoryBotConfigurationContextFactory : IDisposable
    {
        private DbConnection connection;

        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Caller will dispose it through DatabaseAction's Dispose method")]
        public BotConfigurationContext Create()
        {
            if (this.connection == null)
            {
                // Use Sqlite so we're closer to how the product implements it
                this.connection = new SqliteConnection("DataSource=:memory:");
                this.connection.Open();
            }

            DbContextOptionsBuilder<BotConfigurationContext> optionsBuilder = new DbContextOptionsBuilder<BotConfigurationContext>()
                .UseSqlite(this.connection);

            return new BotConfigurationContext(optionsBuilder.Options);
        }

        private bool IsDisposed { get; set; }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.IsDisposed)
            {
                if (disposing)
                {
                    this.connection?.Dispose();
                }

                this.IsDisposed = true;
            }
        }

        public void Dispose()
        {
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
