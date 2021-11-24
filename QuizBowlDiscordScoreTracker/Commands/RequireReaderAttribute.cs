using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class RequireReaderAttribute : PreconditionAttribute
    {
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Discord.Net will pass in non-null CommandContext")]
        public override Task<PreconditionResult> CheckPermissionsAsync(
            ICommandContext context, CommandInfo command, IServiceProvider services)
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
