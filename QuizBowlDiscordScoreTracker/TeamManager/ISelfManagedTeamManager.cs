namespace QuizBowlDiscordScoreTracker.TeamManager
{
    /// <summary>
    /// ISelfManagedTeamManager should manage team and player -> team mappings itself
    /// </summary>
    public interface ISelfManagedTeamManager : ITeamManager
    {
        bool TryAddPlayerToTeam(ulong userId, string teamName);
        bool TryRemovePlayerFromTeam(ulong userId);
        bool TryAddTeam(string teamName, out string message);
        bool TryRemoveTeam(string teamName, out string message);
    }
}
