using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Moq;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.Web;

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
            ulong userId,
            Action<Mock<IGuild>, IGuildTextChannel> updateMockGuild = null)
        {
            return CreateCommandContext(
                messageStore,
                existingUserIds,
                guildId,
                messageChannelId,
                userId: userId,
                updateMockGuild: updateMockGuild,
                out _);
        }

        public static ICommandContext CreateCommandContext(
             MessageStore messageStore,
             HashSet<ulong> existingUserIds,
             ulong guildId,
             ulong messageChannelId,
             ulong userId,
             Action<Mock<IGuild>, IGuildTextChannel> updateMockGuild,
             out IGuildTextChannel guildTextChannel)
        {
            Mock<ICommandContext> mockCommandContext = new Mock<ICommandContext>();
            IGuild guild = CreateGuild(
                messageStore,
                existingUserIds,
                guildId,
                messageChannelId,
                updateMockGuild,
                null,
                out guildTextChannel);

            mockCommandContext
                .Setup(context => context.User)
                .Returns(CreateGuildUser(userId));
            mockCommandContext
                .Setup(context => context.Channel)
                .Returns(guildTextChannel);
            mockCommandContext
                .Setup(context => context.Guild)
                .Returns(guild);

            return mockCommandContext.Object;
        }

        public static IGuild CreateGuild(
            MessageStore messageStore,
            HashSet<ulong> existingUserIds,
            ulong guildId,
            ulong messageChannelId,
            Action<Mock<IGuild>, IGuildTextChannel> updateMockGuild,
            Action<Mock<IGuildTextChannel>> updateMockTextChannel,
            out IGuildTextChannel channel)
        {
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

            mockGuild
                .Setup(guild => guild.GetUsersAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns<CacheMode, RequestOptions>((cacheMode, requestOptions) =>
                {
                    IReadOnlyCollection<IGuildUser> users = existingUserIds.Select(id => CreateGuildUser(id)).ToList();
                    return Task.FromResult(users);
                });

            Mock<IGuildUser> mockBotUser = new Mock<IGuildUser>();
            mockBotUser
                .Setup(user => user.GetPermissions(It.IsAny<IGuildChannel>()))
                .Returns(new ChannelPermissions(viewChannel: true, sendMessages: true, embedLinks: true));
            mockGuild
                .Setup(guild => guild.GetCurrentUserAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns(Task.FromResult(mockBotUser.Object));

            IGuild guild = mockGuild.Object;
            channel = CreateGuildTextChannel(messageStore, guild, messageChannelId, updateMockTextChannel);

            updateMockGuild?.Invoke(mockGuild, channel);
            return guild;
        }

        public static IGuildUser CreateGuildUser(ulong id, Action<Mock<IGuildUser>> updateMock = null)
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

            updateMock?.Invoke(mockUser);
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
            Mock<IDatabaseActionFactory> mockDbActionFactory = new Mock<IDatabaseActionFactory>();
            mockDbActionFactory
                .Setup(dbActionFactory => dbActionFactory.Create())
                .Returns(() => new DatabaseAction(factory.Create()));
            return mockDbActionFactory.Object;
        }

        public static IHubContext<MonitorHub> CreateHubContext()
        {
            Mock<IHubContext<MonitorHub>> mockHubContext = new Mock<IHubContext<MonitorHub>>();
            Mock<IHubClients> mockHubClients = new Mock<IHubClients>();
            Mock<IClientProxy> mockClientProxy = new Mock<IClientProxy>();

            // TODO: We should try to log the messages, but we can't intercept the SendAsync call since it's an
            // extension method, so we can't see what is being sent. One suggestion is to wrap the static extension
            // call in another method/interface, which we could then mock

            // TODO: We should log these messages somewhere (MessageStore?)
            ////mockClientProxy
            ////    .Setup(clientProxy => clientProxy.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            ////    .Returns(Task.CompletedTask);

            mockHubClients
                .Setup(groupManager => groupManager.Group(It.IsAny<string>()))
                .Returns(mockClientProxy.Object);

            mockHubContext
                .Setup(hubContext => hubContext.Clients)
                .Returns(mockHubClients.Object);

            return mockHubContext.Object;
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

        private static IGuildTextChannel CreateGuildTextChannel(
            MessageStore messageStore,
            IGuild guild,
            ulong messageChannelId,
            Action<Mock<IGuildTextChannel>> updateMock = null)
        {
            Mock<IGuildTextChannel> mockMessageChannel =
                CreateMockGuildTextChannel(messageStore, messageChannelId, updateMock);

            mockMessageChannel
                .Setup(channel => channel.Guild)
                .Returns(guild);

            // Okay to invoke twice?
            updateMock?.Invoke(mockMessageChannel);
            return mockMessageChannel.Object;
        }

        private static Mock<IGuildTextChannel> CreateMockGuildTextChannel(
            MessageStore messageStore,
            ulong messageChannelId,
            Action<Mock<IGuildTextChannel>> updateMock = null)
        {
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

            updateMock?.Invoke(mockMessageChannel);
            return mockMessageChannel;
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
