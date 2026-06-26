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
        public string Nik { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Nama { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Departemen { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Perusahaan { get; set; } = string.Empty;

        public int? IdPerusahaan { get; set; }

        public DateTime LastLogin { get; set; }

        /// <summary>Custom role override: Admin / Operator (null = use default from vw_pengguna)</summary>
        [MaxLength(50)]
        public string? Role { get; set; }
    }
}
