using System;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using QuizBowlDiscordScoreTracker.TeamManager;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class RequirePlayerAttribute : PreconditionAttribute
    {
        public override async Task<PreconditionResult> CheckRequirementsAsync(
            IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
        {
            GameStateManager manager = services.GetService<GameStateManager>();
            if (!manager.TryGet(context.Channel.Id, out GameState state))
            {
                return PreconditionResult.FromError("No existing game");
            }
            else if (context.User.Id == state.ReaderId || context.User.Id == context.Client.CurrentUser.Id)
            {
                return PreconditionResult.FromError("Reader or bot cannot be a player");
            }

            if (await state.TeamManager.GetTeamIdOrNull(context.User.Id) != null)
            {
                return PreconditionResult.FromSuccess();
            }

            if (state.TeamManager is SoloOnlyTeamManager ||
                (state.TeamManager is ISelfManagedTeamManager &&
                    (await state.TeamManager.GetTeamIdToNames()).Count == 0))
            {
                return PreconditionResult.FromSuccess();
            }

            return PreconditionResult.FromError("Join a team before using this command.");
        }
    }
}
