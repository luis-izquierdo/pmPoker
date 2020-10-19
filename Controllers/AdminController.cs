using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace pmPoker.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public class AdminController : ControllerBase
    {
		private readonly IWSPokerInterface _wsPokerInterface;
		private readonly IPokerEngine pokerEngine;

        public AdminController(IWSPokerInterface wsPokerInterface, IPokerEngine pokerEngine)
        {
			_wsPokerInterface = wsPokerInterface;
			this.pokerEngine = pokerEngine;
        }

        [HttpGet]
        public string[] Connections()
		{
			return _wsPokerInterface.GetConnectedPlayers();
		}
		
		[HttpPost]
		public string StartGame()
		{
			var randomizerSeed = new Random().Next();
			var t = Task.Run(() => pokerEngine.RunGame(randomizerSeed, _wsPokerInterface, _wsPokerInterface.GetConnectedPlayers(), 
				5000,	// initial chips per player
				(round) => { 
					// small/big blind bets for each round
					if (round < 4)
						return Tuple.Create(5, 10);
					else if (round < 8)
						return Tuple.Create(10, 20);
					else if (round < 12)
						return Tuple.Create(20, 40);
					else
						return Tuple.Create(40, 80);
					},
				_wsPokerInterface.GetEngineCancellationToken()
			));
			return "OK";
		}

		[HttpPost]
		public string Reset()
		{
			_wsPokerInterface.Reset();
			return "OK";
		}
    }
}
