using System;
using System.ComponentModel.DataAnnotations;

namespace SubastaYa.API.Models
{
    public class Subasta
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int VendedorId { get; set; }

        [Required]
        public int CategoriaId { get; set; }

        [Required]
        public string Titulo { get; set; } = string.Empty;

        public string? Descripcion { get; set; }

        public string? UrlImagen { get; set; }

        // Parámetros económicos
        public decimal PrecioBase { get; set; }
        
        public decimal IncrementoMinimo { get; set; }

        // Ventana temporal
        public DateTime FechaInicio { get; set; }
        
        public DateTime FechaFin { get; set; }

        // Posibles valores: "PROGRAMADA", "ACTIVA", "FINALIZADA", "DESIERTA"
        [Required]
        public string Estado { get; set; } = string.Empty;

        // Para Optimistic Locking (Concurrencia y Anti-sniping)
        [Timestamp]
        public byte[] Version { get; set; } = Array.Empty<byte>();
    }
}
