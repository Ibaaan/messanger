using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Messanger.Security;

public sealed class SecurityBudgets : IDisposable
{
    // Bounded partition keys prevent arbitrary IPs/usernames from growing limiter state without limit.
    public const int BucketCount = 4096;
    private readonly SecurityLimitsOptions options;
    private readonly PartitionedRateLimiter<HttpContext>[] httpLimiters;
    private readonly PartitionedRateLimiter<(string Kind, string Key)> actions;
    public PartitionedRateLimiter<HttpContext> Http { get; }

    public SecurityBudgets(IOptions<SecurityLimitsOptions> settings)
    {
        options = settings.Value;
        httpLimiters = [
            PartitionedRateLimiter.Create<HttpContext, int>(context => HttpPartition(context, options.RequestsPerIp)),
            PartitionedRateLimiter.Create<HttpContext, int>(_ => options.Enabled
                ? RateLimitPartition.GetFixedWindowLimiter(0, _ => Window(options.RequestsPerServer)) : RateLimitPartition.GetNoLimiter(0)),
            PartitionedRateLimiter.Create<HttpContext, int>(context => options.Enabled &&
                context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName is "Register" or "Login" or "ChangeMyPassword"
                ? RateLimitPartition.GetConcurrencyLimiter(0, _ => new ConcurrencyLimiterOptions { PermitLimit = options.CredentialConcurrency, QueueLimit = 0 })
                : RateLimitPartition.GetNoLimiter(1))
        ];
        Http = PartitionedRateLimiter.CreateChained(httpLimiters);
        actions = PartitionedRateLimiter.Create<(string Kind, string Key), (string Kind, int Bucket)>(resource => options.Enabled
            ? RateLimitPartition.GetFixedWindowLimiter((resource.Kind, Bucket(resource.Key)), _ => Window(resource.Kind switch
            {
                "login" => options.LoginPerAccount,
                "password" => options.PasswordChangesPerUser,
                "messages" => options.MessagesPerUser,
                "hub" => options.HubCallsPerUser,
                "connections" => options.ConnectionAttemptsPerUser,
                _ => throw new InvalidOperationException("Unknown security budget.")
            })) : RateLimitPartition.GetNoLimiter(("disabled", 0)));
    }

    public RateLimitPartition<int> HttpPartition(HttpContext context, int permits) => options.Enabled
        ? RateLimitPartition.GetFixedWindowLimiter(Bucket(IpKey(context)), _ => Window(permits))
        : RateLimitPartition.GetNoLimiter(-1);
    public RateLimitLease Acquire(string kind, string key) => actions.AttemptAcquire((kind, key));
    public static string UserKey(long userId) => userId.ToString(CultureInfo.InvariantCulture);
    public static string IpKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        return (ip?.IsIPv4MappedToIPv6 == true ? ip.MapToIPv4() : ip)?.ToString() ?? "unknown";
    }
    public static int Bucket(string key) => (int)(BinaryPrimitives.ReadUInt32LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(key))) % BucketCount);
    private FixedWindowRateLimiterOptions Window(int permits) => new() { PermitLimit = permits, Window = options.Window, QueueLimit = 0 };
    public void Dispose()
    {
        Http.Dispose();
        foreach (var limiter in httpLimiters) limiter.Dispose();
        actions.Dispose();
    }
}
