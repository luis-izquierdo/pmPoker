using System;
using System.Collections.Generic;
using System.Linq;

namespace pmPoker
{
    class LuisEvaluator: IHandEvaluator
    {
		public HandEvaluation Evaluate(PlayingCard[] playerCards, List<PlayingCard> communityCards)
		{
			PlayingCard[] sevenCards = playerCards.Concat(communityCards).ToArray();
			return EvalBestHand(sevenCards);
		}
        static HandEvaluation EvalBestHand(PlayingCard[] sevenCards)
        {
        	if (sevenCards.Length != 7)
        		throw new ArgumentException("sevenCards");
        	HandEvaluation bestEvaluation = new HandEvaluation(HandType.HighCard, 0);
			var firstCandidate = true;

        	for (var first = 0; first < 3; first++)
        		for (var second = first + 1; second < 4; second++)
        			for (var third = second + 1; third < 5; third++)
        				for (var fourth = third + 1; fourth < 6; fourth++)
        					for (var fifth = fourth + 1; fifth < 7; fifth++)
        					{
        						var hand = new [] {
        							sevenCards[first],
        							sevenCards[second],
        							sevenCards[third],
        							sevenCards[fourth],
        							sevenCards[fifth]
        						};
        						var e = Eval(hand);
        						if (firstCandidate || e.CompareTo(bestEvaluation) > 0)
        							bestEvaluation = e;
								firstCandidate = false;
        					}
        	return bestEvaluation;
        }

        static HandEvaluation Eval(PlayingCard[] hand)
        {
        	int[] countPerRank = new int[13];
    		int[] countPerSuit = new int[4];
    		// ranks go from 0 to 12
    		int minRank = 13;
    		int maxRank = -1;
    		// alternate ranks (where A is the lowest ranking) go from -1 to 11
    		// used to determine if there is a straight starting with an A
    		int minAlternateRank = 12;
    		int maxAlternateRank = -2;
    		foreach (var c in hand)
    		{
				var zeroBaseRank = c.Rank - 2;
    			countPerRank[zeroBaseRank]++;
    			countPerSuit[c.Suit]++;
    			minRank = Math.Min(minRank, zeroBaseRank);
    			maxRank = Math.Max(maxRank, zeroBaseRank);
    			var alternateRank = zeroBaseRank == 12 ? -1 : zeroBaseRank;	// same as regular rank, except for the A
    			minAlternateRank = Math.Min(minAlternateRank, alternateRank);
    			maxAlternateRank = Math.Max(maxAlternateRank, alternateRank);
    		}
    		int suitsFound = 0;
    		for (var i = 0; i < 4; i++)
    			if (countPerSuit[i] > 0)
    				suitsFound++;

    		int[] countPerGroup = new int[5];
    		// countPerGroup = { 
    		//	not used,
    		//	how many single cards,
    		// 	how many pairs,
    		//	how many groups of three,
    		//	how many groups of four }
    		int[][] rankPerGroup = new []{
    			new int[0],		// not used 
    			new int[5], 	// ranks of single cards, in descending order
    			new int[2], 	// ranks of pairs, in descending order
    			new int[1], 	// rank of group of three
    			new int[1]		// rank of group of four
    		};
    		int[] aux = new int[5];	// aux[x] => index in which we'll do the next write in rankPerGroup[x]
    		for (var r = 12; r >= 0; r--)
    		{
    			var groupSize = countPerRank[r];
    			if (groupSize == 0)
    				continue;
				countPerGroup[groupSize]++;
				rankPerGroup[groupSize][aux[groupSize]] = r;
				aux[groupSize]++;
    		}

    		HandType handType;
    		int tiebreaker;
    		if (countPerGroup[4] == 1)
    		{
    			handType = HandType.FourOfAKind;
    			tiebreaker = rankPerGroup[4][0] * 13 
    						+ rankPerGroup[1][0];
    		}
    		else if (countPerGroup[3] == 1 && countPerGroup[2] == 1)
    		{
    			handType = HandType.FullHouse;
    			tiebreaker = rankPerGroup[3][0] * 13
    						+ rankPerGroup[2][0];
    		}
    		else if (countPerGroup[3] == 1)
    		{
    			handType = HandType.ThreeOfAKind;
    			tiebreaker = rankPerGroup[3][0] * (13 * 13)
							+ rankPerGroup[1][0] * 13
    						+ rankPerGroup[1][1];
    		}
    		else if (countPerGroup[2] == 2)
    		{
    			handType = HandType.TwoPair;
    			tiebreaker = rankPerGroup[2][0] * (13 * 13)
							+ rankPerGroup[2][1] * 13
    						+ rankPerGroup[1][0];
    		}
    		else if (countPerGroup[2] == 1)
    		{
    			handType = HandType.OnePair;
    			tiebreaker = rankPerGroup[2][0] * (13 * 13 * 13)
							+ rankPerGroup[1][0] * (13 * 13)
							+ rankPerGroup[1][1] * 13
    						+ rankPerGroup[1][2];
    		}
    		// all ranks are different
    		else if (maxRank - minRank == 4)
			{
				if (suitsFound == 1 && maxRank == 12)
				{
					handType = HandType.RoyalFlush;
					tiebreaker = 0;
				}
				else if (suitsFound == 1)
				{
					handType = HandType.StraightFlush;
					tiebreaker = maxRank;
				}
				else
				{
					handType = HandType.Straight;
					tiebreaker = maxRank;
				}
			}
			else if (maxAlternateRank - minAlternateRank == 4)	// straight with A at the bottom
			{
				if (suitsFound == 1)
				{
					handType = HandType.StraightFlush;
					tiebreaker = maxAlternateRank;	// will always be 3 (card "5")
				}
				else
				{
					handType = HandType.Straight;
					tiebreaker = maxAlternateRank;	// will always be 3 (card "5")
				}	
			}
			else
			{
				if (suitsFound == 1)
					handType = HandType.Flush;
				else
					handType = HandType.HighCard;
				tiebreaker = rankPerGroup[1][0] * (13 * 13 * 13 * 13)
							+ rankPerGroup[1][1] * (13 * 13 * 13)
							+ rankPerGroup[1][2] * (13 * 13)
							+ rankPerGroup[1][3] * 13
							+ rankPerGroup[1][4];
			}
    		return new HandEvaluation { HandType = handType, Tiebreaker = (ulong)tiebreaker };
        }
    }
}