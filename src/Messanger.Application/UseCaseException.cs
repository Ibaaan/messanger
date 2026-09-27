namespace Messanger.Application;

public sealed class UseCaseException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}