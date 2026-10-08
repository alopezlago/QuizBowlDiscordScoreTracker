using System;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class RequireReaderAttribute : PreconditionAttribute
    {
        public override Task<PreconditionResult> CheckRequirementsAsync(
            IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
        {
            GameStateManager manager = services.GetService<GameStateManager>();
            if (!manager.TryGet(context.Channel.Id, out GameState state))
            {
                return Task.FromResult(PreconditionResult.FromError("No existing game"));
            }

            if (context.User.Id == state.ReaderId ||
                (context.User is IGuildUser guildUser &&
                    (guildUser.GuildPermissions.Administrator || context.Guild.OwnerId == guildUser.Id)))
            {
                return Task.FromResult(PreconditionResult.FromSuccess());
            }

            return Task.FromResult(PreconditionResult.FromError("Not a reader or admin"));
        }
    }
}
