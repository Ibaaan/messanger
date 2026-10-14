using System.Net;
using System.Net.Http.Json;

namespace Messanger.IntegrationTests.Support;

internal sealed class SessionBrowser : IDisposable
{
    public CookieContainer Cookies { get; }
    public HttpClient Client { get; }

    private SessionBrowser(ApiTestHost host, CookieContainer cookies)
    {
        Cookies = cookies;
        Client = new HttpClient(new BrowserCookies(cookies, host.Server.CreateHandler())) { BaseAddress = new Uri("https://localhost") };
    }

    public static async Task<SessionBrowser> CreateAsync(ApiTestHost host, CookieContainer? cookies = null)
    {
        var browser = new SessionBrowser(host, cookies ?? new CookieContainer());
        using var response = await browser.Client.GetAsync("/auth/csrf");
        response.EnsureSuccessStatusCode();
        var proof = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        browser.Client.DefaultRequestHeaders.Add("X-CSRF-Token", proof.GetProperty("token").GetString());
        return browser;
    }

    public void Dispose() => Client.Dispose();

    private sealed class BrowserCookies(CookieContainer cookies, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var cookie = cookies.GetCookieHeader(request.RequestUri!);
            if (cookie.Length > 0) request.Headers.Add("Cookie", cookie);
            var response = await base.SendAsync(request, ct);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                foreach (var value in values) cookies.SetCookies(request.RequestUri!, value);
            return response;
        }
    }
}
