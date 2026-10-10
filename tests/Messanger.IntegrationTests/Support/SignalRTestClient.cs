using System.Threading.Channels;
using Messanger.Application.Chats;
using Messanger.Application.Messages;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Messanger.IntegrationTests.Support;

internal sealed class SignalRTestClient : IAsyncDisposable
{
    public HubConnection Connection { get; }
    public Channel<MessageResponse> Messages { get; } = Channel.CreateUnbounded<MessageResponse>();
    public Channel<long> RevokedChats { get; } = Channel.CreateUnbounded<long>();
    public Channel<long> ChangedChats { get; } = Channel.CreateUnbounded<long>();
    public Channel<ChatResponse> Chats { get; } = Channel.CreateUnbounded<ChatResponse>();

    private SignalRTestClient(ApiTestHost host, string token)
    {
        Connection = new HubConnectionBuilder().WithUrl("https://localhost/hubs/chat", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
            options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            options.Headers["Origin"] = ApiTestHost.AllowedOrigin;
        }).Build();
        Connection.On<MessageResponse>("ReceiveMessage", message => Messages.Writer.TryWrite(message));
        Connection.On<ChatResponse>("ChatCreated", chat => Chats.Writer.TryWrite(chat));
        Connection.On<long>("ChatAccessRevoked", chatId => RevokedChats.Writer.TryWrite(chatId));
        Connection.On<long>("ChatMembershipChanged", chatId => ChangedChats.Writer.TryWrite(chatId));
    }

    public static async Task<SignalRTestClient> ConnectAsync(ApiTestHost host, string token)
    {
        var client = new SignalRTestClient(host, token);
        try { await client.Connection.StartAsync(); return client; }
        catch { await client.DisposeAsync(); throw; }
    }

    public Task<MessageResponse> NextMessageAsync() => Messages.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}
