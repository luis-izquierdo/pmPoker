using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace pmPoker
{
	public interface IPokerUI
    {
        Task<PokerPlay> PlayerNextPlay(string playerID);
        void Broadcast(object info);
        void SendToSinglePlayer(string playerID, object info);
    }

	public interface IWSPokerInterface: IPokerUI
	{
		string[] GetConnectedPlayers();
		Task ConsumeMessages(WebSocket webSocket);
	}
	
    public class WSPokerInterface: IWSPokerInterface
    {
		readonly Dictionary<string, WebSocket> connectedSockets = new Dictionary<string, WebSocket>();
		readonly StringEnumConverter converter = new StringEnumConverter();
		private void RegisterSocket(string userName, WebSocket socket)
		{
			lock (this)
			{
				connectedSockets[userName] = socket;
			}
		}
		private void UnregisterSocket(string userName)
		{
			lock (this)
			{
				connectedSockets.Remove(userName);
			}
		}
		public void Broadcast(object message)
		{
			lock (this)
			{
				var buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message, converter));
				foreach (var webSocket in connectedSockets.Values)
				{
					try
					{
						var t = webSocket.SendAsync(new ArraySegment<byte>(buffer, 0, buffer.Length), WebSocketMessageType.Text, true, CancellationToken.None);
					}
					catch (Exception) {}
				}
			}
		}
		public void SendToSinglePlayer(string userName, object message)
		{
			lock (this)
			{
				try
				{
					var buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message, converter));
					connectedSockets[userName].SendAsync(new ArraySegment<byte>(buffer, 0, buffer.Length), WebSocketMessageType.Text, true, CancellationToken.None);
				}
				catch (Exception) {}
			}
		}
		public string[] GetConnectedPlayers()
		{
			return connectedSockets.Keys.ToArray();
		}
		
		public async Task ConsumeMessages(WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];
            WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
			var userName = Encoding.UTF8.GetString(buffer, 0, result.Count);
			RegisterSocket(userName, webSocket);
            while (!result.CloseStatus.HasValue)
            {
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
				if (result.CloseStatus.HasValue)
				{
					break;
				}
				if (expectingPlayFrom == userName)
				{
					var play = Encoding.UTF8.GetString(buffer, 0, result.Count);
					if (play == "fold")
						tcs.SetResult(new PokerPlay { Type = PokerPlayType.Fold });
					else
						tcs.SetResult(new PokerPlay { Type = PokerPlayType.Bet, BetAmount = int.Parse(play) });
				}
            }
            await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
			UnregisterSocket(userName);
        }
		
		// TODO: analyze thread-safety of the following
		private TaskCompletionSource<PokerPlay> tcs;
		private string expectingPlayFrom;
		
		public Task<PokerPlay> PlayerNextPlay(string playerID)
		{
			expectingPlayFrom = playerID;
			tcs = new TaskCompletionSource<PokerPlay>();
			return tcs.Task;
		}
    }
}
