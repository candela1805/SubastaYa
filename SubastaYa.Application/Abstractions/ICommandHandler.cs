namespace SubastaYa.Application.Abstractions;

public interface ICommandHandler<TCommand, TResult>
{
    Task<TResult> HandleAsync(
        TCommand command,
        CancellationToken cancellationToken = default);
}
