using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Discord;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public static class InteractionResponseExtensions
    {
        // Per-interaction semaphores serialize make sure that handlers with a fast and slow path (like buzzes) don't
        // get both interactions responded to.
        // Weak keys allow completed interactions and their semaphores to be collected, rather than retained
        // indefinitely by a static dictionary or requiring explicit cleanup.
        private static readonly ConditionalWeakTable<IDiscordInteraction, SemaphoreSlim> ResponseLocks = new();

        public static async Task RespondOrFollowupAsync(
            this IDiscordInteraction interaction, string text = null, Embed embed = null, bool ephemeral = false)
        {
            SemaphoreSlim responseLock = ResponseLocks.GetValue(interaction, _ => new SemaphoreSlim(1, 1));
            await responseLock.WaitAsync();
            try
            {
                if (interaction.HasResponded)
                {
                    await interaction.FollowupAsync(text: text, embed: embed, ephemeral: ephemeral);
                }
                else
                {
                    await interaction.RespondAsync(text: text, embed: embed, ephemeral: ephemeral);
                }
            }
            finally
            {
                responseLock.Release();
            }
        }

        internal static async Task<bool> TryRespondAsync(
            this IDiscordInteraction interaction, string text, bool ephemeral)
        {
            SemaphoreSlim responseLock = ResponseLocks.GetValue(interaction, _ => new SemaphoreSlim(1, 1));
            await responseLock.WaitAsync();
            try
            {
                if (interaction.HasResponded)
                {
                    return false;
                }

                await interaction.RespondAsync(text: text, ephemeral: ephemeral);
                return true;
            }
            finally
            {
                responseLock.Release();
            }
        }

        public static async Task RespondOrFollowupWithFileAsync(
            this IDiscordInteraction interaction, Stream stream, string filename, string text = null)
        {
            if (interaction.HasResponded)
            {
                await interaction.FollowupWithFileAsync(stream, filename, text: text);
            }
            else
            {
                await interaction.RespondWithFileAsync(stream, filename, text: text);
            }
        }
    }
}
