using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using QuizBowlDiscordScoreTracker.Commands;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public sealed class AdminCommandHandlerTests : IDisposable
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
        public async Task SetTeamRole()
        {
            const string prefix = "Team #";
            const string newPrefix = "New Team #";

            this.CreateHandler(
                out AdminCommandHandler handler,
                out MessageStore messageStore);
            await handler.SetTeamRolePrefixAsync(prefix);
            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after setting the team role");
            string setMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains(prefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{setMessage}\"");

            messageStore.Clear();

            await handler.GetTeamRolePrefixAsync();
            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after getting the team role");
            string getMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                getMessage.Contains(prefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{getMessage}\"");
            Assert.AreNotEqual(setMessage, getMessage, "Get and set messages should be different");

            messageStore.Clear();

            await handler.SetTeamRolePrefixAsync(newPrefix);
            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after updating the team role");
            setMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains(newPrefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{setMessage}\" after update");

            messageStore.Clear();

            await handler.GetTeamRolePrefixAsync();
            Assert.AreEqual(
                1,
                messageStore.ChannelMessages.Count,
                "Unexpected number of messages when getting the team role after the update");
            getMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                getMessage.Contains(prefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{getMessage}\" after update");
            Assert.AreNotEqual(setMessage, getMessage, "Get and set messages should be different after update");
        }

        [TestMethod]
        public async Task ClearTeamRole()
        {
            const string prefix = "Team #";
            this.CreateHandler(
                out AdminCommandHandler handler,
                out MessageStore messageStore);

            await handler.SetTeamRolePrefixAsync(prefix);
            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after setting the team role");
            string setMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains(prefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{setMessage}\"");

            messageStore.Clear();

            await handler.ClearTeamRolePrefixAsync();
            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after updating the team role");
            string clearMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                clearMessage.Contains("unset", StringComparison.InvariantCulture),
                @$"""unset"" not in message ""{clearMessage}"" after update");

            messageStore.Clear();

            await handler.GetTeamRolePrefixAsync();
            Assert.AreEqual(
                1,
                messageStore.ChannelMessages.Count,
                "Unexpected number of messages when getting the team role after the update");
            string getMessage = messageStore.ChannelMessages[0];
            Assert.AreEqual("No team prefix used", getMessage, $"The team role prefix was not cleared");
        }

        [TestMethod]
        public async Task PairChannels()
        {
            const string voiceChannelName = "Packet Voice";
            const ulong voiceChannelId = DefaultChannelId + 10;

            this.CreateHandler(
                voiceChannelId,
                voiceChannelName,
                out AdminCommandHandler handler,
                out MessageStore messageStore,
                out IGuildTextChannel textChannel);

            await handler.PairChannelsAsync(textChannel, voiceChannelName);

            // TODO: Check the exact string once this issue is fixed:
            // https://github.com/alopezlago/QuizBowlDiscordScoreTracker/issues/23
            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after pairing channels");
            string setMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains("success", StringComparison.InvariantCulture),
                @$"Pairing message doesn't mention ""success"". Message: {setMessage}");
            messageStore.Clear();

            await handler.GetPairedChannelAsync(textChannel);

            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after pairing channels");
            string getMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                getMessage.Contains(voiceChannelName, StringComparison.InvariantCulture),
                $"Voice channel name not found in get message. Message: {getMessage}");
        }

        [TestMethod]
        public async Task UnpairChannel()
        {
            const string voiceChannelName = "Packet Voice";
            const ulong voiceChannelId = DefaultChannelId + 10;
            this.CreateHandler(
                voiceChannelId,
                voiceChannelName,
                out AdminCommandHandler handler,
                out MessageStore messageStore,
                out IGuildTextChannel textChannel);

            await handler.PairChannelsAsync(textChannel, voiceChannelName);

            // TODO: Check the exact string once this issue is fixed:
            // https://github.com/alopezlago/QuizBowlDiscordScoreTracker/issues/23
            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after pairing channels");
            string setMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains("success", StringComparison.InvariantCultureIgnoreCase),
                @$"Pairing message doesn't mention ""success"". Message: {setMessage}");
            messageStore.Clear();

            await handler.UnpairChannelAsync(textChannel);

            Assert.AreEqual(
                1, messageStore.ChannelMessages.Count, "Unexpected number of messages after pairing channels");
            string getMessage = messageStore.ChannelMessages[0];
            Assert.IsTrue(
                getMessage.Contains("unpair", StringComparison.InvariantCultureIgnoreCase),
                @$"Unpairing message doesn't mention ""unpaired"". Message: {getMessage}");
        }

        private void CreateHandler(
            out AdminCommandHandler handler,
            out MessageStore messageStore)
        {
            this.CreateHandler(
                9999,
                "Voice",
                out handler,
                out messageStore,
                out _);
        }

        private void CreateHandler(
            ulong voiceChannelId,
            string voiceChannelName,
            out AdminCommandHandler handler,
            out MessageStore messageStore,
            out IGuildTextChannel guildTextChannel)
        {
            messageStore = new MessageStore();
            ICommandContext commandContext = CommandMocks.CreateCommandContext(
                messageStore,
                DefaultIds,
                DefaultGuildId,
                DefaultChannelId,
                DefaultReaderId,
                (mockGuild, textChannel) =>
                {
                    Mock<IVoiceChannel> mockVoiceChannel = new Mock<IVoiceChannel>();
                    mockVoiceChannel.Setup(voiceChannel => voiceChannel.Id).Returns(voiceChannelId);
                    mockVoiceChannel.Setup(voiceChannel => voiceChannel.Name).Returns(voiceChannelName);
                    mockGuild
                        .Setup(guild => guild.GetVoiceChannelAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                        .Returns(Task.FromResult(mockVoiceChannel.Object));

                    List<IVoiceChannel> voiceChannels = new List<IVoiceChannel>()
                    {
                        mockVoiceChannel.Object
                    };
                    mockGuild
                        .Setup(guild => guild.GetVoiceChannelsAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                        .Returns(Task.FromResult<IReadOnlyCollection<IVoiceChannel>>(voiceChannels));
                },
                out guildTextChannel);
            IDatabaseActionFactory dbActionFactory = CommandMocks.CreateDatabaseActionFactory(
                this.botConfigurationfactory);

            handler = new AdminCommandHandler(commandContext, dbActionFactory);
        }
    }
}
