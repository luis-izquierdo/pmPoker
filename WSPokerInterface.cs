using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
		void Reset();
		CancellationToken GetEngineCancellationToken();
	}
	
    public class WSPokerInterface: IWSPokerInterface
    {
		readonly Dictionary<string, WebSocket> connectedSockets = new Dictionary<string, WebSocket>();
		readonly StringEnumConverter converter = new StringEnumConverter();
		// TODO: analyze thread-safety of the following
		private TaskCompletionSource<PokerPlay> tcs;
		private string expectingPlayFrom;
		private CancellationTokenSource engineCancellationTokenSource;
		private bool gameStarted;
		private List<Tuple<string, byte[]>> messageLog;
		private readonly ILogger<WSPokerInterface> logger;
		public WSPokerInterface(ILogger<WSPokerInterface> logger)
		{
			engineCancellationTokenSource = new CancellationTokenSource();
			messageLog = new List<Tuple<string, byte[]>>();
			this.logger = logger;
		}
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
				gameStarted = true;
				var messageJson = JsonConvert.SerializeObject(message, converter);
				logger.LogDebug($"Broadcast({messageJson})");
				var messageBytes = Encoding.UTF8.GetBytes(messageJson);
				messageLog.Add(Tuple.Create((string)null, messageBytes));
				foreach (var webSocket in connectedSockets.Values)
				{
					try
					{
						var t = webSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
					}
					catch (Exception) {}
				}
			}
		}
		public void SendToSinglePlayer(string userName, object message)
		{
			lock (this)
			{
				gameStarted = true;
				var messageJson = JsonConvert.SerializeObject(message, converter);
				logger.LogDebug($"SendToSinglePlayer({userName}, {messageJson})");
				var messageBytes = Encoding.UTF8.GetBytes(messageJson);
				messageLog.Add(Tuple.Create(userName, messageBytes));
				try
				{
					connectedSockets[userName].SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
				}
				catch (Exception) {}	// TODO: logging
			}
		}
		public string[] GetConnectedPlayers()
		{
			return connectedSockets.Keys.ToArray();
		}
		
		public async Task ConsumeMessages(WebSocket webSocket)
        {
			string userName = null;
			try
			{
				var buffer = new byte[1024 * 4];
				WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), engineCancellationTokenSource.Token);
				userName = Encoding.UTF8.GetString(buffer, 0, result.Count).ToLower();
				RegisterSocket(userName, webSocket);	// only at this point the player can start getting real-time messages

				if (gameStarted)
				{
					logger.LogDebug($"User {userName} connected after game started. Replaying past messages for them.");
					// send all messages so far so that the UI gets to the current point in the game
					await connectedSockets[userName].SendAsync(new ArraySegment<byte>(
							Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new{ MessageType = MessageType.ReplayStart }, converter))), 
						WebSocketMessageType.Text, true, CancellationToken.None);
					foreach (var t in messageLog.Where(t => t.Item1 == null || t.Item1 == userName))
					{
						try
						{
							await connectedSockets[userName].SendAsync(new ArraySegment<byte>(t.Item2), WebSocketMessageType.Text, true, CancellationToken.None);
						}
						catch (Exception) {}	// TODO: logging
					}
					await connectedSockets[userName].SendAsync(new ArraySegment<byte>(
							Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new{ MessageType = MessageType.ReplayEnd }, converter))), 
						WebSocketMessageType.Text, true, CancellationToken.None);
				}

				while (!result.CloseStatus.HasValue)
				{
					result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), engineCancellationTokenSource.Token);
					if (result.CloseStatus.HasValue)
					{
						break;
					}
					var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
					logger.LogDebug($"Message received from {userName}: {message}");
					if (expectingPlayFrom == userName)
					{
						if (message == "fold")
							tcs.SetResult(new PokerPlay { Type = PokerPlayType.Fold });
						else
							tcs.SetResult(new PokerPlay { Type = PokerPlayType.Bet, BetAmount = int.Parse(message) });
					}
				}
				if (webSocket.State != WebSocketState.Closed)
					await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, engineCancellationTokenSource.Token);
			}
			catch (WebSocketException ex)
			{
				logger.LogError(ex, $"Error from player websocket. Player name: {userName}");
				// this happens if a player closes its connection
				return;
			}
			catch (OperationCanceledException)
			{
				logger.LogInformation("Stopping player message loop because of manual cancellation.");
				return;
			}
			finally
			{
				if (userName != null)
					UnregisterSocket(userName);
			}
        }
		
		public Task<PokerPlay> PlayerNextPlay(string playerID)
		{
			expectingPlayFrom = playerID;
			tcs = new TaskCompletionSource<PokerPlay>();
			return tcs.Task;
		}

		public void Reset()
		{
			foreach (var s in connectedSockets.Values)
				s.CloseAsync(WebSocketCloseStatus.NormalClosure, "App reset by admin.", CancellationToken.None);
			if (tcs != null)
				tcs.TrySetCanceled();
			engineCancellationTokenSource.Cancel();
			engineCancellationTokenSource = new CancellationTokenSource();
			messageLog = new List<Tuple<string, byte[]>>();
			gameStarted = false;
		}
		public CancellationToken GetEngineCancellationToken()
		{
			return engineCancellationTokenSource.Token;
		}
    }
}
