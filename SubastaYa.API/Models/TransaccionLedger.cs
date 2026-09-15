using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubastaYa.API.Models;

public sealed class TransaccionLedger
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BilleteraId { get; set; }

    public Billetera Billetera { get; set; } = null!;

    public Guid? LiquidacionSubastaId { get; set; }

    public LiquidacionSubasta? LiquidacionSubasta { get; set; }

    [MaxLength(150)]
    public string? ClaveIdempotencia { get; set; }

    public TipoMovimientoBilletera Tipo { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Monto { get; set; }

    [Required]
    [MaxLength(250)]
    public string Descripcion { get; set; } = string.Empty;

    public DateTimeOffset FechaUtc { get; set; }
        = DateTimeOffset.UtcNow;
}
