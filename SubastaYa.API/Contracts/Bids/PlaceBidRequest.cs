using System.ComponentModel.DataAnnotations;

namespace SubastaYa.API.Contracts.Bids;

public class PlaceBidRequest
{
    public decimal? Monto { get; set; }
}
