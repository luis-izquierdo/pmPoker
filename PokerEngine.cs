using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace pmPoker
{
    class PokerEngine
    {
        private readonly IHandEvaluator evaluator;
        public PokerEngine(IHandEvaluator evaluator)
        {
            this.evaluator = evaluator;
        }
        public async Task RunGame(int cardShufflingRandomSeed, IPokerUI userInterface, 
            string[] playerIDs, int initChips, Func<int, Tuple<int,int>> getBlindBetAmounts,
            CancellationToken cancellationToken)
        {
            if (playerIDs.Length < 3)
                throw new ArgumentOutOfRangeException("Minimum = 3 players");
			userInterface.Broadcast(new {
				MessageType = MessageType.GameStarts, 
				Players = playerIDs,
				Chips = initChips
			});

            var players = playerIDs.Select(p => new PlayerInfo { PlayerID = p, Chips = initChips }).ToList();
            var random = new Random(cardShufflingRandomSeed);
            var dealerIndex = -1;
            
            for (var round = 0; players.Count > 1; round++)
            {
                foreach (var p in players)
				{
                    p.Folded = false;
					p.CurrentBet = 0;
				}

                Queue<PlayingCard> deck = ShuffleCards(random);

                // dealing cards
                dealerIndex = NextPlayerIndex(dealerIndex, players);
				userInterface.Broadcast(new {
					MessageType = MessageType.RoundStarts, 
					Dealer = players[dealerIndex].PlayerID,
				});
                foreach (var p in players)
                {
                    var card1 = deck.Dequeue();
                    var card2 = deck.Dequeue();
					userInterface.SendToSinglePlayer(p.PlayerID, new {
						MessageType = MessageType.YourCards, 
						Cards = new[] {new { Rank = card1.RankName, Suit = card1.SuitName }, new { Rank = card2.RankName, Suit = card2.SuitName }},
					});
                    p.Cards = new[] { card1, card2 };
                }

                // blind bets
                var pot = 0;
                var smallBlindIndex = NextPlayerIndex(dealerIndex, players);
                var bigBlindIndex = NextPlayerIndex(smallBlindIndex, players);
                var t = getBlindBetAmounts(round);
                var smallBlind = t.Item1;
                var bigBlind = t.Item2;
                // TODO: what if the small and/or big blind players don't have enough money for the blind bet?
                players[smallBlindIndex].Chips -= smallBlind;
                players[smallBlindIndex].CurrentBet = smallBlind;
                pot += smallBlind;
				userInterface.Broadcast(new {
					MessageType = MessageType.PlayerPlayed,
					Player = players[smallBlindIndex].PlayerID,
					PlayType = PlayType.Raise,
					PlayTypeDetail = "SmallBlind",
					BetAmount = smallBlind,
					PlayerTotalBet = players[smallBlindIndex].CurrentBet,
					PlayerChips = players[smallBlindIndex].Chips,
					Pot = pot,
				});

                players[bigBlindIndex].Chips -= bigBlind;
                players[bigBlindIndex].CurrentBet = bigBlind;
                pot += bigBlind;
				userInterface.Broadcast(new {
					MessageType = MessageType.PlayerPlayed,
					Player = players[bigBlindIndex].PlayerID,
					PlayType = PlayType.Raise,
					PlayTypeDetail = "BigBlind",
					BetAmount = bigBlind,
					PlayerTotalBet = players[bigBlindIndex].CurrentBet,
					PlayerChips = players[bigBlindIndex].Chips,
					Pot = pot,
				});

                var highestBet = bigBlind;

                var currentPlayerIndex = NextPlayerIndex(bigBlindIndex, players);
                var communityCards = new List<PlayingCard>();
                var cardsFlippedPerRound = new[] { 0, 3, 1, 1 };
                var playersInRound = players.Count; // amount of players who have not folded
                for (int i = 0; i < cardsFlippedPerRound.Length && playersInRound > 1; i++) // betting rounds
                {
					if (i > 0)
						await Task.Delay(2000);		// for UI purposes, so that users notice that a betting round ended and a new one started	
                    // flip cards (first round = no flipping, second round = 3 flipped, third and fourth = 1 flipped
                    for (int j = 0; j < cardsFlippedPerRound[i]; j++)
                    {
                        var communityCard = deck.Dequeue();
                        communityCards.Add(communityCard);
                        userInterface.Broadcast(new {
							MessageType = MessageType.CommunityCardFlipped,
							Card = new { Rank = communityCard.RankName, Suit = communityCard.SuitName }
						});
                    }

                    foreach (var p in players.Where(p => !p.Folded))
                        p.NeedsToPlay = true;

                    // make bets
                    while (playersInRound > 1 && players.Any(p => p.NeedsToPlay))
                    {						
						userInterface.Broadcast(new {
							MessageType = MessageType.WaitingForPlay,
							Player = players[currentPlayerIndex].PlayerID,
							Call = highestBet - players[currentPlayerIndex].CurrentBet,
							AllIn = players[currentPlayerIndex].Chips
						});
                        if (cancellationToken.IsCancellationRequested)
                            return;
                        PokerPlay nextPlay;
                        try
                        {
                            nextPlay = await userInterface.PlayerNextPlay(players[currentPlayerIndex].PlayerID);
                        }
                        catch (OperationCanceledException)  // this happens if the admin resets the game
                        {
                            // TODO: add logging
                            return;
                        }
                        players[currentPlayerIndex].NeedsToPlay = false;
                        if (nextPlay.Type == PokerPlayType.Fold)
                        {
                            players[currentPlayerIndex].Folded = true;
                            playersInRound--;
                            userInterface.Broadcast(new {
								MessageType = MessageType.PlayerPlayed,
								Player = players[currentPlayerIndex].PlayerID,
								PlayType = PlayType.Fold
							});
                        }
                        else
                        {
                            // validate bet
                            if (nextPlay.BetAmount > players[currentPlayerIndex].Chips)
                                throw new InvalidOperationException("Players cannot bet more chips than they have.");
                            if (players[currentPlayerIndex].CurrentBet + nextPlay.BetAmount < highestBet)
                            {
                                // this is only allowed if the player cannot cover the largest bet and is going all-in
                                if (players[currentPlayerIndex].Chips > highestBet)
                                    throw new InvalidOperationException("A player's bet must cover the largest bet unless the player is going all-in.");
                            }
                            // execute bet
                            players[currentPlayerIndex].Chips -= nextPlay.BetAmount;
                            players[currentPlayerIndex].CurrentBet += nextPlay.BetAmount;
                            pot += nextPlay.BetAmount;
							var playType = nextPlay.BetAmount == 0 ? PlayType.Check : PlayType.Call;
                            if (players[currentPlayerIndex].CurrentBet > highestBet)   // if this is a raise
                            {
								playType = PlayType.Raise;
                                highestBet = players[currentPlayerIndex].CurrentBet;
                                // everybody else who has not folded needs to play
                                foreach (var p in players.Where(p => !p.Folded && p != players[currentPlayerIndex]))
                                    p.NeedsToPlay = true;
                            }
							userInterface.Broadcast(new {
								MessageType = MessageType.PlayerPlayed,
								Player = players[currentPlayerIndex].PlayerID,
								PlayType = playType,
								BetAmount = nextPlay.BetAmount,
								PlayerTotalBet = players[currentPlayerIndex].CurrentBet,
								PlayerChips = players[currentPlayerIndex].Chips,
								Pot = pot,
							});

                        }
                        currentPlayerIndex = NextPlayerIndex(currentPlayerIndex, players);
                    }
                }

                PlayerInfo winner;
                if (playersInRound == 1)
                {
                    winner = players.First(p => !p.Folded);
                }
                else
                {
                    // showdown
					userInterface.Broadcast(new {
						MessageType = MessageType.Showdown,
						PlayerCards = players.Where(p => !p.Folded).Select(p => new{
							Player = p.PlayerID,
							Cards = new[] { 
								new { Rank = p.Cards[0].RankName, Suit = p.Cards[0].SuitName}, 
								new { Rank = p.Cards[1].RankName, Suit = p.Cards[1].SuitName}
							}
						}).ToArray()
					});
                    winner = players.Where(p => !p.Folded)
                        .OrderByDescending(p => evaluator.Evaluate(p.Cards, communityCards))
                        .First();	// TODO: deal with ties
                }

                // if the winner was all-in, we may need to give back chips to other players who bet more than the winner
                foreach (var p in players.Where(p => p.CurrentBet > winner.CurrentBet))
                {
                    p.Chips += p.CurrentBet - winner.CurrentBet;
                    pot -= p.CurrentBet - winner.CurrentBet;
                }
                winner.Chips += pot;

				var returns = players.Where(p => p.CurrentBet > winner.CurrentBet)
					.Select(p => new {
						Player = p.PlayerID, 
						ReturnedAmount = p.CurrentBet - winner.CurrentBet, 
						Chips = p.Chips})
					.ToArray();
					
                userInterface.Broadcast(new {
					MessageType = MessageType.RoundWinner,
					Player = winner.PlayerID,
					Pot = pot,
					Chips = winner.Chips,
					Returns = returns
				});
				await Task.Delay(10000);		// it's nicer for the UI if we wait here for a moment

                // remove players who ran out of chips and adjust dealerIndex if needed
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i].Chips == 0)
                    {
                        userInterface.Broadcast(new {
							MessageType = MessageType.PlayerOut,
							Player = players[i].PlayerID
						});
                        players.RemoveAt(i);
                        if (dealerIndex >= i)
                            dealerIndex--;
                        i--;
                    }
                }
            }
            userInterface.Broadcast(new {
				MessageType = MessageType.GameWinner,
				Player = players[0].PlayerID
			});
        }
		
		enum MessageType
		{
			GameStarts,
			RoundStarts,
			YourCards,
			WaitingForPlay,
			PlayerPlayed,
			CommunityCardFlipped,
			Showdown,
			RoundWinner,
			GameWinner,
			PlayerOut
		}
		
		enum PlayType
		{
			Fold,
			Check,
			Call,
			Raise
		}

        private int NextPlayerIndex(int playerIndex, List<PlayerInfo> players)
        {
            do
            { playerIndex = (playerIndex + 1) % players.Count; }
            while (players[playerIndex].Folded);
            return playerIndex;
        }

        class PlayerInfo
        {
            public string PlayerID { get; set; }
            public int Chips { get; set; }
            public PlayingCard[] Cards { get; set; }
            public int CurrentBet { get; set; }
            public bool Folded { get; set; }
            public bool NeedsToPlay { get; set; }
        }

        private Queue<PlayingCard> ShuffleCards(Random random)
        {
            var deck = new List<PlayingCard>();
            for (int i = 0; i < 4; i++)
                for (int j = 2; j <= 14; j++)
                    deck.Add(new PlayingCard { Suit = i, Rank = j });
            var randomizedDeck = new Queue<PlayingCard>();
            while (deck.Count > 0)
            {
                var nextIndex = random.Next(deck.Count);
                randomizedDeck.Enqueue(deck[nextIndex]);
                deck.RemoveAt(nextIndex);
            }
            return randomizedDeck;
        }
    }
	
    public class PlayingCard
    {
        public int Rank { get; set; }   // 11 = J, 12 = Q, 13 = K, 14 = A
        public int Suit { get; set; }   // 0 to 3
		public string RankName
		{
			get
			{
				switch(Rank)
				{
					case 11: return "J";
					case 12: return "Q";
					case 13: return "K";
					case 14: return "A";
					default: return Rank.ToString();
				}
			}
		}
		public string SuitName
		{
			get
			{
				return new[] { "spades", "hearts", "diamonds", "clubs" }[Suit];
			}
		}
		
		public ulong ToBitmask()
		{
			// Each card is assigned an integer from 0 to 51 according to the following sequence:
			// 0: 2S	(2 of spades)
			// 1: 2H	(2 of hearts)
			// 2: 2D 
			// 3: 2C 
			// 4: 3S 
			// 5: 3H
			// ...
			// 48: AS
			// 49: AH
			// 50: AD
			// 51: AC
			char[] ranks = "23456789TJQKA".ToCharArray();
			char[] suits = "SHDC".ToCharArray();
			var shift = (Rank - 2) * 4 + Suit; 
			// that integer can be represented as a ulong (64-bits) with only the n-th bit active
			return 1ul << shift;
		}
		
		public static ulong HandToBitmap(PlayingCard[] hand)
		{
			// multiple card bitmasks can be combined to represent a hand as a single 64-bit ulong
			var bitmap = 0ul;
			foreach (var p in hand)
				bitmap |= p.ToBitmask();
			return bitmap;
		}
    }

    public enum PokerPlayType
    {
        Fold,
        Bet
    }

    public class PokerPlay
    {
        public PokerPlayType Type { get; set; }
        public int BetAmount { get; set; }
    }
}
