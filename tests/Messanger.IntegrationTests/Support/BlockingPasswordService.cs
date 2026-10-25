using Messanger.Application.Authentication;
using Messanger.Domain;
using Microsoft.AspNetCore.Identity;

namespace Messanger.IntegrationTests.Support;

internal sealed class BlockingPasswordService : IPasswordService, IDisposable
{
    private readonly PasswordHasher<User> hasher = new();
    private readonly ManualResetEventSlim released = new(false);
    private int hashCalls;
    public int HashCalls => Volatile.Read(ref hashCalls);
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Hash(User user, string password)
    {
        Interlocked.Increment(ref hashCalls);
        Entered.TrySetResult();
        if (!released.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test credential work was not released.");
        return hasher.HashPassword(user, password);
    }
    public bool Verify(User? user, string password) => user is not null && hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
    public void Release() => released.Set();
    public void Dispose() => released.Dispose();
}
