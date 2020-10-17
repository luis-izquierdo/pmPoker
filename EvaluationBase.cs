using System;
using System.Collections.Generic;

namespace pmPoker
{
    public interface IHandEvaluator
    {
        HandEvaluation Evaluate(PlayingCard[] playerCards, List<PlayingCard> communityCards);
    }

    public struct HandEvaluation : IComparable<HandEvaluation>
    {
        public HandType HandType { get; set; }
        public ulong Tiebreaker { get; set; }

        public HandEvaluation(HandType handType, ulong tiebreaker)
        {
            HandType = handType;
            Tiebreaker = tiebreaker;
        }

        public override string ToString()
        {
            return string.Format("(HandType: {0}, Tiebreaker: {1}", HandType, Tiebreaker);
        }

        public int CompareTo(HandEvaluation other)
        {
            int result = HandType.CompareTo(other.HandType);

            return result != 0 ? result : Tiebreaker.CompareTo(other.Tiebreaker);
        }
    }

    public enum HandType
    {
        RoyalFlush = 9,
        StraightFlush = 8,
        FourOfAKind = 7,
        FullHouse = 6,
        Flush = 5,
        Straight = 4,
        ThreeOfAKind = 3,
        TwoPair = 2,
        OnePair = 1,
        HighCard = 0
    }
}