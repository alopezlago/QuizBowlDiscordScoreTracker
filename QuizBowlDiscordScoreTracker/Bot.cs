using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace QuizBowlDiscordScoreTracker
{
    public class Bot : IDisposable
    {
        private static readonly Regex BuzzRegex = new Regex("^bu?z+$");

        private readonly GameState state;
        private readonly DiscordClient discordClient;
        private readonly CommandsNextModule commandsModule;

        public Bot(string accessToken)
        {
            this.state = new GameState();

            this.discordClient = new DiscordClient(new DiscordConfiguration()
            {
                Token = accessToken,
                TokenType = TokenType.Bot,
                UseInternalLogHandler = true,
                LogLevel = LogLevel.Debug
            });

            DependencyCollectionBuilder dependencyCollectionBuilder = new DependencyCollectionBuilder();
            dependencyCollectionBuilder.AddInstance(this.state);
            this.commandsModule = this.discordClient.UseCommandsNext(new CommandsNextConfiguration()
            {
                StringPrefix = "!",
                CaseSensitive = false,
                EnableDms = false,
                Dependencies = dependencyCollectionBuilder.Build()
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
            if (args.Author == this.discordClient.CurrentUser)
            {
                return;
            }

            // Accepted non-commands:
            // From the reader: -5, 0, 10, 15, no penalty
            // From others: buzzes only
            string message = args.Message.Content.Trim();
            if (message.StartsWith('!'))
            {
                // Skip commands
                return;
            }

            if (this.state.Reader == args.Author)
            {
                switch (args.Message.Content)
                {
                    case "-5":
                    case "0":
                    case "10":
                    case "15":
                    case "20":
                        this.state.ScorePlayer(int.Parse(message));
                        await this.PromptNextPlayer(args.Message);
                        break;
                    case "no penalty":
                        this.state.ScorePlayer(0);
                        await PromptNextPlayer(args.Message);
                        break;
                    default:
                        break;
                }

                return;
            }
            
            if (BuzzRegex.IsMatch(message))
            {
                if (this.state.AddPlayer(args.Message.Author) &&
                    this.state.TryGetNextPlayer(out DiscordUser nextPlayer) &&
                    nextPlayer == args.Message.Author)
                {
                    await this.PromptNextPlayer(args.Message);
                }
            }
        }

        private async Task PromptNextPlayer(DiscordMessage message)
        {
            if (this.state.TryGetNextPlayer(out DiscordUser user))
            {
                await message.RespondAsync(user.Mention);
            }
        }
    }
}
