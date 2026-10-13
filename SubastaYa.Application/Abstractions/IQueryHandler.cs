namespace SubastaYa.Application.Abstractions;

public interface IQueryHandler<TQuery, TResult>
{
    Task<TResult> HandleAsync(
        TQuery query,
        CancellationToken cancellationToken = default);
}
