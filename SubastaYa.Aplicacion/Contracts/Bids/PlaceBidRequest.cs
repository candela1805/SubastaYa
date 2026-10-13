using System.ComponentModel.DataAnnotations;

namespace SubastaYa.Aplicacion.Contracts.Bids;

public class PlaceBidRequest
{
    public decimal? Monto { get; set; }
}
