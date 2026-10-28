using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_t_app_user")]
    public class AppUser
    {
        [Key]
        [MaxLength(50)]
        [Column("nik")]
        public string Nik { get; set; } = string.Empty;

        [MaxLength(100)]
        [Column("nama")]
        public string Nama { get; set; } = string.Empty;

        [MaxLength(100)]
        [Column("departemen")]
        public string Departemen { get; set; } = string.Empty;

        [MaxLength(100)]
        [Column("perusahaan")]
        public string Perusahaan { get; set; } = string.Empty;

        [Column("id_perusahaan")]
        public int? IdPerusahaan { get; set; }

        [Column("karyawan_id")]
        public int? KaryawanId { get; set; }

        [Column("last_login")]
        public DateTime LastLogin { get; set; }

        /// <summary>Custom role override: Admin / Operator (null = use default from vw_pengguna)</summary>
        [MaxLength(50)]
        [Column("role")]
        public string? Role { get; set; }
    }
}
