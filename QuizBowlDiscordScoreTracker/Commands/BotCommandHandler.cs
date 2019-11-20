using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public class BotCommandHandler
    {
        private readonly ICommandContext context;
        private readonly GameStateManager manager;
        private readonly GameState currentGame;

        public BotCommandHandler(ICommandContext context, GameStateManager manager, GameState currentGame)
        {
            this.context = context;
            this.manager = manager;
            this.currentGame = currentGame;
        }

        public async Task SetReader()
        {
            IGuildUser user = await this.context.Guild.GetUserAsync(this.context.User.Id);
            if (user == null)
            {
                // If the reader doesn't exist anymore, don't start a game.
                return;
            }

            GameState state = this.currentGame;
            if (state == null && !this.manager.TryCreate(this.context.Channel.Id, out state))
            {
                // Couldn't add a new reader.
                return;
            }
            else if (state.ReaderId != null)
            {
                // We already have a reader, so do nothing.
                return;
            }

            state.ReaderId = context.User.Id;
            await this.context.Channel.SendMessageAsync($"{this.context.User.Mention} is the reader.");
        }

        public async Task SetNewReader(ulong newReaderId)
        {
            IGuildUser newReader = await this.context.Guild.GetUserAsync(newReaderId);
            if (newReader != null)
            {
                this.currentGame.ReaderId = newReaderId;
                await this.context.Channel.SendMessageAsync($"{newReader.Mention} is now the reader.");
                return;
            }

            await this.context.Channel.SendMessageAsync($"User could not be found. Could not set the new reader.");
        }

        public Task Clear()
        {
            if (this.currentGame != null)
            {
                this.currentGame.ClearCurrentRound();
            }

            return Task.CompletedTask;
        }

        public async Task ClearAll()
        {
            if (this.currentGame != null && this.manager.TryRemove(this.context.Channel.Id))
            {
                currentGame.ClearAll();
                await this.context.Channel.SendMessageAsync($"Reading over. All stats cleared.");
            }
        }

        public async Task GetScore()
        {
            if (this.currentGame?.ReaderId != null)
            {
                IEnumerable<KeyValuePair<ulong, int>> scores = currentGame.GetScores();

                EmbedBuilder builder = new EmbedBuilder();
                builder.Title = scores.Take(checked(GameState.ScoresListLimit + 1)).Count() > GameState.ScoresListLimit ?
                    $"Top {GameState.ScoresListLimit} Scores" :
                    "Scores";
                builder.WithColor(Color.Gold);
                foreach (KeyValuePair<ulong, int> score in scores.Take(GameState.ScoresListLimit))
                {
                    // TODO: Look into moving away from using await in the foreach loop. Maybe use AsyncEnumerable
                    // and do 2-3 lookups at once? The problem is we need the values added in order.
                    IGuildUser user = await this.context.Guild.GetUserAsync(score.Key);
                    string name = user == null ? "<Unknown>" : user.Nickname ?? user.Username;
                    builder.AddField(name, score.Value.ToString(CultureInfo.InvariantCulture));
                }

                Embed embed = builder.Build();
                await this.context.Channel.SendMessageAsync(embed: embed);
            }
        }

        public Task NextQuestion()
        {
            if (this.currentGame != null)
            {
                this.currentGame.NextQuestion();
            }

            return Task.CompletedTask;
        }

        public async Task Undo()
        {
            if (this.currentGame != null && this.currentGame.Undo(out ulong userId))
            {
                IGuildUser user = await this.context.Guild.GetUserAsync(userId);
                string name;
                string message;
                if (user == null)
                {
                    // TODO: Need to test this case
                    // Also unsure if this is really applicable. Could use status, but some people may play while
                    // appearing offline.
                    name = "<Unknown>";

                    // Need to remove player from queue too, since they cannot answer
                    // Maybe we need to find the next player in the queue?
                    this.currentGame.WithdrawPlayer(userId);
                    string nextPlayerMention = null;
                    while (this.currentGame.TryGetNextPlayer(out ulong nextPlayerId))
                    {
                        IGuildUser nextPlayerUser = await this.context.Guild.GetUserAsync(userId);
                        if (nextPlayerUser != null)
                        {
                            nextPlayerMention = nextPlayerUser.Mention;
                            break;
                        }

                        // Player isn't here, so withdraw them
                        this.currentGame.WithdrawPlayer(nextPlayerId);
                    }

                    message = nextPlayerMention != null ?
                        $"Undid scoring for {name}. {nextPlayerMention}. your answer?" :
                        $"Undid scoring for {name}.";
                }
                else
                {
                    name = user.Nickname ?? user.Username;
                    message = $"Undid scoring for {name}. {user.Mention}, your answer?";
                }

                await this.context.Channel.SendMessageAsync(message);
            }
        }
    }
}
