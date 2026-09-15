using System.ComponentModel.DataAnnotations.Schema;

namespace SubastaYa.API.Models;

public sealed class LiquidacionSubasta
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SubastaId { get; set; }

    public Subasta Subasta { get; set; } = null!;

    public Guid PujaGanadoraId { get; set; }

    public Puja PujaGanadora { get; set; } = null!;

    public Guid CompradorId { get; set; }

    public Usuario Comprador { get; set; } = null!;

    public Guid VendedorId { get; set; }

    public Usuario Vendedor { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    public decimal ImporteFinal { get; set; }

    public DateTimeOffset FechaAdjudicacionUtc { get; set; }

    public DateTimeOffset FechaLiquidacionUtc { get; set; }

    public ICollection<TransaccionLedger> Movimientos { get; set; } =
        new List<TransaccionLedger>();
}
