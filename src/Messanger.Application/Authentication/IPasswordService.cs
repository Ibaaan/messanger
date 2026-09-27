using Messanger.Domain;

namespace Messanger.Application.Authentication;

public interface IPasswordService
{
    string Hash(User user, string password);
    bool Verify(User? user, string password);
}