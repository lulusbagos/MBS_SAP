using System;
using System.ComponentModel.DataAnnotations;

namespace MBS_SAP.Models
{
    public class DpaDriver
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        public string DriverNama { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string DriverNamaNormalized { get; set; } = string.Empty;

        public int? PerusahaanId { get; set; }

        [MaxLength(50)]
        public string? CreatedByNik { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}