using System;
using System.Collections.Generic;

namespace pmPoker
{
    public class HandEvaluator
    {
        public const int StraightFlush = 8;
        public const int FourOfAKind = 7;
        public const int FullHouse = 6;
        public const int Flush = 5;
        public const int Straight = 4;
        public const int ThreeOfAKind = 3;
        public const int TwoPair = 2;
        public const int OnePair = 1;
        public const int HighCard = 0;

        public const ulong WheelMask = 0x3C;
        public const ulong AceMask = 1UL << 14;


        public static Result Evaluate(PlayingCard[] playerCards, List<PlayingCard> communityCards)
        {
            var handBitmap = HandToBitmap(playerCards, communityCards);

            ulong rankBitmap = 0;
            ulong pairBitmap = 0;
            ulong threeOfAKindBitMap = 0;
            ulong fourOfAKindBitmap = 0;

            // (Four-Of-A-Kind, Full-House, Flush, Straight) cannot occur at the
            // same time and therefore the order of evaluation is irrelevant


            for (int suit = 0; suit < 4; suit++)
            {
                var suitMask = SuittMask(handBitmap, suit);
                var suitCount = BitCount(suitMask);

                if (suitCount >= 5)
                {
                    if ((suitMask & WheelMask) == WheelMask && (suitMask & AceMask) != 0)
                        return new Result(StraightFlush, (ulong)LSB(AceMask));

                    var straightFlushMask = StraightMask(suitMask);
                    if (straightFlushMask != 0)
                        return new Result(StraightFlush, (ulong)MSB(straightFlushMask));



                    while (suitCount > 5)
                    {
                        suitMask = ResetLSB(suitMask);
                        suitCount--;
                    }

                    return new Result(Flush, suitMask);
                }

                fourOfAKindBitmap |= threeOfAKindBitMap & suitMask;
                threeOfAKindBitMap |= pairBitmap & suitMask;
                pairBitmap |= rankBitmap & suitMask;
                rankBitmap |= suitMask;
            }

            if (fourOfAKindBitmap != 0)
                return new Result(FourOfAKind, (ulong)MSB(rankBitmap ^ fourOfAKindBitmap));

            if ((rankBitmap & WheelMask) == WheelMask && (rankBitmap & AceMask) != 0)
                return new Result(Straight, (ulong)LSB(AceMask));

            var straightMask = StraightMask(rankBitmap);
            if (straightMask != 0)
                return new Result(Straight, (ulong)MSB(straightMask));


            if (pairBitmap != 0)
            {
                if (threeOfAKindBitMap != 0)
                {
                    if (ResetLSB(threeOfAKindBitMap) != 0) // full house, choose higher three of a kind
                        return new Result(FullHouse, FullHouseTiebreaker(MSB(threeOfAKindBitMap), LSB(threeOfAKindBitMap)));

                    if (threeOfAKindBitMap != pairBitmap)
                        return new Result(FullHouse, FullHouseTiebreaker(LSB(threeOfAKindBitMap), MSB(pairBitmap ^ threeOfAKindBitMap)));

                    return new Result(ThreeOfAKind, ThreeOfAKindTiebreaker(threeOfAKindBitMap, rankBitmap));
                }

                if (ResetLSB(pairBitmap) != 0)
                {
                    var highPair = MSB(pairBitmap);
                    var highPairSetMask = 1UL << highPair;
                    var secondPair = MSB(pairBitmap ^ highPairSetMask);
                    var secondPairSetMask = 1UL << secondPair;

                    return new Result(TwoPair, TwoPairTiebreaker(highPairSetMask, secondPairSetMask, rankBitmap));
                }

                return new Result(OnePair, OnePairTiebreaker(pairBitmap, rankBitmap));
            }

            // for sure we have 7 different cards => leave 5 higher
            return new Result(HighCard, ResetLSB(ResetLSB(rankBitmap)));
        }

        private static ulong HandToBitmap(PlayingCard[] playerCards, List<PlayingCard> communityCards)
        {
            return HandToBitmap(playerCards) | HandToBitmap(communityCards);
        }

        private static ulong HandToBitmap(IEnumerable<PlayingCard> cards)
        {
            ulong bitmask = 0;

            foreach (var card in cards)
            {
                bitmask |= CardToBitmask(card);
            }

            return bitmask;
        }

        private static ulong CardToBitmask(PlayingCard card)
        {
            // every 16 bit word contains a suit from bit 2 to bit 14
            return (ulong)(1 << card.Rank) << (16 * card.Suit);
        }

        private static int MSB(ulong bitmask)
        {
            return 63 - System.Numerics.BitOperations.LeadingZeroCount(bitmask);
        }

        private static int LSB(ulong bitmask)
        {
            return System.Numerics.BitOperations.TrailingZeroCount(bitmask);
        }

        private static ulong ResetLSB(ulong bitmask)
        {
            return bitmask & (bitmask - 1);
        }

        private static int BitCount(ulong bitmask)
        {
            return System.Numerics.BitOperations.PopCount(bitmask);
        }

        private static ulong StraightMask(ulong bitmask)
        {
            return bitmask & (bitmask << 1) & (bitmask << 2) & (bitmask << 3) & (bitmask << 4);
        }

        private static ulong SuittMask(ulong bitmask, int suit)
        {
            return (bitmask >> (16 * suit)) & 0xFFFC;
        }

        private static ulong FullHouseTiebreaker(int threeOfAKindRank, int pairRank)
        {
            return (1UL << (threeOfAKindRank + 16)) | (1UL << pairRank);
        }

        public static ulong ThreeOfAKindTiebreaker(ulong threeOfAKindBitmap, ulong rankBitmap)
        {
            return (threeOfAKindBitmap << 16) | ResetLSB(ResetLSB(rankBitmap ^ threeOfAKindBitmap));
        }

        private static ulong OnePairTiebreaker(ulong pairBitmap, ulong rankBitmap)
        {
            return (pairBitmap << 16) | ResetLSB(ResetLSB(rankBitmap ^ pairBitmap));
        }

        private static ulong TwoPairTiebreaker(ulong highPairSetMask, ulong secondPairSetMask, ulong rankBitmap)
        {
            return (highPairSetMask << 32) | (secondPairSetMask << 16) + (ulong)MSB(rankBitmap ^ highPairSetMask ^ secondPairSetMask);
        }

        public struct Result : IComparable<Result>
        {
            public int HandType { get; set; }
            public ulong Tiebreaker { get; set; }

            public Result(int handType, ulong tiebreaker)
            {
                HandType = handType;
                Tiebreaker = tiebreaker;
            }

            public override string ToString()
            {
                return string.Format("(HandType: {0}, Tiebreaker: {1}", HandType, Tiebreaker);
            }

            public int CompareTo(Result other)
            {
                int result = HandType.CompareTo(other.HandType);

                return result != 0 ? result : Tiebreaker.CompareTo(other.Tiebreaker);
            }
        }
    }
}
