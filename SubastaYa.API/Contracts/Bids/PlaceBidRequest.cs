using System.ComponentModel.DataAnnotations;

namespace SubastaYa.API.Contracts.Bids;

public class PlaceBidRequest
{
    [Range(1, double.MaxValue, ErrorMessage = "El monto debe ser mayor a 0.")]
    public decimal Monto { get; set; }
}
