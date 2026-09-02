using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubastaYa.API.Models
{
    public class TransaccionLedger
    {
        [Key]
        public int Id{ get; set; }
        public int BilleteraId{ get; set; }

        // Deposito, Retencion, Liberacion, Pago, Cobro
        public string Tipo { get; set; } = string.Empty;

        [Column(TypeName = "decimal(10,2)")]
        public decimal Monto{ get; set; }
        public DateTime Fecha{ get; set; }

        public int? SubastaId{ get; set; }
    }
}
