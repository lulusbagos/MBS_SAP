using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBS_SAP.Models
{
    [Table("tbl_m_benchmark")]
    public class MasterBenchmark
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("perusahaan_id")]
        public int? PerusahaanId { get; set; }

        [Column("area_utama")]
        public string? AreaUtama { get; set; }

        [Column("nama_benchmark")]
        public string? NamaBenchmark { get; set; }

        [Column("created_by_nik")]
        public string? CreatedByNik { get; set; }

        [Column("created_by_name")]
        public string? CreatedByName { get; set; }

        [Column("created_at")]
        public DateTime? CreatedAt { get; set; } = DateTime.Now;
    }
}
