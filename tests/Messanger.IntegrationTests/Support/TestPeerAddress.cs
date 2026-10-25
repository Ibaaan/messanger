using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Messanger.IntegrationTests.Support;

// TestServer has no TCP peer. This startup filter simulates the transport-provided address only in security fixtures.
internal sealed class TestPeerAddress : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            if (IPAddress.TryParse(context.Request.Headers["X-Test-Peer-IP"], out var peer)) context.Connection.RemoteIpAddress = peer;
            context.Request.Headers.Remove("X-Test-Peer-IP");
            await continuation(context);
        });
        next(app);
    };
}
