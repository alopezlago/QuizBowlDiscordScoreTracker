using System;

namespace QuizBowlDiscordScoreTracker
{
    public class Buzz : IComparable<Buzz>
    {
        public ulong UserId { get; set; }

        public ulong? TeamId { get; set; }

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
            return this.UserId.GetHashCode() ^ this.Timestamp.GetHashCode() ^ (this.TeamId?.GetHashCode() ?? 0);
        }

        public override bool Equals(object obj)
        {
            if (obj is Buzz entry)
            {
                return this.UserId == entry.UserId &&
                    this.Timestamp.Equals(entry.Timestamp) &&
                    this.TeamId == entry.TeamId;
            }

            return false;
        }

        public static bool operator ==(Buzz left, Buzz right)
        {
            if (ReferenceEquals(left, null))
            {
                return ReferenceEquals(right, null);
            }

            return left.Equals(right);
        }

        public static bool operator !=(Buzz left, Buzz right)
        {
            return !(left == right);
        }

        public static bool operator <(Buzz left, Buzz right)
        {
            return ReferenceEquals(left, null) ? !ReferenceEquals(right, null) : left.CompareTo(right) < 0;
        }

        public static bool operator <=(Buzz left, Buzz right)
        {
            return ReferenceEquals(left, null) || left.CompareTo(right) <= 0;
        }

        public static bool operator >(Buzz left, Buzz right)
        {
            return !ReferenceEquals(left, null) && left.CompareTo(right) > 0;
        }

        public static bool operator >=(Buzz left, Buzz right)
        {
            return ReferenceEquals(left, null) ? ReferenceEquals(right, null) : left.CompareTo(right) >= 0;
        }
    }
}
