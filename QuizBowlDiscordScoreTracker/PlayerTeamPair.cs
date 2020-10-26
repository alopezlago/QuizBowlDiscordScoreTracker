using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace QuizBowlDiscordScoreTracker
{
    public class PlayerTeamPair : IEquatable<PlayerTeamPair>
    {
        /// <summary>
        /// A pair representing a player's ID and team. The player and team IDs should be different if the player is on
        /// a team.
        /// </summary>
        /// <param name="playerId"></param>
        /// <param name="teamId"></param>
        public PlayerTeamPair(ulong playerId, string playerDisplayName, string teamId)
        {
            this.PlayerDisplayName = playerDisplayName;
            this.PlayerId = playerId;
            this.TeamId = teamId ?? playerId.ToString(CultureInfo.InvariantCulture);
            this.IsOnTeam = teamId != null;
        }

        public bool IsOnTeam { get; }

        // PlayerDisplayName shouldn't be used for equality
        public string PlayerDisplayName { get; private set; }

        public ulong PlayerId { get; private set; }

        public string TeamId { get; private set; }

        public override bool Equals(object obj)
        {
            if (!(obj is PlayerTeamPair other))
            {
                return false;
            }

            return this.Equals(other);
        }

        public bool Equals([AllowNull] PlayerTeamPair other)
        {
            if (other == null)
            {
                return false;
            }

            return other.PlayerId == this.PlayerId && other.TeamId == this.TeamId;
        }

        public override int GetHashCode()
        {
            return this.PlayerId.GetHashCode() ^ this.TeamId.GetHashCode(StringComparison.Ordinal);
        }
    }
}
