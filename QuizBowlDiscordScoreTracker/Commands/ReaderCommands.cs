using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Options;
using QuizBowlDiscordScoreTracker.Database;
using QuizBowlDiscordScoreTracker.Scoresheet;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireReader]
    [RequireContext(ContextType.Guild)]
    public class ReaderCommands : ModuleBase
    {
        public ReaderCommands(
            GameStateManager manager,
            IOptionsMonitor<BotConfiguration> options,
            IDatabaseActionFactory dbActionFactory,
            IFileScoresheetGenerator scoresheetGenerator,
            IGoogleSheetsGeneratorFactory googleSheetsGeneratorFactory)
        {
            this.Manager = manager;
            this.Options = options;
            this.DatabaseActionFactory = dbActionFactory;
            this.ScoresheetGenerator = scoresheetGenerator;
            this.GoogleSheetsGeneratorFactory = googleSheetsGeneratorFactory;
        }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        private IGoogleSheetsGeneratorFactory GoogleSheetsGeneratorFactory { get; }

        private GameStateManager Manager { get; }

        private IOptionsMonitor<BotConfiguration> Options { get; }

        private IFileScoresheetGenerator ScoresheetGenerator { get; }

        [Command("addTeam")]
        [Summary("Adds a team to the game (not available if the team role prefix is set).")]
        public Task AddTeamAsync([Summary("Name of the team you are adding")][Remainder] string teamName)
        {
            return this.GetHandler().AddTeamAsync(teamName);
        }

        [Command("removeTeam")]
        [Summary("Removes a team from the game (not available if the team role prefix is set).")]
        public Task RemoveTeamAsync([Summary("Name of the team you are removing")][Remainder] string teamName)
        {
            return this.GetHandler().RemoveTeamAsync(teamName);
        }

        [Command("reloadTeamRoles")]
        [Summary("Reload the teams of the game (not available if the team role prefix isn´t set).")]
        public Task ReloadTeamsAsync()
        {
            return this.GetHandler().ReloadTeamRoles();
        }

        [Command("removePlayer")]
        [Summary("Removes a player from the given team (not available if the team role prefix is set).")]
        public Task RemovePlayerAsync([Summary("Mention of the user to remove")] IGuildUser player)
        {
            return this.GetHandler().RemovePlayerAsync(player);
        }

        [Command("disableBonuses")]
        [Summary("Makes the current game track only tossups from now on. This command will reset the current cycle (like !clear)")]
        public Task DisableBonusesAsync()
        {
            return this.GetHandler().DisableBonusesAsync();
        }

        [Command("enableBonuses")]
        [Summary("Makes the current game track bonuses from now on. This command will reset the current cycle (like !clear)")]
        public Task EnableBonusesAsync()
        {
            return this.GetHandler().EnableBonusesAsync();
        }

        [Command("setnewreader")]
        [Summary("Set another user as the reader.")]
        public Task SetNewReaderAsync([Summary("Mention of the new reader to switch to")] IGuildUser newReader)
        {
            return this.GetHandler().SetNewReaderAsync(newReader);
        }

        [Command("stop")]
        [Alias("end")]
        [Summary("Ends the game, clearing the stats and allowing others to read.")]
        public Task EndAsync()
        {
            return this.GetHandler().ClearAllAsync();
        }

        [HumanOnly]
        [Command("exportToFile")]
        [Summary("Exports the scoresheet to a spreadsheet file. The spreadsheet is based on NAQT's electronic " +
            "scoresheet (© National Academic Quiz Tournaments, LLC). Export requires that one or two teams are " +
            "playing, that each team has at most 6 players, and that at most 24 tossups have been played.")]
        public Task ExportToFileAsync()
        {
            return this.GetHandler().ExportToFileAsync();
        }

        [HumanOnly]
        [Command("exportToTJ")]
        [Summary("Exports the scoresheet to the UCSD Scoresheet. Export requires that one or two teams are " +
            "playing, that each team has at most 6 players, and that at most 24 tossups have been played.")]
        [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
            Justification = "Discord.Net can't parse the argument directly as a URI")]
        public Task ExportToTJAsync(
            [Summary("The URL to the TJ Google Sheet. For example, https://docs.google.com/spreadsheets/d/1IJji0c23_mOYz6SnMiUlj2WDCIAHutIqfMdG-0hpEIU")] string sheetsUrl,
            [Summary("The round number, starting from 1")][Remainder] int round)
        {
            return this.GetHandler().ExportToTJ(sheetsUrl, round);
        }

        [HumanOnly]
        [Command("exportToUCSD")]
        [Summary("Exports the scoresheet to the UCSD Scoresheet. Export requires that one or two teams are " +
            "playing, that each team has at most 6 players, and that at most 24 tossups have been played.")]
        [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
            Justification = "Discord.Net can't parse the argument directly as a URI")]
        public Task ExportToUCSDAsync(
            [Summary("The URL to the UCSD Google Sheet. For example, https://docs.google.com/spreadsheets/d/00000-0oVdTQYtgzHPI7L8kf3xePu5fyi_u-00000000")] string sheetsUrl,
            [Summary("The round number, starting from 1")][Remainder] int round)
        {
            return this.GetHandler().ExportToUCSD(sheetsUrl, round);
        }

        [Command("clear")]
        [Summary("Clears the player queue and answers from this question, including scores from this question. This can only be used during the tossup stage.")]
        public Task ClearAsync()
        {
            return this.GetHandler().ClearAsync();
        }

        [Command("next")]
        [Summary("Clears the player queue and moves to the next question. Use this if no one answered correctly.")]
        public Task NextAsync()
        {
            return this.GetHandler().NextAsync();
        }

        [Command("undo")]
        [Summary("Undoes a scoring operation.")]
        public Task UndoAsync()
        {
            return this.GetHandler().UndoAsync();
        }

        private ReaderCommandHandler GetHandler()
        {
            // this.Context is null in the constructor, so create the handler in this method
            return new ReaderCommandHandler(
                this.Context,
                this.Manager,
                this.Options,
                this.DatabaseActionFactory,
                this.ScoresheetGenerator,
                this.GoogleSheetsGeneratorFactory);
        }
    }
}
