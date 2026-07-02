using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    public class AttendanceRecord
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey(nameof(AttendanceEvent))]
        public int AttendanceEventId { get; set; }

        [Required]
        [MaxLength(80)]
        public string Nik { get; set; } = string.Empty;

        [MaxLength(180)]
        public string? Nama { get; set; }

        [MaxLength(180)]
        public string? Jabatan { get; set; }

        [MaxLength(180)]
        public string? Perusahaan { get; set; }

        public DateTime ScanAt { get; set; } = DateTime.Now;

        [MaxLength(60)]
        public string Source { get; set; } = "qr";

        public AttendanceEvent? AttendanceEvent { get; set; }
    }
}
