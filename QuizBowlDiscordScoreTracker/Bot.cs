using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace QuizBowlDiscordScoreTracker
{
    public class Bot : IDisposable
    {
        private readonly GameState gameState;
        private readonly DiscordClient discordClient;
        private readonly CommandsNextModule commandsModule;

        public Bot(string accessToken)
        {
            this.gameState = new GameState();

            this.discordClient = new DiscordClient(new DiscordConfiguration()
            {
                Token = accessToken,
                TokenType = TokenType.Bot,
                UseInternalLogHandler = true,
                LogLevel = LogLevel.Debug
            });

            DependencyCollectionBuilder dependencyCollectionBuilder = new DependencyCollectionBuilder();
            dependencyCollectionBuilder.AddInstance(this.gameState);
            this.commandsModule = this.discordClient.UseCommandsNext(new CommandsNextConfiguration()
            {
                StringPrefix = "!",
                CaseSensitive = false,
                EnableDms = false,
                Dependencies = dependencyCollectionBuilder.Build(),,
            });

            this.commandsModule.RegisterCommands<BotCommands>();

            this.discordClient.MessageCreated += this.MessageReceived;
        }

        public Task ConnectAsync()
        {
            return this.discordClient.ConnectAsync();
        }

        public void Dispose()
        {
            if (this.discordClient != null)
            {
                this.discordClient.MessageCreated -= this.MessageReceived;
                this.discordClient.Dispose();
            }
        }

        private async Task MessageReceived(MessageCreateEventArgs args)
        {
            // Accepted non-commands:
            // From the reader: -5, 0, 10, 15, no penalty
            args.Message.
        }
    }
}
