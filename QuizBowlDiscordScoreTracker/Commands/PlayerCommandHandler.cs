using System.Threading.Tasks;
using Discord;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.Web;
using Serilog;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public class PlayerCommandHandler
    {
        private static readonly ILogger Logger = Log.ForContext<PlayerCommandHandler>();

        public PlayerCommandHandler(
            IInteractionContext context,
            GameStateManager manager,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory,
            IHubContext<MonitorHub> hubContext)
        {
            this.Context = context;
            this.Manager = manager;
            this.Options = options;
            this.DatabaseActionFactory = dbActionFactory;
            this.HubContext = hubContext;
        }

        private IInteractionContext Context { get; }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        private IHubContext<MonitorHub> HubContext { get; }

        private GameStateManager Manager { get; }

        private IOptionsMonitor<BotConfiguration> Options { get; }

        public async Task Buzz()
        {
            if (!(this.Context.User is IGuildUser guildUser))
            {
                await this.Context.Interaction.RespondOrFollowupAsync("This command requires a server member.", ephemeral: true);
                return;
            }
            
            if (!(this.Context.Channel is ITextChannel channel))
            {
                await this.Context.Interaction.RespondOrFollowupAsync("This command requires a text channel.", ephemeral: true);
                return;
            }
            
            if (!this.Manager.TryGet(this.Context.Channel.Id, out GameState state))
            {
                await this.Context.Interaction.RespondOrFollowupAsync("No game is running in this channel.", ephemeral: true);
                return;
            }

            string playerDisplayName = guildUser.Nickname ?? guildUser.Username;
            bool playerAdded = await state.AddPlayer(guildUser.Id, playerDisplayName);

            if (!(playerAdded && state.TryGetNextPlayer(out ulong nextPlayerId)))
            {
                await this.Context.Interaction.RespondOrFollowupAsync(
                    "You've already buzzed in or someone buzzed before you", ephemeral: true);
                return;
            }
            
            if (nextPlayerId == guildUser.Id)
            {
                await PromptHandler.PromptNextPlayerAsync(
                    this.Context, state, this.Options, this.DatabaseActionFactory, this.HubContext);
                return;
            }
            else
            {
                await this.Context.Interaction.RespondOrFollowupAsync("You've been added to the buzz queue", ephemeral: true);
                return;
            }
        }

        public async Task Withdraw()
        {
            if (!(this.Context.User is IGuildUser guildUser))
            {
                await this.Context.Interaction.RespondOrFollowupAsync("This command requires a server member.", ephemeral: true);
                return;
            }

            if (!(this.Context.Channel is ITextChannel channel))
            {
                await this.Context.Interaction.RespondOrFollowupAsync("This command requires a text channel.", ephemeral: true);
                return;
            }

            if (!this.Manager.TryGet(this.Context.Channel.Id, out GameState state))
            {
                await this.Context.Interaction.RespondOrFollowupAsync("No game is running in this channel.", ephemeral: true);
                return;
            }
            
            // See if the player has withdrawn. We want to check if the author is at the top of the queue to see if we
            // want to send a message about the withdrawl
            if (!(state.TryGetNextPlayer(out ulong nextPlayerId) && await state.WithdrawPlayer(guildUser.Id)))
            {
                await this.Context.Interaction.RespondOrFollowupAsync("You are not in the buzz queue.", ephemeral: true);
                return;
            }

            if (nextPlayerId != guildUser.Id)
            {
                await this.Context.Interaction.RespondOrFollowupAsync("You have been withdrawn", ephemeral: true);
                return;
            }

            if (state.TryGetNextPlayer(out _))
            {
                // There's another player, so prompt them
                await PromptHandler.PromptNextPlayerAsync(
                    this.Context, state, this.Options, this.DatabaseActionFactory, this.HubContext);
            }
            else
            {
                // If there are no players in the queue, have the bot recognize the withdrawl
                string teamId = await state.TeamManager.GetTeamIdOrNull(guildUser.Id);
                string teamNameReference = await GetTeamNameForMessage(state, teamId);
                await PromptHandler.PromptNextPlayerAsync(
                    this.Context, state, this.Options, this.DatabaseActionFactory, this.HubContext);
                await this.Context.Interaction.RespondOrFollowupAsync($"{guildUser.Mention}{teamNameReference} has withdrawn.");
                return;
            }

        }

        private static async Task<string> GetTeamNameForMessage(GameState state, string teamId)
        {
            return teamId != null && (await state.TeamManager.GetTeamIdToNames()).TryGetValue(teamId, out string teamName) ?
                $" ({teamName.Trim()})" :
                string.Empty;
        }
    }
}
