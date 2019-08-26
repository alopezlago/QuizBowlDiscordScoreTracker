using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;

namespace QuizBowlDiscordScoreTracker
{
    public class BotCommandHandler
    {
        public async Task SetReader(ICommandContextWrapper context)
        {
            if (!(await context.HasUserId(context.UserId)))
            {
                // If the reader doesn't exist anymore, don't start a game.
                return;
            }

            GameState gameState = context.State;
            if (gameState == null)
            {
                gameState = new GameState();
                context.State = gameState;
            }
            else if (gameState.ReaderId != null)
            {
                // We already have a reader, so do nothing.
                return;
            }

            gameState.ReaderId = context.UserId;
            await context.RespondAsync($"{context.UserMention} is the reader.");
        }

        public async Task SetNewReader(ICommandContextWrapper context, ulong newReaderId)
        {
            if (context.CanPerformReaderActions)
            {
                if (await context.HasUserId(newReaderId))
                {
                    context.State.ReaderId = newReaderId;
                    string mention = await context.GetUserMention(newReaderId);
                    await context.RespondAsync($"{mention} is now the reader.");
                    return;
                }

                await context.RespondAsync($"User could not be found. Could not set the new reader.");
            }
        }

        public Task Clear(ICommandContextWrapper context)
        {
            if (context.CanPerformReaderActions)
            {
                context.State.ClearCurrentRound();
            }

            return Task.CompletedTask;
        }

        public async Task ClearAll(ICommandContextWrapper context)
        {
            if (context.CanPerformReaderActions)
            {
                context.State.ClearAll();
                context.State = null;
                await context.RespondAsync($"Reading over. All stats cleared.");
            }
        }

        public async Task GetScore(ICommandContextWrapper context)
        {
            if (context.State?.ReaderId != null)
            {
                IEnumerable<KeyValuePair<ulong, int>> scores = context.State.GetScores();

                DiscordEmbedBuilder builder = new DiscordEmbedBuilder();
                builder.Title = scores.Take(checked(GameState.ScoresListLimit + 1)).Count() > GameState.ScoresListLimit ?
                    $"Top {GameState.ScoresListLimit} Scores" :
                    "Scores";
                builder.WithColor(DiscordColor.Gold);
                foreach (KeyValuePair<ulong, int> score in scores.Take(GameState.ScoresListLimit))
                {
                    string name = (await context.GetUserNickname(score.Key)) ?? "<Unknown>";
                    builder.AddField(name, score.Value.ToString());
                }

                DiscordEmbed embed = builder.Build();
                await context.RespondAsync(embed: embed);
            }
        }

        public Task NextQuestion(ICommandContextWrapper context)
        {
            if (context.CanPerformReaderActions)
            {
                context.State.NextQuestion();
            }

            return Task.CompletedTask;
        }

        public async Task Undo(ICommandContextWrapper context)
        {
            if (context.CanPerformReaderActions && context.State.Undo(out ulong userId))
            {
                await context.RespondAsync($"Undid scoring for {context.GetUserNickname(userId)}");
            }
        }
    }
}
