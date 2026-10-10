namespace Messanger.Http;

public sealed record ApiErrorDetails(string Code, string Message, string RequestId);
