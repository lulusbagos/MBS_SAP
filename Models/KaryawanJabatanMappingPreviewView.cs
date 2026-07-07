using System;
using System.ComponentModel.DataAnnotations;

namespace MBS_SAP.Models
{
    public class KaryawanJabatanMappingPreviewView
    {
        [Key]
        public int KaryawanId { get; set; }
        public int? PerusahaanId { get; set; }
        public int? DepartemenId { get; set; }
        public int? JabatanIdExisting { get; set; }
        public string? NamaJabatanExisting { get; set; }
        public string? KategoriPengawas { get; set; }
        public int? RJabatanId { get; set; }
        public string? KodeJabatanStandar { get; set; }
        public string? NamaJabatanStandar { get; set; }
        public string? MetodeMapping { get; set; }
        public decimal? ConfidenceScore { get; set; }
        public int? TargetInspeksi { get; set; }
        public int? TargetObservasi { get; set; }
        public int? TargetHazardReport { get; set; }
        public int? TargetCoaching { get; set; }
        public int? TargetSafetyTalk { get; set; }
    }
}
