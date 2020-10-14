using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace pmPoker
{
    public class WSMiddleware
    {
		private readonly RequestDelegate next;

		public WSMiddleware(RequestDelegate next)
		{
			this.next = next;
		}
		
		public async Task Invoke(HttpContext context, IWSPokerInterface wsPokerInterface)
		{
			if (context.Request.Path == "/ws")
			{
				if (context.WebSockets.IsWebSocketRequest)
				{
					WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
					await wsPokerInterface.ConsumeMessages(webSocket);
				}
				else
				{
					context.Response.StatusCode = 400;
				}
			}
			else
			{
				await next(context);
			}
		}
    }
}
