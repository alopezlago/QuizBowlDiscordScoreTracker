using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using QuizBowlDiscordScoreTracker.Database;
using Serilog;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public class ReaderCommandHandler
    {
        private static readonly ILogger Logger = Log.ForContext(typeof(ReaderCommandHandler));

        public ReaderCommandHandler(
            ICommandContext context, GameStateManager manager, IDatabaseActionFactory dbActionFactory)
        {
            this.Context = context;
            this.Manager = manager;
            this.DatabaseActionFactory = dbActionFactory;
        }

        private ICommandContext Context { get; }

        private GameStateManager Manager { get; }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

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

        public async Task NextAsync()
        {
            if (!this.Manager.TryGet(this.Context.Channel.Id, out GameState game))
            {
                return;
            }

            game.NextQuestion();

            // TODO: Consider having an event handler in GameState that will trigger when the phase is changed, so we
            // can avoid duplicating this code
            await this.Context.Channel.SendMessageAsync($"**TU {game.PhaseNumber}**");
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
                ulong? teamId = await user.GetTeamId(this.DatabaseActionFactory);

                // Need to remove player from queue too, since they cannot answer
                // Maybe we need to find the next player in the queue?
                game.WithdrawPlayer(userId, teamId);
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
                    teamId = await user.GetTeamId(this.DatabaseActionFactory);
                    game.WithdrawPlayer(nextPlayerId, teamId);
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
