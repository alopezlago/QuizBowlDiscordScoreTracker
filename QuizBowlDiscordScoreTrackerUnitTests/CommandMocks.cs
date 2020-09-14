using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Options;
using Moq;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    // TODO: Refactor. Perhaps this needs an options class and an object that returns all of the
    // mocks/values we'd want
    public static class CommandMocks
    {

        public static ICommandContext CreateCommandContext(
            MessageStore messageStore,
            HashSet<ulong> existingUserIds,
            ulong guildId,
            ulong messageChannelId,
            ulong userId)
        {
            return CreateCommandContext(
                messageStore,
                existingUserIds,
                guildId,
                messageChannelId,
                voiceChannelId: 7777,
                voiceChannelName: "Voice",
                userId: userId,
                out _);
        }

        public static ICommandContext CreateCommandContext(
             MessageStore messageStore,
             HashSet<ulong> existingUserIds,
             ulong guildId,
             ulong messageChannelId,
             ulong voiceChannelId,
             string voiceChannelName,
             ulong userId,
             out IGuildTextChannel guildTextChannel)
        {
            Mock<ICommandContext> mockCommandContext = new Mock<ICommandContext>();

            Mock<IGuildTextChannel> mockMessageChannel = new Mock<IGuildTextChannel>();
            Mock<IUserMessage> mockUserMessage = new Mock<IUserMessage>();
            mockMessageChannel
                .Setup(channel => channel.Id)
                .Returns(messageChannelId);
            mockMessageChannel
                .Setup(channel => channel.SendMessageAsync(It.IsAny<string>(), false, null, It.IsAny<RequestOptions>()))
                .Returns<string, bool, Embed, RequestOptions>((message, isTTS, embed, options) =>
                {
                    messageStore.ChannelMessages.Add(message);
                    return Task.FromResult(mockUserMessage.Object);
                });
            mockMessageChannel
                .Setup(channel => channel.SendMessageAsync(null, false, It.IsAny<Embed>(), It.IsAny<RequestOptions>()))
                .Returns<string, bool, Embed, RequestOptions>((message, isTTS, embed, options) =>
                {
                    messageStore.ChannelEmbeds.Add(GetMockEmbedText(embed));
                    return Task.FromResult(mockUserMessage.Object);
                });
            mockMessageChannel
                .Setup(channel => channel.Name)
                .Returns("gameChannel");

            Mock<IGuild> mockGuild = new Mock<IGuild>();
            mockGuild
                .Setup(guild => guild.GetUserAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns<ulong, CacheMode, RequestOptions>((id, cacheMode, requestOptions) =>
                {
                    if (existingUserIds?.Contains(id) == true)
                    {
                        return Task.FromResult(CreateGuildUser(id));
                    }

                    return Task.FromResult<IGuildUser>(null);
                });
            mockGuild.Setup(guild => guild.Id).Returns(guildId);

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

            Mock<IGuildUser> mockBotUser = new Mock<IGuildUser>();
            mockBotUser
                .Setup(user => user.GetPermissions(It.IsAny<IGuildChannel>()))
                .Returns(new ChannelPermissions(viewChannel: true, sendMessages: true, embedLinks: true));
            mockGuild
                .Setup(guild => guild.GetCurrentUserAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns(Task.FromResult(mockBotUser.Object));

            mockMessageChannel
                .Setup(channel => channel.Guild)
                .Returns(mockGuild.Object);

            mockCommandContext
                .Setup(context => context.User)
                .Returns(CreateGuildUser(userId));
            mockCommandContext
                .Setup(context => context.Channel)
                .Returns(mockMessageChannel.Object);
            mockCommandContext
                .Setup(context => context.Guild)
                .Returns(mockGuild.Object);

            guildTextChannel = mockMessageChannel.Object;
            return mockCommandContext.Object;
        }

        public static IGuildUser CreateGuildUser(ulong id)
        {
            Mock<IGuildUser> mockUser = new Mock<IGuildUser>();
            mockUser
                .Setup(user => user.Id)
                .Returns(id);
            mockUser
                .Setup(user => user.Mention)
                .Returns($"@User_{id}");
            mockUser
                .Setup(user => user.Username)
                .Returns($"User_{id}");
            return mockUser.Object;
        }

        public static IOptionsMonitor<BotConfiguration> CreateConfigurationOptionsMonitor()
        {
            Mock<IOptionsMonitor<BotConfiguration>> mockOptionsMonitor = new Mock<IOptionsMonitor<BotConfiguration>>();
            Mock<BotConfiguration> mockConfiguration = new Mock<BotConfiguration>();
            mockConfiguration
                .Setup(config => config.DatabaseDataSource)
                .Returns("memory&cached=true");

            // We can't set the WebURL directly without making it virtual or adding an interface for BotConfiguration
            mockOptionsMonitor.Setup(options => options.CurrentValue).Returns(mockConfiguration.Object);
            return mockOptionsMonitor.Object;
        }

        public static IDatabaseActionFactory CreateDatabaseActionFactory(InMemoryBotConfigurationContextFactory factory)
        {
            // TODO: See how we can dispose this correctly

            Mock<IDatabaseActionFactory> mockDbActionFactory = new Mock<IDatabaseActionFactory>();
            mockDbActionFactory
                .Setup(dbActionFactory => dbActionFactory.Create())
                .Returns(() => new DatabaseAction(factory.Create()));
            return mockDbActionFactory.Object;
        }

        public static string GetMockEmbedText(IEmbed embed)
        {
            if (embed == null)
            {
                throw new ArgumentNullException(nameof(embed));
            }

            return GetMockEmbedText(
                embed.Title, embed.Description, embed.Fields.ToDictionary(field => field.Name, field => field.Value));
        }

        private static string GetMockEmbedText(string title, string description, IDictionary<string, string> fields = null)
        {
            string fieldsText = string.Empty;
            if (fields != null)
            {
                fieldsText = string.Join(
                    Environment.NewLine, fields.Select(field => $"{field.Key}: {field.Value}"));
            }
            string embedText = fieldsText.Length > 0 ?
                $"{title}{Environment.NewLine}{description}{Environment.NewLine}{fieldsText}" :
                $"{title}{Environment.NewLine}{description}";
            return embedText;
        }
    }
}
