using Discord;
using Discord.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using QuizBowlDiscordScoreTracker;
using QuizBowlDiscordScoreTracker.Commands;
using System;
using System.Threading.Tasks;

namespace QuizBowlDiscordScoreTrackerUnitTests
{
    [TestClass]
    public class RequireReaderAttributeTests
    {
        private const ulong DefaultChannelId = 2;
        private const ulong AdminId = 3;
        private const ulong ReaderId = 4;
        private const ulong UnprivilegedId = 5;
        private const ulong OwnerId = 6;

        [TestMethod]
        public async Task AcceptAdmin()
        {
            await TestUser(AdminId, true);
        }

        [TestMethod]
        public async Task AcceptReader()
        {
            await TestUser(ReaderId, true);
        }

        [TestMethod]
        public async Task AcceptOwner()
        {
            await TestUser(OwnerId, true);
        }

        [TestMethod]
        public async Task RejectIfNoGameRunning()
        {
            RequireReaderAttribute attribute = new RequireReaderAttribute();
            ICommandContext context = CreateCommandContext(DefaultChannelId, AdminId);

            GameStateManager gameStateManager = new GameStateManager();
            IServiceProvider serviceProvider = CreateServiceProvider(gameStateManager);

            PreconditionResult result = await attribute.CheckPermissionsAsync(context, null, serviceProvider);
            Assert.IsFalse(result.IsSuccess, "Check should have failed.");
        }

        [TestMethod]
        public async Task RejectUnprivileged()
        {
            await TestUser(UnprivilegedId, false);
        }

        [TestMethod]
        public async Task RejectIfWrongChannel()
        {
            RequireReaderAttribute attribute = new RequireReaderAttribute();
            ICommandContext context = CreateCommandContext(DefaultChannelId + 1, AdminId);

            GameStateManager gameStateManager = new GameStateManager();
            gameStateManager.TryCreate(DefaultChannelId, out GameState gameState);
            gameState.ReaderId = ReaderId;
            IServiceProvider serviceProvider = CreateServiceProvider(gameStateManager);

            PreconditionResult result = await attribute.CheckPermissionsAsync(context, null, serviceProvider);
            Assert.IsFalse(result.IsSuccess, "Check should have failed.");
        }

        private async Task TestUser(ulong userId, bool acceptanceExpected)
        {
            RequireReaderAttribute attribute = new RequireReaderAttribute();
            ICommandContext context = CreateCommandContext(DefaultChannelId, userId);

            GameStateManager gameStateManager = new GameStateManager();
            gameStateManager.TryCreate(DefaultChannelId, out GameState gameState);
            gameState.ReaderId = ReaderId;
            IServiceProvider serviceProvider = CreateServiceProvider(gameStateManager);

            PreconditionResult result = await attribute.CheckPermissionsAsync(context, null, serviceProvider);
            if (acceptanceExpected)
            {
                Assert.IsTrue(result.IsSuccess, "User should have been accepted as a reader.");
            }
            else
            {
                Assert.IsFalse(result.IsSuccess, "User shouldn't have been accepted as a reader.");
            }
        }

        private static IServiceProvider CreateServiceProvider(GameStateManager gameStateManager)
        {
            IServiceCollection serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton(gameStateManager);
            return serviceCollection.BuildServiceProvider();
        }

        private static ICommandContext CreateCommandContext(ulong channelId, ulong userId)
        {
            Mock<IMessageChannel> mockChannel = new Mock<IMessageChannel>();
            mockChannel
                .Setup(channel => channel.Id)
                .Returns(channelId);

            Mock<IGuildUser> mockUser = new Mock<IGuildUser>();
            mockUser
                .Setup(user => user.Id)
                .Returns(userId);
            mockUser
                .Setup(user => user.GuildPermissions)
                .Returns(userId == AdminId ? new GuildPermissions(administrator: true) : GuildPermissions.None);

            Mock<IGuild> mockGuild = new Mock<IGuild>();
            mockGuild
                .Setup(guild => guild.OwnerId)
                .Returns(OwnerId);

            Mock<ICommandContext> mockContext = new Mock<ICommandContext>();
            mockContext
                .Setup(context => context.Channel)
                .Returns(mockChannel.Object);
            mockContext
                .Setup(context => context.User)
                .Returns(mockUser.Object);
            mockContext
                .Setup(context => context.Guild)
                .Returns(mockGuild.Object);
            return mockContext.Object;
        }
    }
}
