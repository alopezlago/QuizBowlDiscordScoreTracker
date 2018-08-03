using System;

namespace QuizBowlDiscordScoreTracker
{
    public class Buzz : IComparable<Buzz>
    {
        public ulong UserId { get; set; }

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
            return this.UserId.GetHashCode() ^ this.Timestamp.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is Buzz entry)
            {
                return this.UserId == entry.UserId && this.Timestamp.Equals(entry.Timestamp);
            }

            return false;
        }
    }
}
