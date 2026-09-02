using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubastaYa.API.Models
{
    public class Puja
    {
        [Key]
        public int Id { get; set; }
        public int SubastaId { get; set; }
        public int CompradorId { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal Monto { get; set; }
        public DateTime FechaPuja { get; set; }
    }
}