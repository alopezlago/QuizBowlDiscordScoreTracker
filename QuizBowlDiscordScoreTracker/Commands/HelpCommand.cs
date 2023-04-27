using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;

namespace QuizBowlDiscordScoreTracker.Commands
{
    public class HelpCommand : ModuleBase
    {
        private readonly CommandService commandService;

        public HelpCommand(CommandService commandService)
        {
            this.commandService = commandService;
        }

        [Alias("h")]
        [Command("help")]
        [Summary("Lists available commands and how to use them.")]
        public Task HelpAsync()
        {
            return this.SendAllCommandsHelpInformationAsync();
        }

        [Alias("h")]
        [Command("help")]
        [Summary("Lists available commands and how to use them.")]
        public Task HelpAsync([Remainder][Summary("Command name")] string rawCommandName)
        {
            Verify.IsNotNull(rawCommandName, nameof(rawCommandName));
            return this.SendOneCommandHelpInformationAsync(rawCommandName);
        }

        private async Task SendOneCommandHelpInformationAsync(string rawCommandName)
        {
            IEnumerable<CommandInfo> commands = this.commandService.Commands.Where(command => command.Name != "help");
            string commandName = rawCommandName.Trim();
            commands = commands
                .Where(command => command.Name.Contains(commandName, StringComparison.CurrentCultureIgnoreCase));

            bool userIsBotOwner = this.Context.User.Id == (await this.Context.Client.GetApplicationInfoAsync()).Owner.Id;
            if (!userIsBotOwner)
            {
                // Only let owners see owner-only commands
                commands = commands
                    .Where(command => !command.Module.Preconditions.Any(attribute => attribute is RequireOwnerAttribute));
            }

            await this.Context.Channel.SendAllEmbeds(
                commands,
                () => new EmbedBuilder()
                {
                    Title = "Commands",
                    Color = Color.Gold,

                },
                (commandInfo, index) =>
                {
                    string parameters = string.Join(' ', commandInfo.Parameters
                        .Select(parameter => GetParameterFieldName(parameter)));
                    StringBuilder builder = new StringBuilder(commandInfo.Summary ?? "<undocumented>");
                    if (commandInfo.Parameters.Count > 0)
                    {
                        builder.AppendLine();
                        builder.AppendLine();
                        builder.AppendLine("**Parameters**");
                        builder.AppendLine();
                        foreach (ParameterInfo parameter in commandInfo.Parameters)
                        {
                            builder.AppendLine(
                                CultureInfo.InvariantCulture,
                                $"*{GetParameterFieldName(parameter)}*{(parameter.IsOptional ? " (optional)" : string.Empty)}: " +
                                $"{parameter.Summary ?? "*no description*"}");
                        }
                    }

                    return new EmbedFieldBuilder()
                    {
                        Name = $"{commandInfo.Name} {parameters}",
                        Value = builder.ToString()
                    };
                });
        }

        private async Task SendAllCommandsHelpInformationAsync()
        {
            IEnumerable<CommandInfo> commands = this.commandService.Commands.Where(command => command.Name != "help");

            bool userIsBotOwner = this.Context.User.Id == (await this.Context.Client.GetApplicationInfoAsync()).Owner.Id;
            if (!userIsBotOwner)
            {
                // Only let owners see owner-only commands
                commands = commands
                    .Where(command => !command.Module.Preconditions.Any(attribute => attribute is RequireOwnerAttribute));
            }

            commands = commands.OrderBy(command => command.Name);

            // Send two messages: a "how to play", and then the commands
            EmbedBuilder howToPlayEmbedBuilder = new EmbedBuilder()
            {
                Title = "How to play",
                Color = Color.Gold,
                Description = "Visit [the wiki](https://github.com/alopezlago/QuizBowlDiscordScoreTracker/wiki) or [the official server](https://discord.gg/s2nRnKRFpd) for more information.\n\n" +
                    "1. The reader should use the !read command.\n" +
                    "2. When a player wants to buzz in, they should type in \"buzz\" (near equivalents like \"bzz\" are acceptable).\n" +
                    "3. The reader scores the buzz by typing in the value (-5, 0, 10, 15, 20).\n" +
                    "4. If someone gets the question correct, the buzz queue is cleared. If no one answers the question, then use the !next command to clear the queue and start the next question.\n" +
                    "5. If the reader needs to undo a scoring action, they should use the !undo command.\n" +
                    "6. To see the score, use the !score command.\n" +
                    "7. Once the packet reading is over, the reader should use !end to end the game."
            };
            await this.Context.Channel.SendMessageAsync(embed: howToPlayEmbedBuilder.Build());

            await this.Context.Channel.SendAllEmbeds(
                commands,
                () => new EmbedBuilder()
                {
                    Title = "Commands",
                    Color = Color.Gold
                },
                (commandInfo, index) =>
                {
                    string parameters = string.Join(' ', commandInfo.Parameters
                        .Select(parameter => GetParameterFieldName(parameter)));

                    // Include the other aliases in the description
                    string summary = string.Empty;
                    if (commandInfo.Aliases.Count > 1)
                    {
                        summary = "Aliases for this command: **" + string.Join(", ", commandInfo.Aliases.Skip(1)) + "**. ";
                    }

                    summary += commandInfo.Summary ?? "<undocumented>";


                    return new EmbedFieldBuilder()
                    {
                        Name = $"{commandInfo.Name} {parameters}",
                        Value = summary
                    };
                });
        }

        private static string GetParameterFieldName(ParameterInfo parameter)
        {
            bool isChannel = parameter.Type.GetInterface(nameof(IGuildChannel)) != null;
            bool isMention = !isChannel && parameter.Type.GetInterface(nameof(IUser)) != null;
            return $"*{(isChannel ? "#" : string.Empty)}{(isMention ? "@" : string.Empty)}{parameter.Name}*";
        }
    }
}
