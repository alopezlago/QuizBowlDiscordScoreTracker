using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace QuizBowlDiscordScoreTracker
{
    public class GameStateManager
    {
        // TODO: Make this a concurrent dictionary?
        private readonly IDictionary<ulong, GameState> gamesInChannel;

        public GameStateManager()
        {
            this.gamesInChannel = new Dictionary<ulong, GameState>();
        }

        public IEnumerable<KeyValuePair<ulong, GameState>> GetGameChannelPairs()
        {
            return ImmutableList<KeyValuePair<ulong, GameState>>.Empty.AddRange(this.gamesInChannel);
        }

        // Add, Get, Remove
        public bool TryCreate(ulong channelId, out GameState gameState)
        {
            gameState = new GameState();
            if (this.gamesInChannel.TryAdd(channelId, gameState))
            {
                return true;
            }

            gameState = null;
            return false;
        }

        public bool TryRemove(ulong channelId)
        {
            return this.gamesInChannel.Remove(channelId);
        }

        public bool TryGet(ulong channelId, out GameState gameState)
        {
            return this.gamesInChannel.TryGetValue(channelId, out gameState);
        }
    }
}
