using System;
using System.Collections.Generic;
using System.Linq;
using MBS_SAP.Controllers;
using MBS_SAP.Models;

namespace MBS_SAP.Services
{
    public class ComplianceWeaknessItem
    {
        public string Title { get; set; } = string.Empty;
        public string Severity { get; set; } = "SEDANG"; // KRITIS, TINGGI, SEDANG
        public string SeverityBadgeColor { get; set; } = "#eab308";
        public string AffectedArea { get; set; } = string.Empty;
        public string MetricEvidence { get; set; } = string.Empty;
        public string RootCause { get; set; } = string.Empty;
        public string ImpactRisk { get; set; } = string.Empty;
    }

    public class CompliancePrescriptionItem
    {
        public string TargetRole { get; set; } = string.Empty; // KTT, PJO Mitra, Pengawas Operasional, Tim HSE
        public string Priority { get; set; } = "PRIORITAS 1"; // PRIORITAS 1 (Minggu Ini), PRIORITAS 2 (14 Hari), STRATEGIS
        public string PriorityBadgeColor { get; set; } = "#ef4444";
        public string Title { get; set; } = string.Empty;
        public string ActionDetail { get; set; } = string.Empty;
        public string ExpectedOutcome { get; set; } = string.Empty;
    }

    public class CompliancePillarMetric
    {
        public string PillarName { get; set; } = string.Empty;
        public string Icon { get; set; } = "bi-shield-check";
        public int Target { get; set; }
        public int Actual { get; set; }
        public int Deficit { get; set; }
        public double ComplianceRate { get; set; }
        public string Status { get; set; } = "Normal";
        public string StatusColor { get; set; } = "#10b981";
        public string DiagnosticNote { get; set; } = string.Empty;
    }

    public class RiskBlindspotAlert
    {
        public string Category { get; set; } = string.Empty;
        public string Level { get; set; } = "TINGGI"; // KRITIS, TINGGI, WASPADA
        public string LevelColor { get; set; } = "#f97316";
        public string Description { get; set; } = string.Empty;
        public string PreventiveRecommendation { get; set; } = string.Empty;
    }

    public class ContractorInterventionTarget
    {
        public string CompanyName { get; set; } = string.Empty;
        public string ParentGroup { get; set; } = string.Empty;
        public string IssueType { get; set; } = string.Empty; // "Belum Pernah Login", "Zero Submisi", "Close Rate Rendah"
        public string Detail { get; set; } = string.Empty;
        public string RecommendedIntervention { get; set; } = string.Empty;
    }

    public class SapComplianceAiResult
    {
        public int PeriodYear { get; set; }
        public int PeriodMonth { get; set; }
        public string PeriodName { get; set; } = string.Empty;
        public string ScopeDescription { get; set; } = string.Empty;
        public bool IsNewPolicy { get; set; }

        // Health Score & Grade
        public double HealthScore { get; set; }
        public string HealthGrade { get; set; } = "B";
        public string HealthBadgeColor { get; set; } = "#3b82f6";
        public string HealthStatusText { get; set; } = "Cukup Patuh";
        public string ExecutiveVerdict { get; set; } = string.Empty;
        public string ExecutiveSummary { get; set; } = string.Empty;

        // KPI Summary Counters
        public int TotalEmployeesInScope { get; set; }
        public int EmployeesWithTargetCount { get; set; }
        public double SupervisoryParticipationRate { get; set; }
        public int TotalTargetAll { get; set; }
        public int TotalActualAll { get; set; }
        public int TotalDeficitAll { get; set; }
        public double OverallSubmissionRate { get; set; }
        public double OverallClosureRate { get; set; }
        public int TotalActionPlansInScope { get; set; }
        public int ClosedActionPlansInScope { get; set; }
        public int OpenActionPlansInScope { get; set; }

        // Deep Analytical Sections
        public List<CompliancePillarMetric> PillarMetrics { get; set; } = new();
        public List<ComplianceWeaknessItem> Weaknesses { get; set; } = new();
        public List<CompliancePrescriptionItem> ActionItems { get; set; } = new();
        public List<RiskBlindspotAlert> Blindspots { get; set; } = new();
        public List<ContractorInterventionTarget> InterventionTargets { get; set; } = new();
        public List<string> PositiveHighlights { get; set; } = new();
        public string NextPeriodFocus { get; set; } = string.Empty;
    }

    public static class SapComplianceAiEngine
    {
        private static readonly string[] MonthNames = new[]
        {
            "", "Januari", "Februari", "Maret", "April", "Mei", "Juni",
            "Juli", "Agustus", "September", "Oktober", "November", "Desember"
        };

        public static SapComplianceAiResult AnalyzeCompliance(
            int year,
            int month,
            int? selectedCompanyId,
            string? selectedCompanyName,
            string? departmentName,
            List<MainconGroupComparisonViewModel>? mainconGroups,
            List<CompanyPerformanceViewModel>? topPerformance,
            List<dynamic>? neverLoggedInData)
        {
            var result = new SapComplianceAiResult
            {
                PeriodYear = year,
                PeriodMonth = month,
                PeriodName = (month >= 1 && month <= 12) ? $"{MonthNames[month]} {year}" : $"{year}",
                IsNewPolicy = (year > 2026) || (year == 2026 && month >= 9)
            };

            // 1. Determine Scope Description
            if (selectedCompanyId.HasValue && selectedCompanyId.Value > 1 && !string.IsNullOrWhiteSpace(selectedCompanyName))
            {
                result.ScopeDescription = $"{selectedCompanyName}" + (!string.IsNullOrWhiteSpace(departmentName) ? $" - Dept {departmentName}" : "");
            }
            else
            {
                result.ScopeDescription = "Site Konsolidasi PT Indexim Coalindo & Seluruh Mitra Kerja (KPP, UDU, MGE, Subkontraktor)";
            }

            // 2. Aggregate Scope Numbers
            mainconGroups ??= new List<MainconGroupComparisonViewModel>();
            topPerformance ??= new List<CompanyPerformanceViewModel>();

            // Gunakan kelompok "Total" konsolidasi jika ada, atau akumulasikan baris "Utama" & "Mitra"
            var totalGroups = mainconGroups.Where(g => g.Category == "Total").ToList();
            var primaryGroups = mainconGroups.Where(g => g.Category == "Utama").ToList();
            var mitraGroups = mainconGroups.Where(g => g.Category == "Mitra").ToList();

            int aggTargetH = 0, aggActualH = 0;
            int aggTargetI = 0, aggActualI = 0;
            int aggTargetS = 0, aggActualS = 0;
            int aggTargetO = 0, aggActualO = 0;
            int aggTargetC = 0, aggActualC = 0;
            int aggTotalEmployees = 0;
            int aggTargetEmployees = 0;
            int aggTotalAp = 0;
            int aggClosedAp = 0;

            if (totalGroups.Any())
            {
                foreach (var g in totalGroups)
                {
                    aggTargetH += g.TargetHazard;
                    aggActualH += g.ActualHazard;
                    aggTargetI += g.TargetInspeksi;
                    aggActualI += g.ActualInspeksi;
                    aggTargetS += g.TargetSafetyTalk;
                    aggActualS += g.ActualSafetyTalk;
                    aggTargetO += g.TargetObservasi;
                    aggActualO += g.ActualObservasi;
                    aggTargetC += g.TargetCoaching;
                    aggActualC += g.ActualCoaching;

                    aggTotalEmployees += g.TotalEmployees;
                    aggTargetEmployees += g.EmployeesWithTargetCount;
                    aggTotalAp += g.TotalActionPlans;
                    aggClosedAp += g.ClosedActionPlans;
                }
            }
            else if (mainconGroups.Any())
            {
                foreach (var g in mainconGroups)
                {
                    aggTargetH += g.TargetHazard;
                    aggActualH += g.ActualHazard;
                    aggTargetI += g.TargetInspeksi;
                    aggActualI += g.ActualInspeksi;
                    aggTargetS += g.TargetSafetyTalk;
                    aggActualS += g.ActualSafetyTalk;
                    aggTargetO += g.TargetObservasi;
                    aggActualO += g.ActualObservasi;
                    aggTargetC += g.TargetCoaching;
                    aggActualC += g.ActualCoaching;

                    aggTotalEmployees += g.TotalEmployees;
                    aggTargetEmployees += g.EmployeesWithTargetCount;
                    aggTotalAp += g.TotalActionPlans;
                    aggClosedAp += g.ClosedActionPlans;
                }
            }

            int totalTargetAll = aggTargetH + aggTargetI + aggTargetS + aggTargetO + aggTargetC;
            int totalActualAll = aggActualH + aggActualI + aggActualS + aggActualO + aggActualC;
            int totalDeficitAll = Math.Max(0, totalTargetAll - totalActualAll);

            double subRate = totalTargetAll > 0 ? Math.Round(Math.Min(100.0, (double)totalActualAll / totalTargetAll * 100.0), 1) : 0.0;
            double apRate = aggTotalAp > 0 ? Math.Round(Math.Min(100.0, (double)aggClosedAp / aggTotalAp * 100.0), 1) : 100.0;
            int openAp = Math.Max(0, aggTotalAp - aggClosedAp);

            result.TotalEmployeesInScope = aggTotalEmployees;
            result.EmployeesWithTargetCount = aggTargetEmployees;
            result.SupervisoryParticipationRate = aggTotalEmployees > 0 ? Math.Round((double)aggTargetEmployees / aggTotalEmployees * 100.0, 1) : 0.0;
            result.TotalTargetAll = totalTargetAll;
            result.TotalActualAll = totalActualAll;
            result.TotalDeficitAll = totalDeficitAll;
            result.OverallSubmissionRate = subRate;
            result.OverallClosureRate = apRate;
            result.TotalActionPlansInScope = aggTotalAp;
            result.ClosedActionPlansInScope = aggClosedAp;
            result.OpenActionPlansInScope = openAp;

            // 3. Health Score & Grade Synthesis
            double healthScore;
            if (result.IsNewPolicy)
            {
                // New Policy: 50% Submission + 50% Close Rate (with adjustment for supervisory participation)
                healthScore = (0.50 * subRate) + (0.50 * apRate);
            }
            else
            {
                // Historical Policy: 100% Submission
                healthScore = subRate;
            }

            // Slight penalty if participation rate is severely lagging (< 30%)
            if (result.SupervisoryParticipationRate > 0 && result.SupervisoryParticipationRate < 30.0)
            {
                healthScore = Math.Max(10.0, healthScore * 0.95);
            }

            result.HealthScore = Math.Round(Math.Min(100.0, Math.Max(0.0, healthScore)), 1);

            if (result.HealthScore >= 90.0)
            {
                result.HealthGrade = "A+";
                result.HealthBadgeColor = "#10b981"; // Emerald
                result.HealthStatusText = "SANGAT UNGGUL & PATUH TINGGI";
            }
            else if (result.HealthScore >= 78.0)
            {
                result.HealthGrade = "A";
                result.HealthBadgeColor = "#0284c7"; // Sky blue
                result.HealthStatusText = "UNGGUL & KONDISI BAIK";
            }
            else if (result.HealthScore >= 65.0)
            {
                result.HealthGrade = "B";
                result.HealthBadgeColor = "#f59e0b"; // Amber
                result.HealthStatusText = "CUKUP PATUH (BUTUH PENINGKATAN)";
            }
            else if (result.HealthScore >= 50.0)
            {
                result.HealthGrade = "C";
                result.HealthBadgeColor = "#f97316"; // Orange
                result.HealthStatusText = "PERHATIAN KHUSUS (WASPADA GAP)";
            }
            else
            {
                result.HealthGrade = "D";
                result.HealthBadgeColor = "#ef4444"; // Red
                result.HealthStatusText = "KRITIS / INTERVENSI SEGERA";
            }

            // 4. Five Pillars Breakdown
            void AddPillar(string name, string icon, int tgt, int act, string noteHigh, string noteLow)
            {
                int def = Math.Max(0, tgt - act);
                double rate = tgt > 0 ? Math.Round(Math.Min(100.0, (double)act / tgt * 100.0), 1) : 0.0;
                string status = rate >= 85.0 ? "Optimal" : (rate >= 60.0 ? "Perlu Peningkatan" : "Defisit Berat");
                string color = rate >= 85.0 ? "#10b981" : (rate >= 60.0 ? "#f59e0b" : "#ef4444");

                result.PillarMetrics.Add(new CompliancePillarMetric
                {
                    PillarName = name,
                    Icon = icon,
                    Target = tgt,
                    Actual = act,
                    Deficit = def,
                    ComplianceRate = rate,
                    Status = status,
                    StatusColor = color,
                    DiagnosticNote = rate >= 85.0 ? noteHigh : noteLow
                });
            }

            AddPillar("Hazard Report", "bi-exclamation-triangle-fill", aggTargetH, aggActualH,
                "Pelaporan bahaya K3 aktif dan memenuhi standar kepatuhan minimum.",
                $"Defisit {Math.Max(0, aggTargetH - aggActualH)} laporan bahaya. Pengawas dan pekerja belum optimal mengidentifikasi kondisi/tindakan tidak aman.");

            AddPillar("Inspeksi K3", "bi-search", aggTargetI, aggActualI,
                "Inspeksi terjadwal terlaksana dengan sangat baik di seluruh area kerja.",
                $"Inspeksi terencana defisit {Math.Max(0, aggTargetI - aggActualI)} kegiatan. Ketiadaan inspeksi berkala meningkatkan risiko blindspot struktural.");

            AddPillar("Safety Talk", "bi-megaphone-fill", aggTargetS, aggActualS,
                "Briefing keselamatan dan P5M pra-shift berjalan konsisten di lapangan.",
                $"Capaian briefing keselamatan kurang {Math.Max(0, aggTargetS - aggActualS)} sesi. Komunikasi bahaya harian ke frontliner terindikasi longgar.");

            AddPillar("Observasi K3", "bi-eye-fill", aggTargetO, aggActualO,
                "Observasi perilaku berisiko aktif dan efektif mengubah unsafe act.",
                $"Observasi K3 defisit {Math.Max(0, aggTargetO - aggActualO)} kegiatan. Pengawas operasional minim memantau perilaku kerja berisiko secara langsung.");

            AddPillar("Coaching K3", "bi-person-badge-fill", aggTargetC, aggActualC,
                "Pembinaan 1-on-1 pekerja berisiko terlaksana terarah.",
                $"Coaching defisit {Math.Max(0, aggTargetC - aggActualC)} sesi. Tindak lanjut edukasi personal terhadap pelanggaran K3 belum berjalan terstruktur.");

            // Action Plan Closure Pillar
            string apStatus = apRate >= 85.0 ? "Optimal" : (apRate >= 60.0 ? "Waspada Backlog" : "Kritis Menumpuk");
            string apColor = apRate >= 85.0 ? "#10b981" : (apRate >= 60.0 ? "#f59e0b" : "#ef4444");
            result.PillarMetrics.Add(new CompliancePillarMetric
            {
                PillarName = "Action Plan Closure",
                Icon = "bi-check2-circle",
                Target = aggTotalAp,
                Actual = aggClosedAp,
                Deficit = openAp,
                ComplianceRate = apRate,
                Status = apStatus,
                StatusColor = apColor,
                DiagnosticNote = openAp == 0
                    ? "Seluruh temuan bahaya telah tuntas ditindaklanjuti 100%."
                    : $"Terdapat {openAp} temuan bahaya berstatus Outstanding yang belum diselesaikan PIC/PJA lapangan."
            });

            // 5. DIAGNOSIS KEKURANGAN MENDALAM (Apa Kekurangannya?)
            var weaknesses = new List<ComplianceWeaknessItem>();

            // [Weakness 1] Defisit Inspeksi vs Hazard (Reactivity vs Proactivity)
            double hazardRate = aggTargetH > 0 ? (double)aggActualH / aggTargetH * 100.0 : 0.0;
            double inspRate = aggTargetI > 0 ? (double)aggActualI / aggTargetI * 100.0 : 0.0;
            if (inspRate < 70.0)
            {
                weaknesses.Add(new ComplianceWeaknessItem
                {
                    Title = "Ketimpangan Aktivitas Preventif: Capaian Inspeksi K3 Rendah",
                    Severity = inspRate < 45.0 ? "KRITIS" : "TINGGI",
                    SeverityBadgeColor = inspRate < 45.0 ? "#ef4444" : "#f97316",
                    AffectedArea = "Inspeksi Lapangan / Area Kerja",
                    MetricEvidence = $"Realisasi Inspeksi hanya {inspRate:F1}% ({aggActualI}/{aggTargetI}), mengalami defisit {Math.Max(0, aggTargetI - aggActualI)} inspeksi terencana.",
                    RootCause = "Pengawas operasional dan supervisor cenderung berfokus pada pelaporan reaktif (quick hazard) dibanding meluangkan waktu melaksanakan audit inspeksi terjadwal komprehensif pada fasilitas, alat, dan area kerja.",
                    ImpactRisk = "Kondisi tidak aman (unsafe condition) pada jalur hauling, tanggul lereng, disposal, workshop, dan kelistrikan tidak terdeteksi secara dini, berpotensi memicu kegagalan sistematis atau fatalitas."
                });
            }

            // [Weakness 2] Temuan Outstanding & Backlog Action Plan
            if (openAp > 0 && apRate < 80.0)
            {
                weaknesses.Add(new ComplianceWeaknessItem
                {
                    Title = "Keterlambatan Penutupan Temuan (Remediation Backlog Action Plan)",
                    Severity = apRate < 60.0 ? "KRITIS" : "TINGGI",
                    SeverityBadgeColor = apRate < 60.0 ? "#ef4444" : "#f97316",
                    AffectedArea = "Eksekusi Perbaikan & Closed Status AP",
                    MetricEvidence = $"Close Rate Action Plan berada di {apRate:F1}%. Sebanyak {openAp} dari {aggTotalAp} temuan bahaya masih menggantung (Open/Outstanding).",
                    RootCause = "Kurangnya kontrol follow-up dari PJO/PJA area, kendala pengadaan sparepart/material perbaikan, atau kelalaian pengawas dalam mengunggah foto bukti perbaikan fisik di platform MBS SAP.",
                    ImpactRisk = "Bahaya yang dibiarkan terbuka berhari-hari meningkatkan probabilitas kecelakaan kerja berulang pada lokasi yang sama serta menurunkan wibawa sistem keselamatan tambang."
                });
            }

            // [Weakness 3] Defisit Observasi & Coaching K3 (Frontline Behavioral Safety)
            double obsRate = aggTargetO > 0 ? (double)aggActualO / aggTargetO * 100.0 : 0.0;
            double coachRate = aggTargetC > 0 ? (double)aggActualC / aggTargetC * 100.0 : 0.0;
            if (obsRate < 60.0 || coachRate < 50.0)
            {
                weaknesses.Add(new ComplianceWeaknessItem
                {
                    Title = "Rendahnya Interaksi Pembinaan Perilaku Pekerja (Observasi & Coaching)",
                    Severity = "TINGGI",
                    SeverityBadgeColor = "#f97316",
                    AffectedArea = "Pembinaan Perilaku & Pengawasan Tatap Muka",
                    MetricEvidence = $"Observasi K3: {obsRate:F1}% (Defisit {Math.Max(0, aggTargetO - aggActualO)}), Coaching K3: {coachRate:F1}% (Defisit {Math.Max(0, aggTargetC - aggActualC)}).",
                    RootCause = "Pengawas belum menjadikan interaksi dialog 1-on-1 sebagai rutinitas wajib. Pembinaan K3 masih dianggap beban administratif sekunder dibanding target produksi harian.",
                    ImpactRisk = "Tindakan tidak aman (unsafe action) seperti jalan pintas (shortcut), ketidakpatuhan SOP, dan Golden Rules violation tidak terkoreksi langsung di front kerja."
                });
            }

            // [Weakness 4] Mitra Kerja dengan Kepatuhan Rendah / Zero Contribution
            var uncompliantSubsAll = mainconGroups.SelectMany(g => g.UncompliantChildCompanyNames).Distinct().ToList();
            if (uncompliantSubsAll.Any())
            {
                string sampleSubs = string.Join(", ", uncompliantSubsAll.Take(4));
                weaknesses.Add(new ComplianceWeaknessItem
                {
                    Title = "Kesenjangan Kepatuhan Mitra Kerja: Entitas Tanpa Submisi (Zero Activity)",
                    Severity = uncompliantSubsAll.Count >= 5 ? "KRITIS" : "SEDANG",
                    SeverityBadgeColor = uncompliantSubsAll.Count >= 5 ? "#ef4444" : "#eab308",
                    AffectedArea = "Pengawasan Kontraktor & Subkontraktor",
                    MetricEvidence = $"Ditemukan {uncompliantSubsAll.Count} perusahaan mitra yang memiliki kewajiban target namun belum menyetorkan 1 pun laporan SAP (0% realisasi) bulan ini: {sampleSubs}{(uncompliantSubsAll.Count > 4 ? "..." : "")}.",
                    RootCause = "Lemahnya monitoring internal dari Main Contractor penanggung jawab terhadap mitra kerjanya, serta belum adanya sanksi administratif berkala dari Komite K3 Tambang.",
                    ImpactRisk = "Pekerja mitra menjadi mata rantai terlemah keselamatan operasional pit dan workshop yang dapat menjadi sumber insiden fatal tambang."
                });
            }

            // [Weakness 5] Perusahaan Belum Pernah Login (Onboarding Failure)
            int neverLoginCount = 0;
            if (neverLoggedInData != null && neverLoggedInData.Any())
            {
                foreach (var group in neverLoggedInData)
                {
                    try
                    {
                        var comps = group.Companies as System.Collections.ICollection;
                        if (comps != null) neverLoginCount += comps.Count;
                    }
                    catch { }
                }
            }

            if (neverLoginCount > 0)
            {
                weaknesses.Add(new ComplianceWeaknessItem
                {
                    Title = "Kegagalan Onboarding Digital: Entitas Belum Pernah Mengakses Sistem",
                    Severity = neverLoginCount > 10 ? "KRITIS" : "SEDANG",
                    SeverityBadgeColor = neverLoginCount > 10 ? "#ef4444" : "#eab308",
                    AffectedArea = "Digital Compliance & Akun Pengguna",
                    MetricEvidence = $"Sebanyak {neverLoginCount} perusahaan kontraktor/mitra yang terdaftar memiliki target SAP sama sekali belum pernah login ke platform MBS SAP.",
                    RootCause = "PJO perusahaan mitra belum menerima kredensial akun, kurangnya sosialisasi wajib dari divisi HSE, atau rotasi personel manajemen kontraktor tanpa handover akses akun.",
                    ImpactRisk = "Seluruh aktivitas keselamatan di area entitas tersebut tidak terpantau oleh KTT dan tim inspektur tambang, menciptakan blindspot tata kelola keselamatan pertambangan."
                });
            }

            if (!weaknesses.Any())
            {
                weaknesses.Add(new ComplianceWeaknessItem
                {
                    Title = "Kepatuhan Makro Terpenuhi: Pertahankan Konsistensi Kualitas",
                    Severity = "SEDANG",
                    SeverityBadgeColor = "#10b981",
                    AffectedArea = "Continuous Safety Improvement",
                    MetricEvidence = $"Capaian keseluruhan mencapai {result.OverallSubmissionRate}% dengan tingkat penutupan temuan {result.OverallClosureRate}%.",
                    RootCause = "Seluruh pilar dasar terlaksana. Fokus berikutnya adalah mengeliminasi laporan bahaya 'formalitas semata' (target-chasing tanpa substansi teknis).",
                    ImpactRisk = "Kejenuhan (complacency) di mana tingginya angka kuantitatif laporan tidak merefleksikan penurunan bahaya nyata di lapangan tambang."
                });
            }

            result.Weaknesses = weaknesses;

            // 6. REKOMENDASI PERBAIKAN KONKRET (Apa yang Harus Diperbaiki?)
            var prescriptions = new List<CompliancePrescriptionItem>();

            // Tindakan 1: Untuk KTT & Executive Management
            prescriptions.Add(new CompliancePrescriptionItem
            {
                TargetRole = "Kepala Teknik Tambang (KTT) & Manajemen Puncak",
                Priority = "PRIORITAS 1 (Minggu Ini)",
                PriorityBadgeColor = "#ef4444",
                Title = "Penerbitan Surat Peringatan Kepatuhan & Evaluasi PJO Mitra Zero Activity",
                ActionDetail = $"1. Panggil secara khusus PJO dari {Math.Min(5, uncompliantSubsAll.Count)} mitra kerja dengan submisi terendah dan {neverLoginCount} perusahaan yang belum pernah login.\n2. Tegakkan ketentuan Kepmen ESDM 1827 K bahwa pemenuhan program K3 adalah syarat mutlak izin operasional di wilayah tambang PT Indexim Coalindo.\n3. Berikan batas waktu (grace period) 7 hari kerja untuk aktivasi akun dan pencapaian minimal 50% target mingguan.",
                ExpectedOutcome = "100% aktivasi entitas mitra kerja dan pengentasan zero-contribution dalam kurun 7 hari kalender."
            });

            // Tindakan 2: Untuk PJO Mitra & Main Contractor
            prescriptions.Add(new CompliancePrescriptionItem
            {
                TargetRole = "Para Penanggung Jawab Operasional (PJO) Kontraktor",
                Priority = "PRIORITAS 1 (Minggu Ini)",
                PriorityBadgeColor = "#ef4444",
                Title = "Pembersihan Backlog Action Plan Temuan Bahaya (Target 0 Open Backlog)",
                ActionDetail = $"1. Bentuk Taskforce Khusus Penutupan Temuan untuk mengeksekusi {openAp} Action Plan berstatus Outstanding.\n2. Verifikasi fisik di lapangan: pastikan tindakan korektif bukan sekadar janji, tetapi telah terpasang perbaikan fisik nyata (rambu, tanggul, kabel, APAR, pelindung mesin).\n3. Wajibkan pengawas pengunggah menginput foto bukti perbaikan (FotoPerbaikan) di sistem untuk auto-closure.",
                ExpectedOutcome = $"Tingkat Close Rate Action Plan melonjak di atas 90% dalam tempo 10 hari kerja."
            });

            // Tindakan 3: Untuk Pengawas Operasional & Supervisor
            prescriptions.Add(new CompliancePrescriptionItem
            {
                TargetRole = "Pengawas Operasional (Frontline Supervisors & Foremen)",
                Priority = "PRIORITAS 2 (14 Hari)",
                PriorityBadgeColor = "#f97316",
                Title = "Penjadwalan Wajib Inspeksi Terencana & Pembinaan Tatap Muka",
                ActionDetail = $"1. Alokasikan 'Golden Safety Hour' (misal: 08.00-09.00 atau 14.00-15.00) setiap hari khusus untuk inspeksi terencana dan observasi K3 tanpa terganggu urusan administrasi.\n2. Bagi pengawas yang belum mencapai target inspeksi ({aggActualI}/{aggTargetI}), jadwalkan inspeksi bersama (joint inspection) lintas area pit dan hauling.\n3. Setiap tindakan tidak aman wajib ditindaklanjuti dengan 1 sesi Coaching K3 tercatat di sistem.",
                ExpectedOutcome = $"Defisit inspeksi ({Math.Max(0, aggTargetI - aggActualI)} kegiatan) terpangkas minimal 80% pada evaluasi tengah bulan."
            });

            // Tindakan 4: Untuk Divisi HSE / Safety Officer
            prescriptions.Add(new CompliancePrescriptionItem
            {
                TargetRole = "Departemen HSE & Safety Audit Team",
                Priority = "STRATEGIS (Bulanan)",
                PriorityBadgeColor = "#3b82f6",
                Title = "Audit Mutu Substansi Laporan & Monitoring Dashboard Real-Time",
                ActionDetail = $"1. Terapkan audit sampling berkala menggunakan fitur AI SAP Quality Audit untuk menyaring laporan bernilai rendah (laporan formalitas 'Aman/Nihil').\n2. Pasang display rekap mingguan kepatuhan SAP di pintu masuk mess dan workshop utama untuk membangkitkan akuntabilitas kompetitif antar departemen dan mitra.\n3. Berikan safety reward kepada 3 perusahaan mitra terbaik berdasarkan skor 4 pilar.",
                ExpectedOutcome = "Rata-rata rating mutu SAP meningkat ke angka 4.5/5.0 dan hilangnya laporan formalitas tanpa temuan substansial."
            });

            result.ActionItems = prescriptions;

            // 7. RADAR RISIKO & AREA BLINDSPOT
            var blindspots = new List<RiskBlindspotAlert>();

            if (inspRate < 60.0)
            {
                blindspots.Add(new RiskBlindspotAlert
                {
                    Category = "Geoteknik & Kestabilan Pit / Disposal",
                    Level = "KRITIS",
                    LevelColor = "#ef4444",
                    Description = "Rendahnya inspeksi berkala di area pit dan disposal dapat menyebabkan retakan lereng (tension crack) atau tanggul kritis tidak terdeteksi sebelum terjadinya slumping/longsor.",
                    PreventiveRecommendation = "Wajibkan inspeksi geoteknik harian sebelum alat beroperasi dan laporkan kondisi tanggul penahan standar min 3/4 ban unit terbesar."
                });
            }

            if (openAp > 20)
            {
                blindspots.Add(new RiskBlindspotAlert
                {
                    Category = "Hazard Exposure Berulang pada Jalur Hauling & Workshop",
                    Level = "TINGGI",
                    LevelColor = "#f97316",
                    Description = $"Terdapat {openAp} bahaya yang belum diselesaikan secara tuntas. Pengoperasian unit di sekitar bahaya yang belum diperbaiki meningkatkan risiko fatigue, insiden ban meledak, atau tabrakan unit.",
                    PreventiveRecommendation = "Terapkan Stop Work Authority (SWA) atau isolasi Red Tag pada alat/fasilitas bermasalah hingga PIC menuntaskan action plan."
                });
            }

            if (uncompliantSubsAll.Count > 0)
            {
                blindspots.Add(new RiskBlindspotAlert
                {
                    Category = "Tata Kelola Keselamatan Subkontraktor",
                    Level = "WASPADA",
                    LevelColor = "#eab308",
                    Description = $"{uncompliantSubsAll.Count} entitas mitra belum terpantau perilakunya. Ketidakhadiran laporan mengaburkan risiko bahaya pada pekerjaan dengan risiko tinggi (hot work, lifting, blasting).",
                    PreventiveRecommendation = "Lakukan inspeksi mendadak (sidak) HSE ke workshop dan lokasi kerja mitra yang bersangkutan."
                });
            }

            result.Blindspots = blindspots;

            // 8. CONTRACTOR INTERVENTION TARGETS
            var targets = new List<ContractorInterventionTarget>();

            // Masukkan uncompliant subcons
            foreach (var sub in uncompliantSubsAll.Take(6))
            {
                targets.Add(new ContractorInterventionTarget
                {
                    CompanyName = sub,
                    ParentGroup = "Mitra Kerja Terkait",
                    IssueType = "Zero Submisi (0% Capaian)",
                    Detail = "Memiliki kewajiban target laporan SAP namun belum mengirimkan laporan sama sekali periode ini.",
                    RecommendedIntervention = "Panggil PJO entitas untuk klarifikasi kendala teknis dan berikan surat peringatan pertama."
                });
            }

            // Masukkan sample never logged in
            if (neverLoggedInData != null)
            {
                foreach (var group in neverLoggedInData)
                {
                    try
                    {
                        string pName = group.ParentName ?? "Maincon";
                        var comps = group.Companies as IEnumerable<dynamic>;
                        if (comps != null)
                        {
                            foreach (var c in comps)
                            {
                                if (targets.Count >= 10) break;
                                string cName = c.NamaPerusahaan ?? "Perusahaan";
                                targets.Add(new ContractorInterventionTarget
                                {
                                    CompanyName = cName,
                                    ParentGroup = pName,
                                    IssueType = "Belum Pernah Login",
                                    Detail = "Akun telah terdaftar dengan kewajiban target, namun tidak pernah diakses sejak pembuatan.",
                                    RecommendedIntervention = "Hubungi PIC IT / HSE Maincon untuk reset sandi dan sesi onboarding wajib."
                                });
                            }
                        }
                    }
                    catch { }
                }
            }

            result.InterventionTargets = targets;

            // 9. POSITIVE HIGHLIGHTS (Kekuatan & Hal Positif)
            var positives = new List<string>();
            if (subRate >= 70.0)
            {
                positives.Add($"Partisipasi pelaporan SAP makro cukup solid dengan total submisi aktual mencapai {totalActualAll:N0} laporan ({subRate}% dari target).");
            }
            if (apRate >= 80.0)
            {
                positives.Add($"Komitmen penutupan temuan action plan sangat baik ({apRate}% Closed), menunjukkan responsivitas perbaikan fisik yang tanggap.");
            }
            if (hazardRate >= 85.0)
            {
                positives.Add("Kesadaran pelaporan Hazard Report tinggi, membuktikan keterbukaan pekerja dalam menyampaikan potensi bahaya di lingkungan tambang.");
            }
            if (topPerformance.Any())
            {
                var topComp = topPerformance.First();
                positives.Add($"Apresiasi khusus untuk **{topComp.CompanyName}** sebagai performa terbaik peringkat 1 dengan total skor {Math.Round(topComp.TotalScore, 1)} poin.");
            }
            if (!positives.Any())
            {
                positives.Add("Sistem pencatatan digital MBS SAP telah aktif merekam seluruh data kinerja K3 secara transparan dan terintegrasi.");
            }
            result.PositiveHighlights = positives;

            // 10. EXECUTIVE VERDICT & SUMMARY GENERATION
            string verdict;
            if (result.HealthScore >= 85.0)
            {
                verdict = $"Kinerja Kepatuhan SAP Periode {result.PeriodName} berada dalam Kondisi Prima ({result.HealthScore} Poin / Grade {result.HealthGrade}). Keseimbangan antara pelaporan dan penutupan bahaya terjaga dengan sangat baik.";
            }
            else if (result.HealthScore >= 70.0)
            {
                verdict = $"Kinerja Kepatuhan SAP Periode {result.PeriodName} Tergolong Baik ({result.HealthScore} Poin / Grade {result.HealthGrade}), namun membutuhkan intervensi prioritas pada pilar inspeksi dan penuntasan {openAp} Action Plan berstatus Open.";
            }
            else if (result.HealthScore >= 50.0)
            {
                verdict = $"Kinerja Kepatuhan SAP Periode {result.PeriodName} Masuk Kategori Waspada ({result.HealthScore} Poin / Grade {result.HealthGrade}). Terdapat defisit {totalDeficitAll:N0} laporan dan gap kepatuhan yang cukup lebar pada sektor kontraktor mitra kerja.";
            }
            else
            {
                verdict = $"PERINGATAN KRITIS: Kinerja Kepatuhan SAP Periode {result.PeriodName} Berada di Zona Merah ({result.HealthScore} Poin / Grade {result.HealthGrade}). KTT dan para PJO wajib segera menggelar Rapat Tanggap Darurat K3.";
            }
            result.ExecutiveVerdict = verdict;

            string policyStr = result.IsNewPolicy 
                ? "Formula Penilaian Kebijakan Baru Terbobot 50:50 (50% Submisi Karyawan + 50% Beban Penutupan Action Plan)" 
                : "Formula Penilaian Historis (100% Submisi SAP)";

            result.ExecutiveSummary =
                $"Berdasarkan audit komputasi cerdas AI terhadap data kepatuhan SAP periode **{result.PeriodName}** untuk cakupan *{result.ScopeDescription}*, " +
                $"sistem mencatat realisasi total sebanyak **{totalActualAll:N0} laporan** dari total target **{totalTargetAll:N0} laporan** ({subRate}% capaian). " +
                $"Pada aspek resolusi bahaya, tercatat **{aggClosedAp:N0} temuan** telah diselesaikan dari total **{aggTotalAp:N0} Action Plan**, menyisakan **{openAp} temuan terbuka** ({apRate}% Close Rate). " +
                $"Secara keseluruhan, Health Index kepatuhan berada di angka **{result.HealthScore}/100 ({result.HealthGrade} - {result.HealthStatusText})** berdasarkan {policyStr}. " +
                $"Titik kritis yang paling mendesak untuk segera diintervensi oleh manajemen adalah perbaikan gap pada aktivitas inspeksi terencana, eliminasi {uncompliantSubsAll.Count} entitas mitra zero-activity, dan percepatan verifikasi bukti fisik perbaikan untuk menutup seluruh backlog Action Plan sebelum penutupan buku periode berjalan.";

            result.NextPeriodFocus =
                $"Fokus Utama Bulan Depan: Peningkatan rasio inspeksi terencana minimal 85%, mewajibkan 100% login mitra kerja pada minggu pertama, dan mempertahankan zero backlog action plan berumur > 14 hari.";

            return result;
        }
    }
}
