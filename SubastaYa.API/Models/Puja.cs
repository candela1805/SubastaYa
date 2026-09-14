using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubastaYa.API.Models;

public class Puja
{
    public Guid Id { get; set; }

    public Guid SubastaId { get; set; }

    public Guid UsuarioId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Monto { get; set; }

    public bool EsGanadora { get; set; }

    public DateTimeOffset FechaUtc { get; set; } = DateTimeOffset.UtcNow;
    public Subasta Subasta { get; set; } = null!;
    public Usuario Usuario { get; set; } = null!;

}
