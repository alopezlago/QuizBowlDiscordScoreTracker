using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        // TODO: We may need a lock for this, and this lock would need to be accessible form BotCommands. We could wrap
        // this in an object which would do the locking for us.
        private readonly Dictionary<DiscordChannel, GameState> games;
        private readonly ConfigOptions options;
        private readonly DiscordClient discordClient;
        private readonly CommandsNextModule commandsModule;
        private readonly Regex[] buzzEmojisRegex;

        private Dictionary<DiscordUser, bool> readerRejoinedMap;
        private object readerRejoinedMapLock = new object();

        public Bot(ConfigOptions options)
        {
            this.games = new Dictionary<DiscordChannel, GameState>();
            this.options = options;

            this.discordClient = new DiscordClient(new DiscordConfiguration()
            {
                Token = options.BotToken,
                TokenType = TokenType.Bot,
                UseInternalLogHandler = true,
                LogLevel = LogLevel.Debug
            });

            DependencyCollectionBuilder dependencyCollectionBuilder = new DependencyCollectionBuilder();
            dependencyCollectionBuilder.AddInstance(this.games);
            dependencyCollectionBuilder.AddInstance(options);
            this.commandsModule = this.discordClient.UseCommandsNext(new CommandsNextConfiguration()
            {
                StringPrefix = "!",
                CaseSensitive = false,
                EnableDms = false,
                Dependencies = dependencyCollectionBuilder.Build()
            });

            this.commandsModule.RegisterCommands<BotCommands>();

            this.buzzEmojisRegex = BuildBuzzEmojiRegexes(this.options);

            this.readerRejoinedMap = new Dictionary<DiscordUser, bool>();

            this.discordClient.MessageCreated += this.OnMessageCreated;
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

        private static Regex[] BuildBuzzEmojiRegexes(ConfigOptions options)
        {
            if (options.BuzzEmojis == null)
            {
                return new Regex[0];
            }

            Regex[] result = new Regex[options.BuzzEmojis.Length];
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < options.BuzzEmojis.Length; i++)
            {
                string buzzEmoji = options.BuzzEmojis[i];
                builder.Append("^<");
                builder.Append(buzzEmoji);
                builder.Append("\\d+>$");
                Regex regex = new Regex(builder.ToString());
                result[i] = regex;
                builder.Clear();
            }

            return result;
        }

        private static async Task PromptNextPlayer(GameState state, DiscordMessage message)
        {
            if (state.TryGetNextPlayer(out ulong userId))
            {
                DiscordUser user = await message.Channel.Guild.GetMemberAsync(userId);
                await message.RespondAsync(user.Mention);
            }
        }

        private async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (args.Author == this.discordClient.CurrentUser)
            {
                return;
            }

            string message = args.Message.Content.Trim();
            if (message.StartsWith('!'))
            {
                // Skip commands
                return;
            }

            if (!this.games.TryGetValue(args.Channel, out GameState state))
            {
                return;
            }

            if (state.ReaderId == args.Author.Id)
            {
                switch (args.Message.Content)
                {
                    case "-5":
                    case "0":
                    case "10":
                    case "15":
                    case "20":
                        state.ScorePlayer(int.Parse(message));
                        await PromptNextPlayer(state, args.Message);
                        break;
                    case "no penalty":
                        state.ScorePlayer(0);
                        await PromptNextPlayer(state, args.Message);
                        break;
                    default:
                        break;
                }

                return;
            }

            bool hasPlayerBuzzedIn = this.IsBuzz(message) && state.AddPlayer(args.Message.Author.Id);
            if (hasPlayerBuzzedIn ||
                (message.Equals("wd", StringComparison.CurrentCultureIgnoreCase) && state.WithdrawPlayer(args.Message.Author.Id)))
            {
                if (state.TryGetNextPlayer(out ulong nextPlayerId) && nextPlayerId == args.Message.Author.Id)
                {
                    await PromptNextPlayer(state, args.Message);
                }
            }
        }

        private Task OnPresenceUpdated(PresenceUpdateEventArgs args)
        {
            DiscordUser user = args.Member?.Presence?.User;
            if (user == null)
            {
                // Can't do anything, we don't know what game they were reading.
                return Task.CompletedTask;
            }

            KeyValuePair<DiscordChannel, GameState>[] readingGames = this.games
                .Where(kvp => kvp.Value.ReaderId == user.Id)
                .ToArray();

            if (readingGames.Length > 0)
            {
                lock (this.readerRejoinedMapLock)
                {
                    if (!this.readerRejoinedMap.TryGetValue(user, out bool hasRejoined) &&
                        args.Member.Presence.Status == UserStatus.Offline)
                    {
                        this.readerRejoinedMap[user] = false;
                    }
                    else if (hasRejoined == false && args.Member.Presence.Status != UserStatus.Offline)
                    {
                        this.readerRejoinedMap[user] = true;
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
                    bool rejoined = false;
                    lock (this.readerRejoinedMapLock)
                    {
                        this.readerRejoinedMap.TryGetValue(user, out rejoined);
                        this.readerRejoinedMap.Remove(user);
                    }

                    if (!rejoined)
                    {
                        Task<DiscordMessage>[] sendResetTasks = new Task<DiscordMessage>[readingGames.Length];
                        for (int i = 0; i < readingGames.Length; i++)
                        {
                            KeyValuePair<DiscordChannel, GameState> pair = readingGames[i];
                            pair.Value.ClearAll();
                            sendResetTasks[i] = pair.Key.SendMessageAsync(
                                $"Reader {args.Member.Mention} has left. Ending the game.");
                        }

                        await Task.WhenAll(sendResetTasks);
                    }
                });

                t.Start();
                // This is a lie, but await seems to block the event handlers from receiving other events, so say that
                // we have completed.
                return Task.CompletedTask;
            }

            return Task.CompletedTask;
        }

        private bool IsBuzz(string buzzText)
        {
            return BuzzRegex.IsMatch(buzzText) || this.buzzEmojisRegex.Any(regex => regex.IsMatch(buzzText));
        }
    }
}
