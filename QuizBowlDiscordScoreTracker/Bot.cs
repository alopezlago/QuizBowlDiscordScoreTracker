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
        private static readonly Regex BuzzRegex = new Regex("^bu?z+$", RegexOptions.IgnoreCase);

        // TODO: Rewrite this so that we have a dictionary of game states mapped to the channel the bot is reading in.
        private readonly GameState state;
        private readonly ConfigOptions options;
        private readonly DiscordClient discordClient;
        private readonly CommandsNextModule commandsModule;

        private bool? readerRejoined;
        private object readerRejoinedLock = new object();

        public Bot(ConfigOptions options)
        {
            this.state = new GameState();
            this.options = options;

            this.discordClient = new DiscordClient(new DiscordConfiguration()
            {
                Token = options.BotToken,
                TokenType = TokenType.Bot,
                UseInternalLogHandler = true,
                LogLevel = LogLevel.Debug
            });

            DependencyCollectionBuilder dependencyCollectionBuilder = new DependencyCollectionBuilder();
            dependencyCollectionBuilder.AddInstance(this.state);
            dependencyCollectionBuilder.AddInstance(options);
            this.commandsModule = this.discordClient.UseCommandsNext(new CommandsNextConfiguration()
            {
                StringPrefix = "!",
                CaseSensitive = false,
                EnableDms = false,
                Dependencies = dependencyCollectionBuilder.Build()
            });

            this.commandsModule.RegisterCommands<BotCommands>();

            this.readerRejoined = null;

            this.discordClient.MessageCreated += this.OnMessageCreated;

            // TODO: We should make sure that, if the reader disconnects, we can reset the game or pick a new reader.
            this.discordClient.PresenceUpdated += this.OnPresenceUpdated;
        }

        public Task ConnectAsync()
        {
            return this.discordClient.ConnectAsync();
        }

        public void Dispose()
        {
            if (this.discordClient != null)
            {
                this.discordClient.MessageCreated -= this.OnMessageCreated;
                this.discordClient.PresenceUpdated -= this.OnPresenceUpdated;
                this.discordClient.Dispose();
            }
        }

        private async Task OnMessageCreated(MessageCreateEventArgs args)
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

        private Task OnPresenceUpdated(PresenceUpdateEventArgs args)
        {
            if (this.state.Reader == args.Member.Presence?.User)
            {
                lock (this.readerRejoinedLock)
                {
                    if (this.readerRejoined == null && args.Member.Presence.Status == UserStatus.Offline)
                    {
                        this.readerRejoined = false;
                    }
                    else if (this.readerRejoined == false && args.Member.Presence.Status != UserStatus.Offline)
                    {
                        this.readerRejoined = true;
                        return Task.CompletedTask;
                    }
                    else
                    {
                        return Task.CompletedTask;
                    }
                }

                // The if-statement is structured so that we can call Task.Delay later without holding onto the lock
                // We should only be here if the first condition was true
                Task t = new Task(async () =>
                {
                    await Task.Delay(this.options.WaitForRejoinMs);
                    bool reset = false;
                    lock (this.readerRejoinedLock)
                    {
                        reset = this.readerRejoined == false;
                        this.readerRejoined = null;
                    }

                    if (reset)
                    {
                        await this.state.Channel?.SendMessageAsync(
                            $"Reader {args.Member.Mention} has left. Ending the game.");
                        this.state.ClearAll();
                    }
                });

                t.Start();
                // This is a lie, but await seems to block the event handlers from receiving other events, so say that
                // we have completed.
                return Task.CompletedTask;
            }

            return Task.CompletedTask;
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
