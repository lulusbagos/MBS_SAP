using System.ComponentModel.DataAnnotations;

namespace MBS_SAP.Models
{
    public class PenggunaView
    {
        [Key]
        public int PenggunaId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string KataSandi { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string NamaLengkap { get; set; } = string.Empty;

        [MaxLength(150)]
        public string? Email { get; set; }

        public int PerusahaanId { get; set; }

        public int PeranId { get; set; }

        public int? DepartemenId { get; set; }

        public int? JabatanId { get; set; }

        public bool IsAktif { get; set; }
    }
}
