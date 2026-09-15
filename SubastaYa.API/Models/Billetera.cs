using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubastaYa.API.Models;
public sealed class Billetera
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UsuarioId { get; set; }

    public Usuario Usuario { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    public decimal SaldoTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SaldoRetenido { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SaldoDisponible { get; set; }

    [Timestamp]
    public byte[] Version { get; set; } = Array.Empty<byte>();


}
