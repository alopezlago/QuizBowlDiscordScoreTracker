using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using QuizBowlDiscordScoreTracker.Database;

namespace QuizBowlDiscordScoreTracker.Commands
{
    [RequireReader]
    [RequireContext(ContextType.Guild)]
    public class ReaderCommands : ModuleBase
    {
        public ReaderCommands(GameStateManager manager, IDatabaseActionFactory dbActionFactory)
        {
            this.Manager = manager;
            this.DatabaseActionFactory = dbActionFactory;
        }

        private IDatabaseActionFactory DatabaseActionFactory { get; }

        private GameStateManager Manager { get; }

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
        [Summary("Reload a teams of the game (not available if the team role prefix is set).")]
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
        public Task SetNewReaderAsync([Summary("Mention of the new reader")] IGuildUser newReader)
        {
            return this.GetHandler().SetNewReaderAsync(newReader);
        }

        [Command("stop")]
        [Summary("Ends the game, clearing the stats and allowing others to read.")]
        public Task StopAsync()
        {
            return this.GetHandler().ClearAllAsync();
        }

        [Command("end")]
        [Summary("Ends the game, clearing the stats and allowing others to read.")]
        public Task EndAsync()
        {
            return this.GetHandler().ClearAllAsync();
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
            return new ReaderCommandHandler(this.Context, this.Manager, this.DatabaseActionFactory);
        }
    }
}
