using System;
using DSharpPlus.Entities;

namespace QuizBowlDiscordScoreTracker
{
    public class Buzz : IComparable<Buzz>
    {
        public DiscordUser User { get; set; }

        public DateTime Timestamp { get; set; }

        public int CompareTo(Buzz other)
        {
            if (other == null)
            {
                return 1;
            }

            return this.Timestamp.CompareTo(other.Timestamp);
        }

        public override int GetHashCode()
        {
            return this.User.GetHashCode() ^ this.Timestamp.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is Buzz entry)
            {
                return this.User.Equals(entry.User) && this.Timestamp.Equals(entry.Timestamp);
            }

            return false;
        }
    }
}
