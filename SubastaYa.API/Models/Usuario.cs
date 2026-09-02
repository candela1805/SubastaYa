using System.ComponentModel.DataAnnotations;

namespace SubastaYa.API.Models
{
    public class Usuario
    {
        [Key]
        public int Id { get; set; }
        public string Email { get; set; }
        public string Nombre { get; set; }
        public string PasswordHash { get; set; }
        public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
    }
}