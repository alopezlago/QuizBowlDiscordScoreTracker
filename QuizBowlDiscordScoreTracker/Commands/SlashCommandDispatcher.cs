using System;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using QuizBowlDiscordScoreTracker.Database;
using Serilog;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public sealed class SlashCommandDispatcher
    {
        private static readonly ILogger Logger = Log.ForContext<SlashCommandDispatcher>();
        private readonly IDatabaseActionFactory databaseActionFactory;

        public SlashCommandDispatcher(IDatabaseActionFactory databaseActionFactory)
        {
            this.databaseActionFactory = databaseActionFactory;
        }

        public async Task ExecuteAsync(
            IInteractionContext context, Func<Task<Discord.Interactions.IResult>> executeCommand)
        {
            // Buzzes need to be fast, so skip a lot of the deferral logic if possible
            if (context.Interaction is ISlashCommandInteraction buzzCommand && buzzCommand.Data.Name == "buzz")
            {
                await this.ExecuteBuzzAsync(context, executeCommand);
                return;
            }

            try
            {
                await context.Interaction.DeferAsync(ephemeral: true);
                // Complete the private acknowledgment before sending public follow-ups.
                await context.Interaction.ModifyOriginalResponseAsync(
                    properties => properties.Content = "Processing command...");

                bool isUnbanCommand = context.Interaction is ISlashCommandInteraction slashCommand &&
                    slashCommand.Data.Name == "unban-user";
                if (!isUnbanCommand)
                {
                    using (DatabaseAction action = this.databaseActionFactory.Create())
                    {
                        if (await action.GetCommandBannedAsync(context.User.Id))
                        {
                            await context.Interaction.ModifyOriginalResponseAsync(
                                properties => properties.Content = "You are banned from using commands.");
                            return;
                        }
                    }
                }

                Discord.Interactions.IResult result = await executeCommand();
                if (!result.IsSuccess)
                {
                    Logger.Warning("Error executing slash command: {Error}: {Reason}", result.Error, result.ErrorReason);
                    await context.Interaction.ModifyOriginalResponseAsync(
                        properties => properties.Content = result.ErrorReason ?? "The command could not be completed.");
                    return;
                }

                await context.Interaction.DeleteOriginalResponseAsync();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Exception processing slash command");
                const string message = "An error occurred while running the command.";
                if (context.Interaction.HasResponded)
                {
                    await context.Interaction.ModifyOriginalResponseAsync(properties => properties.Content = message);
                }
                else
                {
                    await context.Interaction.RespondAsync(message, ephemeral: true);
                }
            }
        }

        private async Task ExecuteBuzzAsync(
            IInteractionContext context, Func<Task<Discord.Interactions.IResult>> executeCommand)
        {
            using (CancellationTokenSource acknowledgmentCancellation = new CancellationTokenSource())
            {
                Task<bool> acknowledgment = AcknowledgeSlowBuzzAsync(context.Interaction, acknowledgmentCancellation.Token);
                try
                {
                    using (DatabaseAction action = this.databaseActionFactory.Create())
                    {
                        if (await action.GetCommandBannedAsync(context.User.Id))
                        {
                            await context.Interaction.RespondOrFollowupAsync(
                                "You are banned from using commands.", ephemeral: true);
                            return;
                        }
                    }

                    Discord.Interactions.IResult result = await executeCommand();
                    if (!result.IsSuccess)
                    {
                        Logger.Warning("Error executing buzz command: {Error}: {Reason}", result.Error, result.ErrorReason);
                        await context.Interaction.RespondOrFollowupAsync(
                            result.ErrorReason ?? "The command could not be completed.", ephemeral: true);
                    }
                    else if (!context.Interaction.HasResponded)
                    {
                        await context.Interaction.TryRespondAsync("Your buzz was processed.", ephemeral: true);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Exception processing buzz command");
                    await context.Interaction.RespondOrFollowupAsync(
                        "An error occurred while running the command.", ephemeral: true);
                }
                finally
                {
                    acknowledgmentCancellation.Cancel();
                    if (await acknowledgment)
                    {
                        await context.Interaction.DeleteOriginalResponseAsync();
                    }
                }
            }
        }

        private static async Task<bool> AcknowledgeSlowBuzzAsync(
            IDiscordInteraction interaction, CancellationToken cancellationToken)
        {
            try
            {
                TimeSpan delay = interaction.CreatedAt.AddSeconds(2) - DateTimeOffset.UtcNow;

                // Fast buzzes cancel this delay, so an OperationCanceledException is expected and handled below.
                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, cancellationToken);
                return await interaction.TryRespondAsync("Processing buzz...", ephemeral: true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Exception acknowledging slow buzz command");
                return false;
            }
        }
    }
}
