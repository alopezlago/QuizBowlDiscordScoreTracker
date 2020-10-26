using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.Commands;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public sealed class BotOwnerCommandHandlerTests : IDisposable
    {
        private const ulong DefaultReaderId = 1;
        private static readonly HashSet<ulong> DefaultIds = new HashSet<ulong>(new ulong[] { 1, 2, 3 });

        private const ulong DefaultChannelId = 11;
        private const ulong DefaultGuildId = 9;

        private InMemoryBotConfigurationContextFactory botConfigurationfactory;

        [TestInitialize]
        public void InitializeTest()
        {
            this.botConfigurationfactory = new InMemoryBotConfigurationContextFactory();

            // Make sure the database is initialized before running the test
            using (BotConfigurationContext context = this.botConfigurationfactory.Create())
            {
                context.Database.Migrate();
            }
        }

        [TestCleanup]
        public void Dispose()
        {
            this.botConfigurationfactory.Dispose();
        }

        [TestMethod]
        public async Task BanUser()
        {
            const ulong bannedUser = 123;
            this.CreateHandler(out BotOwnerCommandHandler handler, out MessageStore messageStore);

            using (BotConfigurationContext context = this.botConfigurationfactory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                bool banned = await action.GetCommandBannedAsync(bannedUser);
                Assert.IsFalse(banned, "User shouldn't be banned initially");
            }

            await handler.BanUserAsync(bannedUser);

            using (BotConfigurationContext context = this.botConfigurationfactory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                bool banned = await action.GetCommandBannedAsync(bannedUser);
                Assert.IsTrue(banned, "User should be banned");
            }
        }

        [TestMethod]
        public async Task UnbanUser()
        {
            const ulong bannedUser = 123;
            this.CreateHandler(out BotOwnerCommandHandler handler, out MessageStore messageStore);

            using (BotConfigurationContext context = this.botConfigurationfactory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                await action.AddCommandBannedUser(bannedUser);
            }

            await handler.UnbanUserAsync(bannedUser);

            using (BotConfigurationContext context = this.botConfigurationfactory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                bool banned = await action.GetCommandBannedAsync(bannedUser);
                Assert.IsFalse(banned, "User should be unbanned");
            }
        }

        private void CreateHandler(out BotOwnerCommandHandler handler, out MessageStore messageStore)
        {
            messageStore = new MessageStore();
            ICommandContext commandContext = CommandMocks.CreateCommandContext(
                messageStore,
                DefaultIds,
                DefaultGuildId,
                DefaultChannelId,
                DefaultReaderId);
            IDatabaseActionFactory dbActionFactory = CommandMocks.CreateDatabaseActionFactory(
                this.botConfigurationfactory);
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();

            handler = new BotOwnerCommandHandler(commandContext, options, dbActionFactory);
        }
    }
}
