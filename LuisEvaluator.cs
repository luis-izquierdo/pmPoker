using System;
using System.Collections.Generic;
using System.Linq;

namespace evalhand
{
    class LuisEvaluator
    {
        //static void Main(string[] args)
        //{
        //	var r = new Random();
        //	var deck = Enumerable.Range(0, 52).ToList();
        //	var cards = new PlayingCard[7];
        //	while (true)
        //	{
    	//		for (var i = 0; i < cards.Length; i++)
    	//		{
		//			var index = r.Next(deck.Count);
		//			cards[i] = new PlayingCard { Rank = deck[index] / 4, Suit = deck[index] % 4 };
		//			deck.RemoveAt(index);
    	//		}
    	//		Console.WriteLine(string.Join(" | ", cards.Select(c => $"{c.Rank} {c.Suit}")));
    	//		var e = EvalBestHand(cards);
    	//		Console.WriteLine($"{e.HandType} {e.TieBreaker}");
    	//		for (var i = 0; i < cards.Length; i++)
    	//		{
    	//			deck.Add(cards[i].Rank * 4 + cards[i].Suit);
    	//		}
        //		Console.ReadLine();
        //	}
        //}

        static HandEvaluation EvalBestHand(PlayingCard[] sevenCards)
        {
        	if (sevenCards.Length != 7)
        		throw new ArgumentException("sevenCards");
        	HandEvaluation bestEvaluation = null;

        	for (var first = 0; first < 3; first++)
        		for (var second = first + 1; second < 4; second++)
        			for (var third = second + 1; third < 5; third++)
        				for (var fourth = third + 1; fourth < 6; fourth++)
        					for (var fifth = fourth + 1; fifth < 7; fifth++)
        					{
        						//Console.WriteLine($"{first} {second} {third} {fourth} {fifth}");
        						var hand = new [] {
        							sevenCards[first],
        							sevenCards[second],
        							sevenCards[third],
        							sevenCards[fourth],
        							sevenCards[fifth]
        						};
        						var e = Eval(hand);
        						if (bestEvaluation == null || e.AbsoluteValue > bestEvaluation.AbsoluteValue)
        							bestEvaluation = e;
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
    			countPerRank[c.Rank]++;
    			countPerSuit[c.Suit]++;
    			minRank = Math.Min(minRank, c.Rank);
    			maxRank = Math.Max(maxRank, c.Rank);
    			var alternateRank = c.Rank == 12 ? -1 : c.Rank;	// same as regular rank, except for the A
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
    		int tieBreaker;
    		if (countPerGroup[4] == 1)
    		{
    			handType = HandType.FourOfAKind;
    			tieBreaker = rankPerGroup[4][0] * 13 
    						+ rankPerGroup[1][0];
    		}
    		else if (countPerGroup[3] == 1 && countPerGroup[2] == 1)
    		{
    			handType = HandType.FullHouse;
    			tieBreaker = rankPerGroup[3][0] * 13
    						+ rankPerGroup[2][0];
    		}
    		else if (countPerGroup[3] == 1)
    		{
    			handType = HandType.ThreOfAKind;
    			tieBreaker = rankPerGroup[3][0] * (13 * 13)
							+ rankPerGroup[1][0] * 13
    						+ rankPerGroup[1][1];
    		}
    		else if (countPerGroup[2] == 2)
    		{
    			handType = HandType.TwoPair;
    			tieBreaker = rankPerGroup[2][0] * (13 * 13)
							+ rankPerGroup[2][1] * 13
    						+ rankPerGroup[1][0];
    		}
    		else if (countPerGroup[2] == 1)
    		{
    			handType = HandType.Pair;
    			tieBreaker = rankPerGroup[2][0] * (13 * 13 * 13)
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
					tieBreaker = 0;
				}
				else if (suitsFound == 1)
				{
					handType = HandType.StraightFlush;
					tieBreaker = maxRank;
				}
				else
				{
					handType = HandType.Straight;
					tieBreaker = maxRank;
				}
			}
			else if (maxAlternateRank - minAlternateRank == 4)	// straight with A at the bottom
			{
				if (suitsFound == 1)
				{
					handType = HandType.StraightFlush;
					tieBreaker = maxAlternateRank;	// will always be 3 (card "5")
				}
				else
				{
					handType = HandType.Straight;
					tieBreaker = maxAlternateRank;	// will always be 3 (card "5")
				}	
			}
			else
			{
				if (suitsFound == 1)
					handType = HandType.Flush;
				else
					handType = HandType.HighCard;
				tieBreaker = rankPerGroup[1][0] * (13 * 13 * 13 * 13)
							+ rankPerGroup[1][1] * (13 * 13 * 13)
							+ rankPerGroup[1][2] * (13 * 13)
							+ rankPerGroup[1][3] * 13
							+ rankPerGroup[1][4];
			}
    		return new HandEvaluation { HandType = handType, TieBreaker = tieBreaker };
        }
    }

    enum HandType
    {
    	HighCard = 1,
    	Pair = 2,
    	TwoPair = 3,
    	ThreOfAKind = 4,
    	Straight = 5,
    	Flush = 6,
    	FullHouse = 7,
    	FourOfAKind = 8,
    	StraightFlush = 9,
    	RoyalFlush = 10
    }

    class HandEvaluation
    {
    	public HandType HandType { get; set; }
    	public int TieBreaker { get; set; }
    	public int AbsoluteValue { 
    		get 
    		{ 
    			return (int)HandType * 1000000		// tie breakers are < 13ˆ5, which is approx 370K 
    			+ TieBreaker; 
			} 
		}
    }

    struct PlayingCard
    {
    	public int Rank {get; set;}
    	public int Suit {get; set;}
    }
}
