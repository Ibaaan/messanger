namespace Messanger.Application;

// Changes are tracked in one scoped unit of work; SaveChangesAsync commits them atomically.
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}