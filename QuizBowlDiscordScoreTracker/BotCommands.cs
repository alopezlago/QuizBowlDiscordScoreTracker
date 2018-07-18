using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace QuizBowlDiscordScoreTracker
{
    public class BotCommands
    {
        // May just want this in the Bot class, since we want the queues.
        [Command("hi")]
        public async Task Hi(CommandContext ctx)
        {
            await ctx.RespondAsync($"Hi there, {ctx.User.Mention}!");
        }
    }
}
