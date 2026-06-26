using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    public class MasterArea
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        public string NamaArea { get; set; } = string.Empty;

        [Required]
        public int PerusahaanId { get; set; }

        [Required]
        [MaxLength(50)]
        public string CreatedByNik { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string CreatedByName { get; set; } = string.Empty;

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
