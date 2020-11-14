using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Discord.Commands;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [AttributeUsage(AttributeTargets.Method)]
    public class HumanOnlyAttribute : PreconditionAttribute
    {
        [SuppressMessage(
            "Design",
            "CA1062:Validate arguments of public methods",
            Justification = "Discord.Net will pass in non-null CommandContext")]
        public override Task<PreconditionResult> CheckPermissionsAsync(
            ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.User.IsBot)
            {
                return Task.FromResult(PreconditionResult.FromError("Bots are not allowed to run this command."));
            }

            return Task.FromResult(PreconditionResult.FromSuccess());
        }
    }
}
