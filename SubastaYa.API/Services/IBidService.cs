using SubastaYa.API.Contracts.Bids;

namespace SubastaYa.API.Services;

public interface IBidService
{
    Task<BidResponse> PlaceBidAsync(
        Guid subastaId,
        Guid usuarioId,
        PlaceBidRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BidHistoryResponse>> GetBidHistoryAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default);

    Task<BidRoomStateResponse> GetRoomStateAsync(
        Guid subastaId,
        Guid usuarioId,
        CancellationToken cancellationToken = default);
}
