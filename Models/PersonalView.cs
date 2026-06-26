using System;
using System.ComponentModel.DataAnnotations;

namespace MBS_SAP.Models
{
    public class PersonalView
    {
        [Key]
        public int IdPersonal { get; set; }

        [MaxLength(30)]
        public string? NoKtp { get; set; }

        [Required]
        [MaxLength(60)]
        public string NamaLengkap { get; set; } = string.Empty;

        [MaxLength(10)]
        public string? JenisKelamin { get; set; }

        public DateTime? TanggalLahir { get; set; }

        [MaxLength(150)]
        public string? EmailPribadi { get; set; }

        [MaxLength(30)]
        public string? Hp1 { get; set; }

        [MaxLength(500)]
        public string? Alamat { get; set; }
    }
}
