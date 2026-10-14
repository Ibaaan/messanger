using Messanger.Domain;

namespace Messanger.Application.Authentication;

public sealed record RefreshRotation(User? User, bool ReuseDetected);
