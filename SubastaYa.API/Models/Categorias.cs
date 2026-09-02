using System.ComponentModel.DataAnnotations;

namespace SubastaYa.API.Models
{
    public class Categoria
    {
        [Key]
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string UrlIcono { get; set; }
    }
}