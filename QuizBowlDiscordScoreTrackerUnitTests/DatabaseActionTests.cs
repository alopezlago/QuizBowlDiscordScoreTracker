using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public class DatabaseActionTests
    {
        private const ulong guildId = 1234;

        [TestMethod]
        public async Task PairNewChannelAndUpdateChannel()
        {
            const ulong textChannelId = 12345;
            const ulong voiceChannelId = 123456;
            const ulong newVoiceChannelId = 123567;

            using (InMemoryBotConfigurationContextFactory factory = new InMemoryBotConfigurationContextFactory())
            using (BotConfigurationContext context = factory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                await action.MigrateAsync();
                await action.PairChannelsAsync(guildId, new (ulong, ulong)[] { (textChannelId, voiceChannelId) });

                ulong? pairedVoiceChannelId = await action.GetPairedVoiceChannelIdOrNullAsync(textChannelId);
                Assert.AreEqual(voiceChannelId, pairedVoiceChannelId, "Voice channel wasn't paired");

                await action.PairChannelsAsync(guildId, new (ulong, ulong)[] { (textChannelId, newVoiceChannelId) });
                pairedVoiceChannelId = await action.GetPairedVoiceChannelIdOrNullAsync(textChannelId);
                Assert.AreEqual(newVoiceChannelId, pairedVoiceChannelId, "Voice channel wasn't updated");
            }
        }

        [TestMethod]
        public async Task UnpairChannel()
        {
            const ulong textChannelId = 12345;
            const ulong voiceChannelId = 123456;

            using (InMemoryBotConfigurationContextFactory factory = new InMemoryBotConfigurationContextFactory())
            using (BotConfigurationContext context = factory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                await action.MigrateAsync();
                ulong? pairedVoiceChannelId = await action.GetPairedVoiceChannelIdOrNullAsync(textChannelId);
                Assert.IsNull(pairedVoiceChannelId, "Voice channel wasn't null initially");

                await action.PairChannelsAsync(guildId, new (ulong, ulong)[] { (textChannelId, voiceChannelId) });
                pairedVoiceChannelId = await action.GetPairedVoiceChannelIdOrNullAsync(textChannelId);
                Assert.AreEqual(voiceChannelId, pairedVoiceChannelId, "Voice channel wasn't paired.");

                await action.UnpairChannelAsync(textChannelId);
                pairedVoiceChannelId = await action.GetPairedVoiceChannelIdOrNullAsync(textChannelId);
                Assert.IsNull(pairedVoiceChannelId, "Voice channel should be unpaired");

                bool anyTextChannels = await context.TextChannels.AnyAsync();
                Assert.IsFalse(anyTextChannels, "Text channel wasn't removed after the last setting was");

                bool anyGuilds = await context.Guilds.AnyAsync();
                Assert.IsFalse(anyGuilds, "Guild wasn't removed after the last setting was");
            }
        }

        [TestMethod]
        public async Task SetTeamRole()
        {
            const string prefix = "Team ";
            const string newPrefix = "New Team ";

            using (InMemoryBotConfigurationContextFactory factory = new InMemoryBotConfigurationContextFactory())
            using (BotConfigurationContext context = factory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                await action.MigrateAsync();
                string teamRolePrefix = await action.GetTeamRolePrefixAsync(guildId);
                Assert.IsNull(teamRolePrefix, "Team role prefix should be uninitialized");

                await action.SetTeamRolePrefixAsync(guildId, prefix);
                teamRolePrefix = await action.GetTeamRolePrefixAsync(guildId);
                Assert.AreEqual(prefix, teamRolePrefix, "Team role prefix was not set");

                await action.SetTeamRolePrefixAsync(guildId, newPrefix);
                teamRolePrefix = await action.GetTeamRolePrefixAsync(guildId);
                Assert.AreEqual(newPrefix, teamRolePrefix, "Team role prefix was not updated");
            }
        }

        [TestMethod]
        public async Task ClearTeamRole()
        {
            const string prefix = "Team ";

            using (InMemoryBotConfigurationContextFactory factory = new InMemoryBotConfigurationContextFactory())
            using (BotConfigurationContext context = factory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                await action.MigrateAsync();
                string teamRolePrefix = await action.GetTeamRolePrefixAsync(guildId);
                Assert.IsNull(teamRolePrefix, "Team role prefix should be uninitialized");

                await action.SetTeamRolePrefixAsync(guildId, prefix);
                teamRolePrefix = await action.GetTeamRolePrefixAsync(guildId);
                Assert.AreEqual(prefix, teamRolePrefix, "Team role prefix was not set");

                await action.ClearTeamRolePrefixAsync(guildId);

                bool anyGuilds = await context.Guilds.AnyAsync();
                Assert.IsFalse(anyGuilds, "Guild wasn't removed after the last setting was");

                teamRolePrefix = await action.GetTeamRolePrefixAsync(guildId);
                Assert.IsNull(teamRolePrefix, "Team role prefix should be cleared");
            }
        }
    }
}
