using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;
using Serilog;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public class GeneralCommandHandler
    {
        private static readonly ILogger Logger = Log.ForContext(typeof(GeneralCommandHandler));

        public GeneralCommandHandler(
            ICommandContext context,
            GameStateManager manager,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory)
        {
            this.Context = context;
            this.DatabaseActionFactory = dbActionFactory;
            this.Manager = manager;
            this.Options = options;
        }

        private ICommandContext Context { get; }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        private GameStateManager Manager { get; }

        private IOptionsMonitor<BotConfiguration> Options { get; }

        public async Task SetReaderAsync()
        {
            IGuildUser user = await this.Context.Guild.GetUserAsync(this.Context.User.Id);
            if (user == null)
            {
                // If the reader doesn't exist anymore, don't start a game.
                return;
            }

            if (!(this.Manager.TryGet(this.Context.Channel.Id, out GameState state) ||
                this.Manager.TryCreate(this.Context.Channel.Id, out state)))
            {
                // Couldn't add a new reader.
                return;
            }
            else if (state.ReaderId != null)
            {
                // We already have a reader, so do nothing.
                return;
            }

            state.ReaderId = this.Context.User.Id;

            if (this.Context.Channel is IGuildChannel guildChannel)
            {
                Logger.Information(
                     "Game started in guild '{0}' in channel '{1}'", guildChannel.Guild.Name, guildChannel.Name);
            }

            // Prevent a cold start on the first buzz, and eagerly get the team prefix and channel pair
            Task[] dbTasks = new Task[2];
            using (DatabaseAction action = this.DatabaseActionFactory.Create())
            {
                dbTasks[0] = action.GetPairedVoiceChannelIdOrNullAsync(this.Context.Channel.Id);
                dbTasks[1] = action.GetTeamRolePrefixAsync(this.Context.Guild.Id);
            }

            await Task.WhenAll(dbTasks);

            string message = this.Options.CurrentValue.WebBaseURL == null ?
                $"{this.Context.User.Mention} is the reader." :
                $"{this.Context.User.Mention} is the reader. Please visit {this.Options.CurrentValue.WebBaseURL}?{this.Context.Channel.Id} to hear buzzes.";
            await this.Context.Channel.SendMessageAsync(message);
        }

        public async Task GetScoreAsync()
        {
            if (!this.Manager.TryGet(this.Context.Channel.Id, out GameState currentGame) ||
                currentGame?.ReaderId == null ||
                !(this.Context.Channel is IGuildChannel guildChannel))
            {
                return;
            }

            IGuildUser guildBotUser = await this.Context.Guild.GetCurrentUserAsync();
            ChannelPermissions channelPermissions = guildBotUser.GetPermissions(guildChannel);
            if (!channelPermissions.EmbedLinks)
            {
                await this.Context.Channel.SendMessageAsync(
                    "This bot must have \"Embed Links\" permissions to show the score");
                return;
            }

            IEnumerable<IGrouping<ulong, ScoreAction>> scoreActions = currentGame.GetScoringActions();
            IEnumerable<IGrouping<ulong, ScoreAction>> topScores = scoreActions.Take(GameState.ScoresListLimit);

            bool hasSuperpowers = false;
            bool hasPowers = false;
            foreach (IGrouping<ulong, ScoreAction> scoreAction in scoreActions)
            {
                if (!hasSuperpowers && scoreAction.Any(action => action.Score == 20))
                {
                    // super-powers implies there are powers.
                    hasSuperpowers = true;
                    hasPowers = true;
                    break;
                }

                if (!hasPowers && scoreAction.Any(action => action.Score == 15))
                {
                    hasPowers = true;
                }
            }

            // We could have more than the embed limit, so split them up if necessary
            int embedsSentCount = await this.Context.Channel.SendAllEmbeds(
                topScores,
                () =>
                {
                    return new EmbedBuilder
                    {
                        Title = scoreActions.Take(checked(GameState.ScoresListLimit + 1)).Count() > GameState.ScoresListLimit ?
                            $"Top {GameState.ScoresListLimit} Scores" :
                            "Scores",
                        Color = Color.Gold
                    };
                },
                (scoringGroup, index) =>
                {
                    StringBuilder valueBuilder = new StringBuilder();
                    string name = scoringGroup.LastOrDefault()?.Buzz.PlayerDisplayName ?? "<Unknown>";

                    // TODO: Give the top 3 by score the following: 🥇 🥈 🥉
                    // We need to take ties into account, which means tracking who 1st-3rd is manually
                    int negs = 0;
                    int noPenalties = 0;
                    int gets = 0;
                    int powers = 0;
                    int superPowers = 0;
                    foreach (ScoreAction action in scoringGroup)
                    {
                        switch (action.Score)
                        {
                            case -5:
                                negs++;
                                break;
                            case 0:
                                noPenalties++;
                                break;
                            case 10:
                                gets++;
                                break;
                            case 15:
                                powers++;
                                break;
                            case 20:
                                superPowers++;
                                break;
                            default:
                                Logger.Warning($"Unknown point value found computing score: {action.Score}");
                                break;
                        }
                    }

                    int totalPoints = scoringGroup.Sum(action => action.Score);
                    valueBuilder.Append("**");
                    valueBuilder.Append(totalPoints);
                    valueBuilder.Append("** (");
                    if (hasSuperpowers)
                    {
                        valueBuilder.Append(superPowers);
                        valueBuilder.Append('/');
                    }

                    if (hasPowers)
                    {
                        valueBuilder.Append(powers);
                        valueBuilder.Append('/');
                    }

                    valueBuilder.Append($"{gets}/{negs})");
                    if (noPenalties > 0)
                    {
                        valueBuilder.Append($" ({noPenalties} no penalty buzz{(noPenalties != 1 ? "es" : "")})");
                    }

                    return new EmbedFieldBuilder()
                    {
                        Name = name,
                        Value = valueBuilder.ToString()
                    };
                });

            if (embedsSentCount == 0)
            {
                await this.Context.Channel.SendMessageAsync("No one has scored yet");
            }
        }
    }
}
