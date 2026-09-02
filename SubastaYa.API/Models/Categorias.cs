using System.ComponentModel.DataAnnotations;

namespace SubastaYa.API.Models
{
    public class Categoria
    {
        [Key]
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string UrlIcono { get; set; } = string.Empty;
    }
}
