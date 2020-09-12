using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Serilog;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public class ReaderCommandHandler
    {
        private static readonly ILogger Logger = Log.ForContext(typeof(ReaderCommandHandler));

        public ReaderCommandHandler(ICommandContext context, GameStateManager manager)
        {
            this.Context = context;
            this.Manager = manager;
        }

        private ICommandContext Context { get; }

        private GameStateManager Manager { get; }

        public async Task SetNewReaderAsync(IGuildUser newReader)
        {
            if (newReader != null && this.Manager.TryGet(this.Context.Channel.Id, out GameState game))
            {
                game.ReaderId = newReader.Id;
                await this.Context.Channel.SendMessageAsync($"{newReader.Mention} is now the reader.");
                return;
            }

            if (this.Context.Channel is IGuildChannel guildChannel)
            {
                Logger.Information(
                    "New reader called in guild '{0}' in channel '{1}' with ID that could not be found: {2}",
                    guildChannel.Guild.Name,
                    guildChannel.Name,
                    newReader?.Id);
            }

            await this.Context.Channel.SendMessageAsync($"User could not be found. Could not set the new reader.");
        }

        public Task ClearAllAsync()
        {
            if (!(this.Manager.TryGet(this.Context.Channel.Id, out GameState game) &&
                this.Manager.TryRemove(this.Context.Channel.Id)))
            {
                return Task.CompletedTask;
            }

            game.ClearAll();

            if (this.Context.Channel is IGuildChannel guildChannel)
            {
                Logger.Information(
                    "Game ended in guild '{0}' in channel '{0}'", guildChannel.Guild.Name, guildChannel.Name);
            }

            return this.Context.Channel.SendMessageAsync($"Reading over. All stats cleared.");
        }

        public Task ClearAsync()
        {
            if (this.Manager.TryGet(this.Context.Channel.Id, out GameState game))
            {
                game.ClearCurrentRound();
            }

            return Task.CompletedTask;
        }

        public Task NextAsync()
        {
            if (this.Manager.TryGet(this.Context.Channel.Id, out GameState game))
            {
                game.NextQuestion();
            }

            return Task.CompletedTask;
        }

        public async Task UndoAsync()
        {
            if (!(this.Manager.TryGet(this.Context.Channel.Id, out GameState game) && game.Undo(out ulong userId)))
            {
                return;
            }

            IGuildUser user = await this.Context.Guild.GetUserAsync(userId);
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
                game.WithdrawPlayer(userId);
                string nextPlayerMention = null;
                while (game.TryGetNextPlayer(out ulong nextPlayerId))
                {
                    IGuildUser nextPlayerUser = await this.Context.Guild.GetUserAsync(userId);
                    if (nextPlayerUser != null)
                    {
                        nextPlayerMention = nextPlayerUser.Mention;
                        break;
                    }

                    // Player isn't here, so withdraw them
                    game.WithdrawPlayer(nextPlayerId);
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

            await this.Context.Channel.SendMessageAsync(message);
        }
    }
}
