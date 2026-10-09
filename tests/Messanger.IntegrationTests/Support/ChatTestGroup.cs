using Messanger.Application;
using Messanger.Application.Chats;
using Messanger.IntegrationTests.Api;

namespace Messanger.IntegrationTests.Support;

internal sealed record ChatTestGroup(UserResponse Owner, UserResponse Member, UserResponse Other,
    string OwnerToken, string MemberToken, string OtherToken, ChatResponse Chat)
{
    public static async Task<ChatTestGroup> CreateAsync(ApiTestHost host)
    {
        var owner = await host.RegisterAsync();
        var member = await host.RegisterAsync();
        var other = await host.RegisterAsync();
        var ownerToken = (await host.LoginAsync(owner)).AccessToken;
        var memberToken = (await host.LoginAsync(member)).AccessToken;
        var otherToken = (await host.LoginAsync(other)).AccessToken;
        var chat = await ChatApiTests.CreateAsync(host, ownerToken, [member.Id]);
        return new(owner, member, other, ownerToken, memberToken, otherToken, chat);
    }
}
