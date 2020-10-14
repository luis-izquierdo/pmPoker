using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using pmPoker;

namespace pmPoker.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public class AdminController : ControllerBase
    {
		private readonly IWSPokerInterface _wsPokerInterface;

        public AdminController(IWSPokerInterface wsPokerInterface)
        {
			_wsPokerInterface = wsPokerInterface;
        }

        [HttpGet]
        public string[] Connections()
		{
			return _wsPokerInterface.GetConnectedPlayers();
		}
		
		[HttpPost]
		public string StartGame()
		{
			var pokerEngine = new PokerEngine();
			var randomizerSeed = new Random().Next();
			var t = Task.Run(() => pokerEngine.RunGame(randomizerSeed, _wsPokerInterface, _wsPokerInterface.GetConnectedPlayers(), 
				100,	// initial chips per player
				() => Tuple.Create(5, 10)	// constant small and big blind bets
			));
			return "OK";
		}
    }
}
