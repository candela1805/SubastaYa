using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubastaYa.API.Models
{
    public class Billetera
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UsuarioId { get; set; }

        public decimal SaldoTotal { get; set; } = 0;

        public decimal SaldoRetenido { get; set; } = 0;

        // Regla de Negocio: Único dinero disponible para nuevas pujas o retiros.
        // No se guarda en la BD, EF Core lo calcula en memoria.
        [NotMapped]
        public decimal SaldoDisponible => SaldoTotal - SaldoRetenido;

        // Para Optimistic Locking (Concurrencia)
        [Timestamp]
        public byte[] Version { get; set; }
    }
}