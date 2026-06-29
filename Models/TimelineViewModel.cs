using System;
using System.Collections.Generic;

namespace MBS_SAP.Models
{
    public class TimelineViewModel
    {
        public int Id { get; set; } // Just a sequence or original ID
        public string ItemType { get; set; } = string.Empty; // Hazard, Inspection, ActionPlan, SafetyTalk, P5m
        public int OriginalId { get; set; }
        
        public string Nama { get; set; } = string.Empty;
        public string Nik { get; set; } = string.Empty;
        public string? Departemen { get; set; }
        public int? PerusahaanId { get; set; }
        public string? Jabatan { get; set; }
        public string? Perusahaan { get; set; }
        
        public DateTime Tanggal { get; set; }
        public TimeSpan Waktu { get; set; }
        public DateTime CreatedAt { get; set; }
        
        public string? Area { get; set; }
        public string? Lokasi { get; set; }
        public string? Kategori { get; set; } // Jenis Inspeksi, atau Tingkat Resiko, atau Jenis Bahaya
        
        public string? Title { get; set; } // Judul/Topik
        public string? Content { get; set; } // Temuan, Keterangan, RencanaPerbaikan
        
        public string? Status { get; set; } // Open / Closed / Progres
        
        public string? TingkatResiko { get; set; } // Rendah / Sedang / Tinggi / Ekstrim (Hazard only)
        
        public string? FotoUrl { get; set; }
        public string? FotoDiriUrl { get; set; } // For SafetyTalk
        
        public int LikesCount { get; set; }
        public int CommentsCount { get; set; }
        public bool IsLikedByCurrentUser { get; set; }
        
        public List<TimelineComment> Comments { get; set; } = new List<TimelineComment>();
    }
}
