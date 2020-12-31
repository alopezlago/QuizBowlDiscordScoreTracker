using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.Commands;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.Scoresheet;
using QuizBowlDiscordScoreTracker.TeamManager;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public sealed class AdminCommandHandlerTests : IDisposable
    {
        private const ulong DefaultReaderId = 1;
        private static readonly HashSet<ulong> DefaultIds = new HashSet<ulong>(new ulong[] { 1, 2, 3 });
        private static readonly string[] DefaultRoles = new string[] { $"{TeamRolePrefix} A", $"{TeamRolePrefix} B" };

        private const ulong DefaultChannelId = 11;
        private const ulong DefaultGuildId = 9;
        private const string TeamRolePrefix = "Team";

        private InMemoryBotConfigurationContextFactory botConfigurationfactory;

        private AdminCommandHandler Handler { get; set; }

        private IGoogleSheetsGeneratorFactory GoogleSheetsGeneratorFactory { get; set; }

        private IGuildTextChannel GuildTextChannel { get; set; }

        private MessageStore MessageStore { get; set; }

        [TestInitialize]
        public void InitializeTest()
        {
            this.botConfigurationfactory = new InMemoryBotConfigurationContextFactory();

            // Make sure the database is initialized before running the test
            using (BotConfigurationContext context = this.botConfigurationfactory.Create())
            {
                context.Database.Migrate();
            }

            // Clear out the old fields
            this.Handler = null;
            this.GoogleSheetsGeneratorFactory = null;
            this.GuildTextChannel = null;
            this.MessageStore = null;
        }

        [TestCleanup]
        public void Dispose()
        {
            this.botConfigurationfactory.Dispose();
        }

        [TestMethod]
        public async Task DisableBonusesAlways()
        {
            this.InitializeHandler();

            // Enable, then disable the bonuses
            using (BotConfigurationContext context = this.botConfigurationfactory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                await action.SetUseBonuses(DefaultGuildId, true);
            }

            await this.Handler.GetDefaultFormatAsync();
            Assert.AreEqual(
                1, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of messages after getting the team role");
            string getEmbed = this.MessageStore.ChannelEmbeds[0];
            Assert.IsTrue(
                getEmbed.Contains("Require scoring bonuses?: Yes", StringComparison.InvariantCulture),
                $"Enabled setting not in message \"{getEmbed}\"");
            this.MessageStore.Clear();

            await this.Handler.DisableBonusesByDefaultAsync();
            Assert.AreEqual(
                1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages after setting the team role");
            string setMessage = this.MessageStore.ChannelMessages[0];
            Assert.AreEqual(
                "Scoring bonuses will no longer be enabled for every game in this server.",
                setMessage,
                "Unexpected message when enabled");

            this.MessageStore.Clear();

            await this.Handler.GetDefaultFormatAsync();
            Assert.AreEqual(
                1, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of messages after getting the team role");
            getEmbed = this.MessageStore.ChannelEmbeds[0];
            Assert.IsTrue(
                getEmbed.Contains("Require scoring bonuses?: No", StringComparison.InvariantCulture),
                $"Disabled setting not in message \"{getEmbed}\"");
        }

        [TestMethod]
        public async Task EnableBonusesByDefault()
        {
            this.InitializeHandler();
            await this.Handler.EnableBonusesByDefaultAsync();
            Assert.AreEqual(
                1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages after setting the team role");
            string setMessage = this.MessageStore.ChannelMessages[0];
            Assert.AreEqual(
                "Scoring bonuses is now enabled for every game in this server.",
                setMessage,
                "Unexpected message when enabled");

            this.MessageStore.Clear();

            await this.Handler.GetDefaultFormatAsync();
            Assert.AreEqual(
                1, this.MessageStore.ChannelEmbeds.Count, "Unexpected number of messages after getting the team role");
            string getEmbed = this.MessageStore.ChannelEmbeds[0];
            Assert.IsTrue(
                getEmbed.Contains("Require scoring bonuses?: Yes", StringComparison.InvariantCulture),
                $"Enabled setting not in message \"{getEmbed}\"");
        }

        [TestMethod]
        public async Task SetReaderRole()
        {
            const string prefix = "Reader";
            const string newPrefix = "The Reader";

            this.InitializeHandler();
            await this.Handler.SetReaderRolePrefixAsync(prefix);
            Assert.AreEqual(
                1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages after setting the team role");
            string setMessage = this.MessageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains(prefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{setMessage}\"");

            this.MessageStore.Clear();

            await this.Handler.GetReaderRolePrefixAsync();
            Assert.AreEqual(
                1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages after getting the team role");
            string getMessage = this.MessageStore.ChannelMessages[0];
            Assert.IsTrue(
                getMessage.Contains(prefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{getMessage}\"");
            Assert.AreNotEqual(setMessage, getMessage, "Get and set messages should be different");

            this.MessageStore.Clear();

            await this.Handler.SetReaderRolePrefixAsync(newPrefix);
            Assert.AreEqual(
                1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages after updating the team role");
            setMessage = this.MessageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains(newPrefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{setMessage}\" after update");

            this.MessageStore.Clear();

            await this.Handler.GetReaderRolePrefixAsync();
            Assert.AreEqual(
                1,
                this.MessageStore.ChannelMessages.Count,
                "Unexpected number of messages when getting the team role after the update");
            getMessage = this.MessageStore.ChannelMessages[0];
            Assert.IsTrue(
                getMessage.Contains(prefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{getMessage}\" after update");
            Assert.AreNotEqual(setMessage, getMessage, "Get and set messages should be different after update");
        }

        [TestMethod]
        public async Task ClearReaderRolePrefix()
        {
            const string prefix = "Reader";
            this.InitializeHandler();

            await this.Handler.SetReaderRolePrefixAsync(prefix);
            Assert.AreEqual(
                1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages after setting the team role");
            string setMessage = this.MessageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains(prefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{setMessage}\"");

            this.MessageStore.Clear();

            await this.Handler.ClearReaderRolePrefixAsync();
            Assert.AreEqual(
                1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages after updating the team role");
            string clearMessage = this.MessageStore.ChannelMessages[0];
            Assert.IsTrue(
                clearMessage.Contains("unset", StringComparison.InvariantCulture),
                @$"""unset"" not in message ""{clearMessage}"" after update");

            this.MessageStore.Clear();

            await this.Handler.GetReaderRolePrefixAsync();
            Assert.AreEqual(
                1,
                this.MessageStore.ChannelMessages.Count,
                "Unexpected number of messages when getting the team role after the update");
            string getMessage = this.MessageStore.ChannelMessages[0];
            Assert.AreEqual("No reader prefix used", getMessage, $"The team role prefix was not cleared");
        }

        [TestMethod]
        public async Task SetTeamRole()
        {
            const string prefix = "Team #";
            const string newPrefix = "New Team #";

            this.InitializeHandler();
            await this.Handler.SetTeamRolePrefixAsync(prefix);
            Assert.AreEqual(
                1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages after setting the team role");
            string setMessage = this.MessageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains(prefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{setMessage}\"");

            this.MessageStore.Clear();

            await this.Handler.GetTeamRolePrefixAsync();
            Assert.AreEqual(
                1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages after getting the team role");
            string getMessage = this.MessageStore.ChannelMessages[0];
            Assert.IsTrue(
                getMessage.Contains(prefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{getMessage}\"");
            Assert.AreNotEqual(setMessage, getMessage, "Get and set messages should be different");

            this.MessageStore.Clear();

            await this.Handler.SetTeamRolePrefixAsync(newPrefix);
            Assert.AreEqual(
                1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages after updating the team role");
            setMessage = this.MessageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains(newPrefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{setMessage}\" after update");

            this.MessageStore.Clear();

            await this.Handler.GetTeamRolePrefixAsync();
            Assert.AreEqual(
                1,
                this.MessageStore.ChannelMessages.Count,
                "Unexpected number of messages when getting the team role after the update");
            getMessage = this.MessageStore.ChannelMessages[0];
            Assert.IsTrue(
                getMessage.Contains(prefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{getMessage}\" after update");
            Assert.AreNotEqual(setMessage, getMessage, "Get and set messages should be different after update");
        }

        [TestMethod]
        public async Task ClearTeamRole()
        {
            const string prefix = "Team #";
            this.InitializeHandler();

            await this.Handler.SetTeamRolePrefixAsync(prefix);
            Assert.AreEqual(
                1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages after setting the team role");
            string setMessage = this.MessageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains(prefix, StringComparison.InvariantCulture),
                $"Prefix not in message \"{setMessage}\"");

            this.MessageStore.Clear();

            await this.Handler.ClearTeamRolePrefixAsync();
            Assert.AreEqual(
                1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages after updating the team role");
            string clearMessage = this.MessageStore.ChannelMessages[0];
            Assert.IsTrue(
                clearMessage.Contains("unset", StringComparison.InvariantCulture),
                @$"""unset"" not in message ""{clearMessage}"" after update");

            this.MessageStore.Clear();

            await this.Handler.GetTeamRolePrefixAsync();
            Assert.AreEqual(
                1,
                this.MessageStore.ChannelMessages.Count,
                "Unexpected number of messages when getting the team role after the update");
            string getMessage = this.MessageStore.ChannelMessages[0];
            Assert.AreEqual("No team prefix used", getMessage, $"The team role prefix was not cleared");
        }

        [TestMethod]
        public async Task PairChannels()
        {
            const string voiceChannelName = "Packet Voice";
            const ulong voiceChannelId = DefaultChannelId + 10;

            this.InitializeHandler(voiceChannelId, voiceChannelName);

            await this.Handler.PairChannelsAsync(this.GuildTextChannel, voiceChannelName);

            // TODO: Check the exact string once this issue is fixed:
            // https://github.com/alopezlago/QuizBowlDiscordScoreTracker/issues/23
            Assert.AreEqual(
                1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages after pairing channels");
            string setMessage = this.MessageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains("success", StringComparison.InvariantCulture),
                @$"Pairing message doesn't mention ""success"". Message: {setMessage}");
            this.MessageStore.Clear();

            await this.Handler.GetPairedChannelAsync(this.GuildTextChannel);

            Assert.AreEqual(
                1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages after pairing channels");
            string getMessage = this.MessageStore.ChannelMessages[0];
            Assert.IsTrue(
                getMessage.Contains(voiceChannelName, StringComparison.InvariantCulture),
                $"Voice channel name not found in get message. Message: {getMessage}");
        }

        [TestMethod]
        public async Task UnpairChannel()
        {
            const string voiceChannelName = "Packet Voice";
            const ulong voiceChannelId = DefaultChannelId + 10;
            this.InitializeHandler(voiceChannelId, voiceChannelName);

            await this.Handler.PairChannelsAsync(this.GuildTextChannel, voiceChannelName);

            // TODO: Check the exact string once this issue is fixed:
            // https://github.com/alopezlago/QuizBowlDiscordScoreTracker/issues/23
            Assert.AreEqual(
                1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages after pairing channels");
            string setMessage = this.MessageStore.ChannelMessages[0];
            Assert.IsTrue(
                setMessage.Contains("success", StringComparison.InvariantCultureIgnoreCase),
                @$"Pairing message doesn't mention ""success"". Message: {setMessage}");
            this.MessageStore.Clear();

            await this.Handler.UnpairChannelAsync(this.GuildTextChannel);

            Assert.AreEqual(
                1, this.MessageStore.ChannelMessages.Count, "Unexpected number of messages after pairing channels");
            string getMessage = this.MessageStore.ChannelMessages[0];
            Assert.IsTrue(
                getMessage.Contains("unpair", StringComparison.InvariantCultureIgnoreCase),
                @$"Unpairing message doesn't mention ""unpaired"". Message: {getMessage}");
        }

        [TestMethod]
        public async Task SetRostersFromRolesForTJSheetsFails()
        {
            await this.SetRostersFromRolesForGoogleSheetsFails(
                GoogleSheetsType.TJ, (url) => this.Handler.SetRostersFromRolesForTJ(url));
        }

        [TestMethod]
        public async Task SetRostersFromRolesForTJSheetsSucceeds()
        {
            await this.SetRostersFromRolesForGoogleSheetsSucceeds(
                GoogleSheetsType.TJ, (url) => this.Handler.SetRostersFromRolesForTJ(url));
        }

        [TestMethod]
        public async Task SetRostersFromRolesForTJSheetsWithoutByRoleTeamsFails()
        {
            await this.SetRostersFromRolesForGoogleSheetsWithoutByRoleTeamsFails(
                GoogleSheetsType.TJ, (url) => this.Handler.SetRostersFromRolesForTJ(url));
        }

        [TestMethod]
        public async Task SetRostersFromRolesForTJSheetsWithBadUrlFails()
        {
            await this.SetRostersFromRolesForGoogleSheetsWithBadUrlFails(
                GoogleSheetsType.TJ, (url) => this.Handler.SetRostersFromRolesForTJ(url));
        }

        [TestMethod]
        public async Task SetRostersFromRolesForUCSDFails()
        {
            await this.SetRostersFromRolesForGoogleSheetsFails(
                GoogleSheetsType.UCSD, (url) => this.Handler.SetRostersFromRolesForUCSD(url));
        }

        [TestMethod]
        public async Task SetRostersFromRolesForUCSDSucceeds()
        {
            await this.SetRostersFromRolesForGoogleSheetsSucceeds(
                GoogleSheetsType.UCSD, (url) => this.Handler.SetRostersFromRolesForUCSD(url));
        }

        [TestMethod]
        public async Task SetRostersFromRolesForUCSDWithoutByRoleTeamsFails()
        {
            await this.SetRostersFromRolesForGoogleSheetsWithoutByRoleTeamsFails(
                GoogleSheetsType.UCSD, (url) => this.Handler.SetRostersFromRolesForUCSD(url));
        }

        [TestMethod]
        public async Task SetRostersFromRolesForUCSDWithBadUrlFails()
        {
            await this.SetRostersFromRolesForGoogleSheetsWithBadUrlFails(
                GoogleSheetsType.UCSD, (url) => this.Handler.SetRostersFromRolesForUCSD(url));
        }

        private async Task SetRostersFromRolesForGoogleSheetsFails(
            GoogleSheetsType type, Func<string, Task> setRosters)
        {
            const string errorMessage = "API call failed";

            Mock<IGoogleSheetsGenerator> mockGenerator = new Mock<IGoogleSheetsGenerator>();
            mockGenerator
                .Setup(generator => generator.TryUpdateRosters(It.IsAny<ITeamManager>(), It.IsAny<Uri>()))
                .Returns(Task.FromResult<IResult<string>>(new FailureResult<string>(errorMessage)))
                .Verifiable();

            Mock<IGoogleSheetsGeneratorFactory> mockFactory = new Mock<IGoogleSheetsGeneratorFactory>();
            mockFactory
                .Setup(factory => factory.Create(type))
                .Returns(mockGenerator.Object);

            this.InitializeHandler(googleSheetsGeneratorFactory: mockFactory.Object);

            using (BotConfigurationContext context = this.botConfigurationfactory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                await action.SetTeamRolePrefixAsync(DefaultGuildId, TeamRolePrefix);
            }

            await setRosters("http://localhost/sheetsUrl");

            mockFactory.Verify();
            this.MessageStore.VerifyChannelMessages(errorMessage);
        }

        private async Task SetRostersFromRolesForGoogleSheetsSucceeds(
            GoogleSheetsType type, Func<string, Task> setRosters)
        {
            Mock<IGoogleSheetsGenerator> mockGenerator = new Mock<IGoogleSheetsGenerator>();
            mockGenerator
                .Setup(generator => generator.TryUpdateRosters(It.IsAny<ITeamManager>(), It.IsAny<Uri>()))
                .Returns(Task.FromResult<IResult<string>>(new SuccessResult<string>(string.Empty)))
                .Verifiable();

            Mock<IGoogleSheetsGeneratorFactory> mockFactory = new Mock<IGoogleSheetsGeneratorFactory>();
            mockFactory
                .Setup(factory => factory.Create(type))
                .Returns(mockGenerator.Object);

            this.InitializeHandler(googleSheetsGeneratorFactory: mockFactory.Object);

            using (BotConfigurationContext context = this.botConfigurationfactory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                await action.SetTeamRolePrefixAsync(DefaultGuildId, TeamRolePrefix);
            }

            await setRosters("http://localhost/sheetsUrl");

            mockFactory.Verify();
            this.MessageStore.VerifyChannelMessages("Rosters updated.");
        }

        private async Task SetRostersFromRolesForGoogleSheetsWithoutByRoleTeamsFails(
            GoogleSheetsType type, Func<string, Task> setRosters)
        {
            Mock<IGoogleSheetsGenerator> mockGenerator = new Mock<IGoogleSheetsGenerator>();
            mockGenerator
                .Setup(generator => generator.TryUpdateRosters(It.IsAny<ITeamManager>(), It.IsAny<Uri>()))
                .Returns(Task.FromResult<IResult<string>>(new SuccessResult<string>(string.Empty)))
                .Verifiable();

            Mock<IGoogleSheetsGeneratorFactory> mockFactory = new Mock<IGoogleSheetsGeneratorFactory>();
            mockFactory
                .Setup(factory => factory.Create(type))
                .Returns(mockGenerator.Object);

            this.InitializeHandler(googleSheetsGeneratorFactory: mockFactory.Object);

            await setRosters("http://localhost/sheetsUrl");

            this.MessageStore.VerifyChannelMessages(
                "Couldn't export to the rosters sheet. This server is not using the team role prefix. Use !setTeamRolePrefix to set the prefix for role names to use for teams.");
            mockFactory.Verify(factory => factory.Create(It.IsAny<GoogleSheetsType>()), Times.Never);
        }

        private async Task SetRostersFromRolesForGoogleSheetsWithBadUrlFails(
            GoogleSheetsType type, Func<string, Task> setRosters)
        {
            Mock<IGoogleSheetsGenerator> mockGenerator = new Mock<IGoogleSheetsGenerator>();
            mockGenerator
                .Setup(generator => generator.TryUpdateRosters(It.IsAny<ITeamManager>(), It.IsAny<Uri>()))
                .Returns(Task.FromResult<IResult<string>>(new SuccessResult<string>(string.Empty)));

            Mock<IGoogleSheetsGeneratorFactory> mockFactory = new Mock<IGoogleSheetsGeneratorFactory>();
            mockFactory
                .Setup(factory => factory.Create(type))
                .Returns(mockGenerator.Object);

            this.InitializeHandler(googleSheetsGeneratorFactory: mockFactory.Object);

            using (BotConfigurationContext context = this.botConfigurationfactory.Create())
            using (DatabaseAction action = new DatabaseAction(context))
            {
                await action.SetTeamRolePrefixAsync(DefaultGuildId, TeamRolePrefix);
            }

            await setRosters("this URL does not parse");

            this.MessageStore.VerifyChannelMessages(
                "The link to the Google Sheet wasn't understandable. Be sure to copy the full URL from the address bar.");
            mockFactory.Verify(factory => factory.Create(It.IsAny<GoogleSheetsType>()), Times.Never);
        }

        private void InitializeHandler(
            ulong voiceChannelId = 9999,
            string voiceChannelName = "Voice",
            IGoogleSheetsGeneratorFactory googleSheetsGeneratorFactory = null)
        {
            this.MessageStore = new MessageStore();
            ICommandContext commandContext = CommandMocks.CreateCommandContext(
                this.MessageStore,
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
                    mockGuild
                        .Setup(guild => guild.Roles)
                        .Returns(DefaultRoles.Select((role, index) =>
                        {
                            Mock<IRole> mockRole = new Mock<IRole>();
                            mockRole
                                .Setup(r => r.Name)
                                .Returns(role);
                            mockRole
                                .Setup(r => r.Id)
                                .Returns((ulong)index);
                            return mockRole.Object;
                        }).ToArray());
                },
                out IGuildTextChannel guildTextChannel);
            this.GuildTextChannel = guildTextChannel;
            IOptionsMonitor<BotConfiguration> options = CommandMocks.CreateConfigurationOptionsMonitor();
            IDatabaseActionFactory dbActionFactory = CommandMocks.CreateDatabaseActionFactory(
                this.botConfigurationfactory);
            this.GoogleSheetsGeneratorFactory = googleSheetsGeneratorFactory ?? (new Mock<IGoogleSheetsGeneratorFactory>()).Object;

            this.Handler = new AdminCommandHandler(commandContext, dbActionFactory, this.GoogleSheetsGeneratorFactory);
        }
    }
}
