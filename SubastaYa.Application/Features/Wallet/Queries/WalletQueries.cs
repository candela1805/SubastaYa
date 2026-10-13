using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Features.Wallet.DTOs;

namespace SubastaYa.Application.Features.Wallet.Queries;

public sealed record GetWalletBalanceQuery(Guid UserId);
public sealed record GetWalletTransactionsQuery(Guid UserId);
public sealed record GetRetainedFundsQuery(Guid UserId);

public sealed class GetWalletBalanceQueryHandler
    : IQueryHandler<GetWalletBalanceQuery, WalletBalanceResult?>
{
    private readonly IWalletRepository _repository;
    public GetWalletBalanceQueryHandler(IWalletRepository repository) => _repository = repository;
    public Task<WalletBalanceResult?> HandleAsync(GetWalletBalanceQuery query, CancellationToken cancellationToken = default) =>
        _repository.GetBalanceAsync(query.UserId, cancellationToken);
}

public sealed class GetWalletTransactionsQueryHandler
    : IQueryHandler<GetWalletTransactionsQuery, IReadOnlyList<WalletTransactionResult>?>
{
    private readonly IWalletRepository _repository;
    public GetWalletTransactionsQueryHandler(IWalletRepository repository) => _repository = repository;
    public Task<IReadOnlyList<WalletTransactionResult>?> HandleAsync(GetWalletTransactionsQuery query, CancellationToken cancellationToken = default) =>
        _repository.GetTransactionsAsync(query.UserId, cancellationToken);
}

public sealed class GetRetainedFundsQueryHandler
    : IQueryHandler<GetRetainedFundsQuery, RetainedFundsResult?>
{
    private readonly IWalletRepository _repository;
    public GetRetainedFundsQueryHandler(IWalletRepository repository) => _repository = repository;
    public Task<RetainedFundsResult?> HandleAsync(GetRetainedFundsQuery query, CancellationToken cancellationToken = default) =>
        _repository.GetRetainedFundsAsync(query.UserId, cancellationToken);
}
