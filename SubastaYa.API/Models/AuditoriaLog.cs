using System.ComponentModel.DataAnnotations;

namespace SubastaYa.API.Models
{
    public class AuditoriaLog
    {
        [Key]
        public int Id{ get; set; }
        public string Entidad{ get; set; } // Subasta, Billetera, Sistema
        public int? EntidadId{ get; set; }
        public string Accion{ get; set; }
        public int? UsuarioId{ get; set; }
        public string DatalleJson{ get; set; }
        public DateTime Fecha{ get; set; } = DateTime.UtcNow;
    }
}