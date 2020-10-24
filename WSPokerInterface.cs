using System;
using System.Collections.Concurrent;
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
		readonly StringEnumConverter converter = new StringEnumConverter();
		readonly ConcurrentDictionary<string, byte> connectedPlayers = new ConcurrentDictionary<string, byte>();
		// TODO: analyze thread-safety of the following
		private TaskCompletionSource<PokerPlay> nextPlay;
		private string expectingPlayFrom;
		private CancellationTokenSource engineCancellationTokenSource;
		private List<Tuple<string, byte[]>> messageLog;
		private List<TaskCompletionSource<bool>> messageAvailable;
		private readonly ILogger<WSPokerInterface> logger;
		public WSPokerInterface(ILogger<WSPokerInterface> logger)
		{
			engineCancellationTokenSource = new CancellationTokenSource();
			messageLog = new List<Tuple<string, byte[]>>();
			messageAvailable = new List<TaskCompletionSource<bool>>{new TaskCompletionSource<bool>()};
			this.logger = logger;
		}
		public void Broadcast(object message)
		{
			var messageJson = JsonConvert.SerializeObject(message, converter);
			logger.LogDebug($"Broadcast({messageJson})");
			var messageBytes = Encoding.UTF8.GetBytes(messageJson);
			messageAvailable.Add(new TaskCompletionSource<bool>());
			messageLog.Add(Tuple.Create((string)null, messageBytes));
			messageAvailable[messageLog.Count - 1].SetResult(true);
		}
		public void SendToSinglePlayer(string userName, object message)
		{
			var messageJson = JsonConvert.SerializeObject(message, converter);
			logger.LogDebug($"SendToSinglePlayer({userName}, {messageJson})");
			var messageBytes = Encoding.UTF8.GetBytes(messageJson);
			messageAvailable.Add(new TaskCompletionSource<bool>());
			messageLog.Add(Tuple.Create(userName, messageBytes));
			messageAvailable[messageLog.Count - 1].SetResult(true);
		}
		public string[] GetConnectedPlayers()
		{
			return connectedPlayers.Keys.ToArray();
		}
		
		public async Task ConsumeMessages(WebSocket webSocket)
        {
			string userName = null;
			TaskCompletionSource<bool> webSocketDisconnected = new TaskCompletionSource<bool>();
			try
			{
				var buffer = new byte[1024 * 4];
				WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), engineCancellationTokenSource.Token);
				if (result.CloseStatus.HasValue)
				{
					logger.LogError("Error receiving user name from new connection.");
					return;
				}
				userName = Encoding.UTF8.GetString(buffer, 0, result.Count).ToLower();
				connectedPlayers.TryAdd(userName, 1);
				var messagesDelivered = 0;
				if (messageLog.Count > 0)
				{
					logger.LogDebug($"User {userName} connected after game started. Replaying past messages for them.");
					// send all messages so far so that the UI gets to the current point in the game
					await webSocket.SendAsync(new ArraySegment<byte>(
							Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new{ MessageType = MessageType.ReplayStart }, converter))), 
						WebSocketMessageType.Text, true, engineCancellationTokenSource.Token);
					while (messagesDelivered < messageLog.Count)
					{
						await messageAvailable[messagesDelivered].Task;
						var targetAndMessage = messageLog[messagesDelivered];
						if (targetAndMessage.Item1 == null || targetAndMessage.Item1 == userName)
						{
							try
							{
								await webSocket.SendAsync(new ArraySegment<byte>(targetAndMessage.Item2), WebSocketMessageType.Text, true, engineCancellationTokenSource.Token);
							}
							catch (Exception ex)
							{
								logger.LogError(ex, $"Error re-delivering message to {userName}.");
							}
						}
						messagesDelivered++;
					}
					await webSocket.SendAsync(new ArraySegment<byte>(
							Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new{ MessageType = MessageType.ReplayEnd }, converter))), 
						WebSocketMessageType.Text, true, engineCancellationTokenSource.Token);
				}

				// message delivery loop
				var sendMessages = Task.Run(async() => {
					logger.LogDebug($"Starting message delivery loop for {userName}.");
					while (true)
					{
						var t = Task.WhenAny(messageAvailable[messagesDelivered].Task, webSocketDisconnected.Task);
						await t;
						// if what happened was that the socket disconnected, exit the message delivery loop
						if (t.Result == webSocketDisconnected.Task)
							break;
						// otherwise there should be a new outgoing message ready
						var targetAndMessage = messageLog[messagesDelivered];
						if (targetAndMessage.Item1 == null 			// broadcast
							|| targetAndMessage.Item1 == userName)	// specific to this user
						{
							await webSocket.SendAsync(new ArraySegment<byte>(targetAndMessage.Item2), WebSocketMessageType.Text, true, engineCancellationTokenSource.Token);
						}
						messagesDelivered++;
					}
					logger.LogDebug($"Exiting message delivery loop for {userName}.");
				});

				// message reception loop
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
							nextPlay.SetResult(new PokerPlay { Type = PokerPlayType.Fold });
						else
							nextPlay.SetResult(new PokerPlay { Type = PokerPlayType.Bet, BetAmount = int.Parse(message) });
					}
				}
				webSocketDisconnected.SetResult(true);
			}
			catch (WebSocketException ex)
			{
				// this happens if a player closes its connection without proper handshake (e.g. closing the browser)
				logger.LogError(ex, $"Error from player websocket. Player name: {userName}");
				webSocketDisconnected.SetResult(true);	// break the message delivery loop
			}
			catch (OperationCanceledException)
			{
				logger.LogInformation("Stopping player message loop because of manual cancellation.");
				try
				{
					await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Game reset.", CancellationToken.None);
				}
				catch (Exception ex2)
				{
					logger.LogError(ex2, $"Error performing websocket closing handshake for user {userName} after game reset.");
				}
			}
			if (userName != null)
				connectedPlayers.TryRemove(userName, out _);
        }
		
		public Task<PokerPlay> PlayerNextPlay(string playerID)
		{
			expectingPlayFrom = playerID;
			nextPlay = new TaskCompletionSource<PokerPlay>();
			return nextPlay.Task;
		}

		public void Reset()
		{
			if (nextPlay != null)
				nextPlay.TrySetCanceled();
			engineCancellationTokenSource.Cancel();
			foreach (var m in messageAvailable)
				m.TrySetCanceled();	// abort message sending loops

			// start clean for the next game
			engineCancellationTokenSource = new CancellationTokenSource();
			messageLog = new List<Tuple<string, byte[]>>();
			messageAvailable = new List<TaskCompletionSource<bool>>{ new TaskCompletionSource<bool>() };
		}
		public CancellationToken GetEngineCancellationToken()
		{
			return engineCancellationTokenSource.Token;
		}
    }
}
