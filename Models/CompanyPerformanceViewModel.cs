using System;

namespace MBS_SAP.Models
{
    public class CompanyPerformanceViewModel
    {
        public int PerusahaanId { get; set; }
        public string CompanyName { get; set; } = string.Empty;

        // Metrik 1: Temuan Terbanyak (Kuantitas)
        public int TotalTemuan { get; set; }

        // Metrik 2: Close (Rasio penutupan Hazard)
        public int TotalHazard { get; set; }
        public int TotalClosedHazard { get; set; }
        
        public int TotalActionPlan { get; set; }
        public int TotalClosedActionPlan { get; set; }
        public double CloseRate { get; set; }

        // Metrik 3: Kualitas SAP (Rata-rata Rating 1-5)
        public double AvgQuality { get; set; }

        // Metrik 4: Kecepatan (Rata-rata waktu penyelesaian dalam hari)
        public double AvgSpeedDays { get; set; }

        // Kalkulasi Skor Akhir
        public int TotalTarget { get; set; }
        public double ScorePencapaian { get; set; }
        public double ScoreSkalaBeban { get; set; }
        public double ScoreCloseRate { get; set; }
        public double ScoreKualitas { get; set; }
        public double ScoreKecepatan { get; set; }
        
        public double TotalScore { get; set; }
    }
}
