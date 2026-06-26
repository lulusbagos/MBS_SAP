using System;
using System.ComponentModel.DataAnnotations;

namespace MBS_SAP.Models
{
    public class P2hVehicle
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string NoLambung { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string JenisKendaraan { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string Merek { get; set; } = string.Empty;

        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
