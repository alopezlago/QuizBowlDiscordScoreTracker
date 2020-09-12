using System;
using System.Collections.Generic;
using System.Linq;
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

        [Command("help")]
        [Summary("Lists available commands and how to use them.")]
        public Task HelpAsync()
        {
            return this.SendHelpInformationAsync();
        }

        [Command("help")]
        [Summary("Lists available commands and how to use them.")]
        public Task HelpAsync([Remainder][Summary("Command name")] string rawCommandName)
        {
            return this.SendHelpInformationAsync(rawCommandName);
        }

        private async Task SendHelpInformationAsync(string rawCommandName = null)
        {
            EmbedBuilder embedBuilder = new EmbedBuilder();
            IEnumerable<CommandInfo> commands = this.commandService.Commands
                .Where(command => command.Name != "help");

            if (rawCommandName != null)
            {
                string commandName = rawCommandName.Trim();
                commands = commands
                    .Where(command => command.Name.Equals(commandName, StringComparison.CurrentCultureIgnoreCase));
            }

            EmbedFieldBuilder[] embedFields = await Task.WhenAll(commands
                .Select(commandInfo => this.GetEmbedFieldOrNull(commandInfo)));
            foreach (EmbedFieldBuilder embedField in embedFields.Where(field => field != null))
            {
                embedBuilder.AddField(embedField);
            }

            // DM the user, so that we can't spam the channel.
            await this.Context.User.SendMessageAsync(embed: embedBuilder.Build());
        }

        private async Task<EmbedFieldBuilder> GetEmbedFieldOrNull(CommandInfo commandInfo)
        {
            // Only show the bot owner commands to the bot owner. Instead of checking each precondition, just cheat and
            // see if we require the bot owner.
            if (commandInfo.Module.Preconditions.Any(attribute => attribute is RequireOwnerAttribute) &&
                this.Context.User.Id != (await this.Context.Client.GetApplicationInfoAsync()).Owner.Id)
            {
                return null;
            }

            // We could try to limit who can see admin commands, but that doesn't work if they ask for help in a DM.

            string parameters = string.Join(' ', commandInfo.Parameters.Select(parameter => $"*{parameter.Name}*"));
            return new EmbedFieldBuilder()
            {
                Name = $"{commandInfo.Name} {parameters}",
                Value = commandInfo.Summary ?? "<undocumented>"
            };
        }
    }
}
