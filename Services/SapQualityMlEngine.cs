using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MBS_SAP.Models;

namespace MBS_SAP.Services
{
    public class HazardAiAnalysisResult
    {
        public string PriorityLevel { get; set; } = "MEDIUM"; // CRITICAL, HIGH, MEDIUM, LOW
        public string PriorityLabel { get; set; } = "MEDIUM - Prioritas Sedang";
        public string PriorityBadgeColor { get; set; } = "#eab308";
        public string PriorityBadgeText { get; set; } = "MEDIUM (Max 7 Hari)";
        public int RecommendedDays { get; set; } = 7;
        public DateTime RecommendedDeadline { get; set; }
        public string RecommendedDueDateText { get; set; } = "7 Hari";
        public string DueDateFormatted { get; set; } = "";
        public string DueDateStatus { get; set; } = "Aktif"; // Aktif, Hari Ini, Overdue, Closed
        public string DueDateStatusColor { get; set; } = "#10b981";
        public int DaysRemainingOrOverdue { get; set; } = 0;
        public string RiskCategory { get; set; } = "Sedang";
        public string AiReasoning { get; set; } = "";
        public string ActionRecommendation { get; set; } = "";
        public int QualityScore { get; set; } = 4;
        public string QualityScoreNotes { get; set; } = "";
        public List<string> TriggeredKeywords { get; set; } = new List<string>();
        public bool IsGoldenRuleViolation { get; set; } = false;
        public string GoldenRuleTopic { get; set; } = "";
        public double RiskScore { get; set; } = 0;
    }

    public class SapQualityMlEngine
    {
        private static readonly HashSet<string> TargetChasingKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "aman", "oke", "ok", "sip", "tidak ada", "no", "clean", "tidak ada temuan", "normal", "ready", "bagus", "baik", "kondisi aman", "nihil", "tidakada", "semua aman", "standar", "sudah oke"
        };

        private static readonly string[] NegationPhrases = new[]
        {
            "tidak ada", "tidak terdapat", "tidak ditemukan", "nihil", "bukan", 
            "sudah diperbaiki", "telah diperbaiki", "sudah selesai", "telah selesai", 
            "sudah standar", "kondisi aman", "sudah diganti", "telah diganti", "telah diatasi", "sudah dibersihkan"
        };

        // --- HAZARD RULES DEFINITION ---

        // 1. Level 1 - CRITICAL: 1x24 Hours (Immediate Closure / Fatalities Prevention)
        private static readonly (string Topic, string[] Keywords, string Action, string Reason, int SeverityScore)[] CriticalHazardRules = new[]
        {
            (
                "Geoteknik & Kestabilan Lereng / Tanggul",
                new[] { 
                    "tanggul kritis", "tanggul jebol", "tanggul retak", "tanggul rendah", "tanggul rusak", "tanggul kurang", "tanggul tidak standar", 
                    "retakan lereng", "crack lereng", "tension crack", "longsor", "sliding", "crest pit amblas", "crest amblas", "toe pit", 
                    "disposal jebol", "disposal amblas", "amblas parah", "ambles parah", "tanpa tanggul", "batu gantung", "hanging rock", 
                    "mud rush", "slumping lereng", "kemiringan lereng curam" 
                },
                "1. Segera pasang safety line/demarkasi dan hentikan pergerakan unit/pekerja di area terdampak (Stop Work Authority).\n2. Kerahkan alat berat (Dozer/Grader) untuk rekondisi tanggul penahan standar min 3/4 tinggi ban unit terbesar.\n3. Lakukan verifikasi kestabilan oleh tim Geoteknik & PJA sebelum jalur/area dibuka kembali.",
                "Terdeteksi risiko keruntuhan lereng, kegagalan tanggul penahan, atau batu gantung di area tambang/hauling yang berpotensi fatalitas unit terperosok ke jurang.",
                95
            ),
            (
                "Keselamatan Unit Bergerak & Golden Rules Pengemudi",
                new[] { 
                    "rem blong", "rem kurang pakem", "rem tidak pakem", "brake failure", "steering macet", "steering rusak", "kemudi rusak", "steering loss", 
                    "ban meledak", "tyre burst", "ban robek parah", "baut roda patah", "baut roda kendor", "seatbelt rusak", "seatbelt tidak fungsi", 
                    "tanpa seatbelt", "tidak pakai seatbelt", "fatigue", "mengantuk berat", "tertidur", "sopir ngantuk", "driver ngantuk", "microsleep", 
                    "tabrakan", "kabin hancur", "blind spot parah", "wiper mati saat hujan lebat", "overspeed parah", "ngebut di pit" 
                },
                "1. Segera lakukan penarikan (Grounded/Out of Service) unit dari operasional dan pasang Red Tag (Do Not Operate).\n2. Laporkan ke Workshop/Mekanik untuk investigasi sistem rem/kemudi/ban dan lakukan perbaikan darurat.\n3. Istirahatkan segera operator jika terindikasi fatigue/mengantuk berat, lakukan tes kebugaran & rotasi driver.",
                "Terdeteksi kegagalan sistem kritis pada unit bergerak atau pelanggaran Golden Rules (fatigue/seatbelt/overspeed) yang berisiko tabrakan fatal.",
                90
            ),
            (
                "Listrik Tegangan Tinggi & Bahaya Kebakaran Aktif",
                new[] { 
                    "kabel terkelupas", "kabel telanjang", "kabel terbuka", "tegangan tinggi", "high voltage", "panel terbakar", "korsleting", "korslet", 
                    "short circuit", "percikan api", "bocor solar dekat exhaust", "bocor solar knalpot", "kebocoran bbm panas", "fuel leak exhaust", 
                    "kebakaran", "apar kosong", "apar expired saat hot work", "hot work tanpa apar", "tanpa loto", "loto tidak dipasang" 
                },
                "1. Matikan breaker utama & lakukan isolasi energi (LOTO - Lockout/Tagout) seketika.\n2. Sediakan APAR siap pakai dan bersihkan ceceran bahan bakar yang berdekatan dengan sumber panas/exhaust.\n3. Teknisi elektrikal berwenang wajib mereparasi isolasi kabel dan memverifikasi nol energi sebelum power dinyalakan kembali.",
                "Terdeteksi potensi bahaya sengatan listrik tegangan tinggi (electrocution) atau bahaya kebakaran aktif pada fasilitas operasional.",
                90
            ),
            (
                "Bekerja di Ketinggian & Ruang Terbatas (Confined Space)",
                new[] { 
                    "ketinggian tanpa", "tanpa harness", "harness tidak", "tanpa safety belt", "tanpa lifeline", "scaffolding rubuh", "scaffolding miring", 
                    "scaffolding tidak standar", "confined space tanpa", "ruang terbatas tanpa", "gas beracun", "h2s", "co2 tinggi", "ch4", 
                    "kurang oksigen", "tanpa gas detector", "manhole terbuka tanpa barikade", "lubang sump terbuka" 
                },
                "1. Terapkan Stop Work Authority (SWA) dan evakuasi pekerja dari area kerja berbahaya.\n2. Wajibkan pemasangan anchor point kokoh dan pemakaian Full Body Harness 100% tie-off untuk pekerjaan ketinggian.\n3. Lakukan gas test atmosfer & pasang ventilasi paksa (blower) serta stand-by person sebelum memasuki ruang terbatas.",
                "Terdeteksi potensi jatuh dari ketinggian atau asfiksia/keracunan gas beracun di ruang terbatas yang merupakan penyebab utama fatalitas tambang.",
                88
            ),
            (
                "Area Peledakan (Blasting) & Bahaya Tenggelam (Sump Pit)",
                new[] { 
                    "peledakan", "blasting", "misfire", "batu terbang", "fly rock", "masuk area blasting", "radius blasting", "gudang handak", 
                    "tenggelam", "sump meluap", "air pit naik cepat", "pompa pit tenggelam", "arus deras pit" 
                },
                "1. Kosongkan dan sterilkan seluruh radius bahaya peledakan dan koordinasikan dengan KJL (Kepala Juru Ledak).\n2. Pasang barikade pada bibir sump pit dan evakuasi personel/unit dari genangan kritis.\n3. Pastikan komunikasi radio dua arah aktif sebelum aktivitas peledakan/operasional dilanjutkan.",
                "Terdeteksi potensi bahaya peledakan atau bahaya air mendalam di area pit yang membutuhkan respon tanggap darurat.",
                88
            )
        };

        // 2. Level 2 - HIGH: 3 Days (Major Operational Safety Hazards)
        private static readonly (string Topic, string[] Keywords, string Action, string Reason, int SeverityScore)[] HighHazardRules = new[]
        {
            (
                "Jalan Hauling & Manajemen Lalu Lintas Tambang",
                new[] { 
                    "jalan berlubang besar", "pothole dalam", "jalan berlubang", "pothole", "jalan amblas", "jalan amblas sedang", "jalan licin parah", "jalan licin", 
                    "jalan bergelombang parah", "jalan bergelombang", "rambu roboh", "rambu rusak", "rambu stop roboh", "rambu stop rusak", "rambu prioritas rusak", 
                    "persimpangan tanpa rambu", "intersection", "lampu unit mati", "lampu kerja mati", "rotary mati", "buggy whip patah", "buggy whip", 
                    "radio mati", "radio rusak", "spion pecah", "klakson mati" 
                },
                "1. Pasang rambu peringatan dini dan safety cone pada titik jalan hauling yang rusak/berlubang.\n2. Jadwalkan Road Maintenance (Grader, Water Truck & Compactor) untuk grading & perbaikan permukaan jalan.\n3. Unit dengan lampu kerja/rotary/radio rusak dilarang beroperasi shift malam hingga diperbaiki.",
                "Kondisi jalan hauling yang rusak atau perlengkapan keselamatan unit yang tidak lengkap meningkatkan risiko insiden tabrakan dan terguling.",
                65
            ),
            (
                "Mesin, Alat Angkat & Guarding Mekanikal",
                new[] { 
                    "guarding terbuka", "pelindung mesin lepas", "safety guard lepas", "conveyor pinch point", "emergency stop mati", "pull cord putus", 
                    "pull cord conveyor", "sling rantas", "webbing sling sobek", "shackle aus", "crane", "rigging tidak standar", "hydraulic bocor", 
                    "hose bocor", "hose hidrolik pecah", "dongkrak bocor" 
                },
                "1. Pasang barikade di sekitar komponen mesin berputar dan pasang kembali safety guard.\n2. Karantina/musnahkan lifting gear (sling/shackle) yang telah aus atau sobek.\n3. Ganti hose hydraulic yang bocor sebelum tekanan hidrolik hilang mendadak.",
                "Bahaya mekanikal pada mesin berputar atau alat angkat dapat menyebabkan cidera berat (terjepit/tertimpa beban material).",
                62
            ),
            (
                "Tumpahan B3, Ceceran Oli & Akses Kerja Workshop",
                new[] { 
                    "tumpahan bbm", "tumpahan oli", "ceceran oli banyak", "ceceran oli", "drum b3 bocor", "drum bocor", "oli ke tanah", "solar tumpah", 
                    "b3 tidak ada label", "msds tidak ada", "eyewash rusak", "tangga kerja licin", "tangga licin", "lantai berminyak", "handrail patah", 
                    "handrail rusak", "lubang tanpa penutup", "manhole terbuka" 
                },
                "1. Gunakan oil spill kit dan serbuk gergaji untuk membersihkan tumpahan agar tidak mencemari lingkungan.\n2. Bersihkan lantai kerja dengan degreaser dan pasang penutup/handrail kokoh pada lubang/tangga.\n3. Berikan label simbol B3 yang jelas dan perbaiki fasilitas eyewash darurat.",
                "Tumpahan bahan kimia/oli berisiko menyebabkan pekerja tergelincir serta pencemaran lingkungan tambang.",
                60
            )
        };

        // 3. Level 3 - MEDIUM: 7 Days (Housekeeping, Lighting & Standard Deviations)
        private static readonly (string Topic, string[] Keywords, string Action, string Reason, int SeverityScore)[] MediumHazardRules = new[]
        {
            (
                "Housekeeping, Penerangan & Penataan Area Kerja",
                new[] { 
                    "housekeeping", "kotor", "berantakan", "tidak rapi", "material berserakan", "tumpukan barang", "jalur pedestrian terhalang", "jalur pedestrian", 
                    "sampah menumpuk", "sampah berserakan", "lampu redup", "penerangan kurang", "lampu mati di kantor", "ac bocor", "exhaust fan mati", 
                    "tempat sampah b3 penuh", "oil trap kotor", "sediment trap dangkal", "helm pudar", "helm retak", "sepatu sobek", "sarung tangan kotor", 
                    "kacamata baret", "rambu pudar", "stiker reflektor pudar" 
                },
                "1. Lakukan program 5R/5S (Ringkas, Rapi, Resik, Rawat, Rajin) pada area kerja dan gudang.\n2. Ganti lampu penerangan yang redup dan rapikan jalur pejalan kaki.\n3. Kosongkan tempat sampah B3 ke TPS resmi dan lakukan pengajuan penggantian APD yang aus.",
                "Kondisi housekeeping yang kurang tertata berpotensi menyebabkan insiden tersandung/terbentur dan menurunkan standar higienitas.",
                35
            )
        };

        // 4. Level 4 - LOW: 14 Days (Administrative & General Facility Maintenance)
        private static readonly (string Topic, string[] Keywords, string Action, string Reason, int SeverityScore)[] LowHazardRules = new[]
        {
            (
                "Administrasi K3, Marka & Fasilitas Umum",
                new[] { 
                    "poster k3", "spanduk k3", "spanduk sobek", "poster sobek", "marka pudar", "garis parkir pudar", "papan nama ruangan", "form checklist", 
                    "kotak p3k perlu restock", "kotak p3k", "kursi kantor", "ergonomis kantor", "kabel komputer kantor", "kebersihan meja kantor" 
                },
                "1. Jadwalkan pengecatan ulang marka lantai dan peremajaan poster keselamatan K3.\n2. Lakukan restock isi kotak P3K dan kelengkapan form checklist inspeksi.\n3. Rapikan kabel peralatan kantor dan sesuaikan posisi kerja untuk ergonomi optimal.",
                "Temuan bersifat administratif atau peremajaan fasilitas umum dengan tingkat risiko rendah terhadap keselamatan operasional utama.",
                15
            )
        };

        /// <summary>
        /// Analyzes a hazard report and calculates closure urgency, recommended due date, AI reasoning, and mitigation actions with advanced NLP and risk matrix scoring.
        /// </summary>
        public static HazardAiAnalysisResult AnalyzeHazard(
            string? temuan, 
            string? kategoriBahaya, 
            string? jenisBahaya, 
            string? tingkatResiko, 
            string? lokasi, 
            string? detilLokasi, 
            string? perbaikan, 
            string? statusTemuan, 
            DateTime tanggalTemuan)
        {
            var result = new HazardAiAnalysisResult();
            var cleanTemuan = (temuan ?? "").Trim();
            var cleanTemuanLower = cleanTemuan.ToLowerInvariant();
            var combinedText = $"{temuan} {kategoriBahaya} {jenisBahaya} {tingkatResiko} {lokasi} {detilLokasi} {perbaikan}".ToLowerInvariant();

            // 0. Base Quality / Target Chasing check
            bool isTargetChasing = TargetChasingKeywords.Any(kw => cleanTemuanLower == kw || cleanTemuanLower.Contains(" " + kw) || cleanTemuanLower.Contains(kw + " "));
            if (string.IsNullOrWhiteSpace(cleanTemuanLower) || cleanTemuanLower == "-" || (cleanTemuanLower.Length < 15 && isTargetChasing))
            {
                result.PriorityLevel = "LOW";
                result.PriorityLabel = "LOW - Prioritas Rendah (Indikasi Formalitas)";
                result.PriorityBadgeColor = "#94a3b8";
                result.PriorityBadgeText = "LOW (Formalitas / 14 Hari)";
                result.RecommendedDays = 14;
                result.RiskCategory = "Rendah / Tidak Jelas";
                result.QualityScore = 1;
                result.QualityScoreNotes = "Kualitas Rendah (Formalitas Target). Deskripsi bahaya sangat minim atau hanya frasa klise tanpa mencantumkan kondisi riil di lapangan.";
                result.AiReasoning = "Laporan hazard tidak memuat deskripsi bahaya spesifik atau terindikasi pengisian formalitas target. Tidak ditemukan indikasi bahaya kritis saat ini.";
                result.ActionRecommendation = "1. Minta pelapor melengkapi deskripsi temuan kondisi lapangan yang sebenarnya beserta dokumentasi foto.\n2. Lakukan verifikasi ulang bersama Pengawas Area (PJA).";
                CalculateDatesAndStatus(result, tanggalTemuan, statusTemuan);
                return result;
            }

            // Quality score calculation based on descriptiveness
            int wordCount = cleanTemuanLower.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (cleanTemuanLower.Length >= 50 && wordCount >= 8)
            {
                result.QualityScore = 5;
                result.QualityScoreNotes = "Kualitas Sangat Baik (Rill & Konstruktif). Deskripsi temuan detail dan menjelaskan kondisi bahaya serta usulan mitigasi.";
            }
            else if (cleanTemuanLower.Length >= 25 && wordCount >= 4)
            {
                result.QualityScore = 4;
                result.QualityScoreNotes = "Kualitas Baik (Acuan Standar). Laporan mendeskripsikan kondisi bahaya dengan cukup memadai.";
            }
            else
            {
                result.QualityScore = 3;
                result.QualityScoreNotes = "Kualitas Cukup. Informasi temuan ringkas namun masih dapat diidentifikasi.";
            }

            // --- MULTI-FACTOR RISK SCORING ENGINE ---
            double totalScore = 0;
            string bestTopic = "";
            string bestAction = "";
            string bestReason = "";
            bool isCriticalMatched = false;

            // Check Critical Rules
            foreach (var rule in CriticalHazardRules)
            {
                var matched = rule.Keywords.Where(kw => HasActiveKeyword(cleanTemuanLower, combinedText, kw)).ToList();
                if (matched.Any())
                {
                    isCriticalMatched = true;
                    totalScore = Math.Max(totalScore, rule.SeverityScore);
                    result.TriggeredKeywords.AddRange(matched);
                    result.IsGoldenRuleViolation = true;
                    result.GoldenRuleTopic = rule.Topic;
                    bestTopic = rule.Topic;
                    bestAction = rule.Action;
                    bestReason = rule.Reason;
                }
            }

            // Check High Rules
            if (!isCriticalMatched)
            {
                foreach (var rule in HighHazardRules)
                {
                    var matched = rule.Keywords.Where(kw => HasActiveKeyword(cleanTemuanLower, combinedText, kw)).ToList();
                    if (matched.Any())
                    {
                        totalScore = Math.Max(totalScore, rule.SeverityScore);
                        result.TriggeredKeywords.AddRange(matched);
                        if (string.IsNullOrEmpty(bestTopic))
                        {
                            bestTopic = rule.Topic;
                            bestAction = rule.Action;
                            bestReason = rule.Reason;
                        }
                    }
                }
            }

            // Check Medium Rules
            if (totalScore == 0)
            {
                foreach (var rule in MediumHazardRules)
                {
                    var matched = rule.Keywords.Where(kw => HasActiveKeyword(cleanTemuanLower, combinedText, kw)).ToList();
                    if (matched.Any())
                    {
                        totalScore = Math.Max(totalScore, rule.SeverityScore);
                        result.TriggeredKeywords.AddRange(matched);
                        if (string.IsNullOrEmpty(bestTopic))
                        {
                            bestTopic = rule.Topic;
                            bestAction = rule.Action;
                            bestReason = rule.Reason;
                        }
                    }
                }
            }

            // Check Low Rules
            if (totalScore == 0)
            {
                foreach (var rule in LowHazardRules)
                {
                    var matched = rule.Keywords.Where(kw => HasActiveKeyword(cleanTemuanLower, combinedText, kw)).ToList();
                    if (matched.Any())
                    {
                        totalScore = Math.Max(totalScore, rule.SeverityScore);
                        result.TriggeredKeywords.AddRange(matched);
                        if (string.IsNullOrEmpty(bestTopic))
                        {
                            bestTopic = rule.Topic;
                            bestAction = rule.Action;
                            bestReason = rule.Reason;
                        }
                    }
                }
            }

            // Location Sensitivity Factor (Pit, Hauling Road, Disposal, Sump, Blasting Area carry inherent high risk)
            var locCombined = $"{lokasi} {detilLokasi}".ToLowerInvariant();
            bool isHighRiskLocation = locCombined.Contains("pit") || 
                                      locCombined.Contains("hauling") || 
                                      locCombined.Contains("disposal") || 
                                      locCombined.Contains("sump") || 
                                      locCombined.Contains("front") || 
                                      locCombined.Contains("fuel") || 
                                      locCombined.Contains("handak") || 
                                      locCombined.Contains("blasting") ||
                                      locCombined.Contains("crusher");

            if (isHighRiskLocation && totalScore > 0)
            {
                totalScore += 8; // Elevate priority for high-risk zones
            }

            // Category Factor (Tindakan Tidak Aman / Unsafe Act is elevated because human action can cause immediate incident)
            var katLower = (kategoriBahaya ?? "").Trim().ToLowerInvariant();
            if (katLower.Contains("tindakan") && totalScore > 0)
            {
                totalScore += 5;
            }

            // Reported Risk Level Multiplier
            var resikoLower = (tingkatResiko ?? "").Trim().ToLowerInvariant();
            if (resikoLower.Contains("extreme") || resikoLower.Contains("kritis"))
            {
                totalScore = Math.Max(totalScore, 85);
            }
            else if (resikoLower.Contains("high") || resikoLower.Contains("tinggi"))
            {
                totalScore = Math.Max(totalScore, 60);
            }
            else if (resikoLower.Contains("medium") || resikoLower.Contains("sedang"))
            {
                totalScore = Math.Max(totalScore, 35);
            }
            else if (resikoLower.Contains("low") || resikoLower.Contains("rendah"))
            {
                if (totalScore == 0) totalScore = 15;
            }

            // Default fallback if no rules matched
            if (totalScore == 0)
            {
                totalScore = 40; // Default to Medium
                bestTopic = "Standar Operasional K3 Lapangan";
                bestReason = "Temuan bahaya umum di area kerja yang membutuhkan perbaikan terencana agar tidak berkembang menjadi kondisi tidak aman.";
                bestAction = "1. Tinjau kondisi temuan bersama Pengawas Lapangan (PJA).\n2. Lakukan perbaikan atau penggantian komponen yang tidak sesuai.\n3. Unggah bukti foto perbaikan dan close temuan.";
            }

            result.RiskScore = totalScore;

            // --- FINAL CLASSIFICATION BY COMPOSITE SCORE ---
            if (totalScore >= 75)
            {
                result.PriorityLevel = "CRITICAL";
                result.PriorityLabel = "CRITICAL - Segera Di-Close (Max 1x24 Jam)";
                result.PriorityBadgeColor = "#ef4444";
                result.PriorityBadgeText = "CRITICAL (Max 1x24 Jam)";
                result.RecommendedDays = 1;
                result.RiskCategory = "Kritis / Fatal Risk";
                string kwStr = result.TriggeredKeywords.Any() ? $" Kata kunci terdeteksi: [{string.Join(", ", result.TriggeredKeywords.Distinct())}]." : "";
                result.AiReasoning = $"{bestReason} Kategori: {bestTopic}.{kwStr} Sesuai kaidah Golden Rules K3LH Pertambangan, hazard ini wajib ditangani dan di-close dalam waktu maksimal 1x24 Jam.";
                result.ActionRecommendation = !string.IsNullOrEmpty(bestAction) ? bestAction : "1. Terapkan Stop Work Authority (SWA) dan isolasi area.\n2. Lakukan perbaikan darurat segera.\n3. Verifikasi penutupan status oleh PJA.";
            }
            else if (totalScore >= 50)
            {
                result.PriorityLevel = "HIGH";
                result.PriorityLabel = "HIGH - Prioritas Tinggi (Max 3 Hari)";
                result.PriorityBadgeColor = "#f97316";
                result.PriorityBadgeText = "HIGH (Max 3 Hari)";
                result.RecommendedDays = 3;
                result.RiskCategory = "Tinggi (Major Hazard)";
                string kwStr = result.TriggeredKeywords.Any() ? $" Kata kunci terdeteksi: [{string.Join(", ", result.TriggeredKeywords.Distinct())}]." : "";
                result.AiReasoning = $"{bestReason} Kategori: {bestTopic}.{kwStr} Berpotensi menyebabkan insiden kecelakaan kerja atau kerusakan unit jika tidak segera diperbaiki dalam 3 hari kerja.";
                result.ActionRecommendation = !string.IsNullOrEmpty(bestAction) ? bestAction : "1. Pasang rambu peringatan sementara pada area temuan.\n2. Koordinasikan rencana perbaikan dengan Pengawas Operasional (PJA).\n3. Selesaikan tindakan korektif dan verifikasi penutupan status hazard.";
            }
            else if (totalScore >= 25)
            {
                result.PriorityLevel = "MEDIUM";
                result.PriorityLabel = "MEDIUM - Prioritas Sedang (Max 7 Hari)";
                result.PriorityBadgeColor = "#eab308";
                result.PriorityBadgeText = "MEDIUM (Max 7 Hari)";
                result.RecommendedDays = 7;
                result.RiskCategory = "Sedang (Moderate)";
                string kwStr = result.TriggeredKeywords.Any() ? $" Kata kunci terdeteksi: [{string.Join(", ", result.TriggeredKeywords.Distinct())}]." : "";
                result.AiReasoning = $"{bestReason} Kategori: {bestTopic}.{kwStr} Memerlukan perbaikan rutin dan penataan dalam jangka waktu 1 minggu.";
                result.ActionRecommendation = !string.IsNullOrEmpty(bestAction) ? bestAction : "1. Lakukan program 5R/5S pada area temuan.\n2. Jadwalkan tindakan perbaikan dengan pengawas area terkait.\n3. Perbarui status penyelesaian di sistem SAP.";
            }
            else
            {
                result.PriorityLevel = "LOW";
                result.PriorityLabel = "LOW - Prioritas Rendah (Max 14 Hari)";
                result.PriorityBadgeColor = "#10b981";
                result.PriorityBadgeText = "LOW (Max 14 Hari)";
                result.RecommendedDays = 14;
                result.RiskCategory = "Rendah (Minor)";
                string kwStr = result.TriggeredKeywords.Any() ? $" Kata kunci terdeteksi: [{string.Join(", ", result.TriggeredKeywords.Distinct())}]." : "";
                result.AiReasoning = $"{bestReason} Kategori: {bestTopic}.{kwStr} Penanganan dapat dilakukan secara terjadwal dalam waktu 14 hari.";
                result.ActionRecommendation = !string.IsNullOrEmpty(bestAction) ? bestAction : "1. Jadwalkan peremajaan fasilitas umum atau administrasi K3.\n2. Lakukan penataan berkala.";
            }

            CalculateDatesAndStatus(result, tanggalTemuan, statusTemuan);
            return result;
        }

        /// <summary>
        /// Smart keyword checker with Negation Phrase filtering (prevents false positives like "tidak ada rem blong" or "telah diperbaiki tanggul").
        /// </summary>
        private static bool HasActiveKeyword(string cleanTemuan, string combinedText, string keyword)
        {
            if (string.IsNullOrWhiteSpace(combinedText) || string.IsNullOrWhiteSpace(keyword)) return false;

            // First check if keyword exists in combined text
            int index = combinedText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return false;

            // Check if keyword exists in cleanTemuan specifically
            int temuanIndex = cleanTemuan.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (temuanIndex >= 0)
            {
                // Check 40 characters preceding the keyword for negation terms
                int start = Math.Max(0, temuanIndex - 40);
                string prefix = cleanTemuan.Substring(start, temuanIndex - start);

                foreach (var neg in NegationPhrases)
                {
                    if (prefix.Contains(neg, StringComparison.OrdinalIgnoreCase))
                    {
                        return false; // Negated in temuan
                    }
                }
                return true;
            }

            // If found in combinedText (other fields), verify prefix negation
            int combStart = Math.Max(0, index - 40);
            string combPrefix = combinedText.Substring(combStart, index - combStart);
            foreach (var neg in NegationPhrases)
            {
                if (combPrefix.Contains(neg, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static void CalculateDatesAndStatus(HazardAiAnalysisResult result, DateTime tanggalTemuan, string? statusTemuan)
        {
            var baseDate = tanggalTemuan > DateTime.MinValue ? tanggalTemuan.Date : DateTime.Today;
            result.RecommendedDeadline = baseDate.AddDays(result.RecommendedDays);
            result.DueDateFormatted = result.RecommendedDeadline.ToString("dd MMM yyyy");
            result.RecommendedDueDateText = $"{result.RecommendedDays} Hari (Target: {result.DueDateFormatted})";

            var today = DateTime.Today;
            var isClosed = string.Equals(statusTemuan, "Closed", StringComparison.OrdinalIgnoreCase) || 
                           string.Equals(statusTemuan, "Selesai", StringComparison.OrdinalIgnoreCase);

            if (isClosed)
            {
                result.DueDateStatus = "CLOSED (Selesai)";
                result.DueDateStatusColor = "#64748b";
                result.DaysRemainingOrOverdue = 0;
            }
            else if (today > result.RecommendedDeadline)
            {
                var overdueDays = (today - result.RecommendedDeadline).Days;
                result.DueDateStatus = $"OVERDUE ({overdueDays} Hari)";
                result.DueDateStatusColor = "#ef4444";
                result.DaysRemainingOrOverdue = -overdueDays;
            }
            else if (today == result.RecommendedDeadline)
            {
                result.DueDateStatus = "DUE TODAY (Hari Ini)";
                result.DueDateStatusColor = "#f59e0b";
                result.DaysRemainingOrOverdue = 0;
            }
            else
            {
                var remainingDays = (result.RecommendedDeadline - today).Days;
                result.DueDateStatus = $"AKTIF (Sisa {remainingDays} Hari)";
                result.DueDateStatusColor = "#10b981";
                result.DaysRemainingOrOverdue = remainingDays;
            }
        }

        public static (int SuggestedRating, string AiNotes) AssessQuality(string programType, string title, string description)
        {
            var cleanType = programType?.Trim().ToLowerInvariant() ?? "";

            // Check if description has the protocol format
            if (description != null && description.StartsWith("INSPECTION_AUDIT |"))
            {
                return AssessInspection(description);
            }
            if (description != null && description.StartsWith("OBSERVATION_AUDIT |"))
            {
                return AssessObservation(description);
            }

            // Hazard report intelligent assessment
            if (cleanType == "hazard")
            {
                var aiHazard = AnalyzeHazard(description, null, null, null, null, null, null, "Open", DateTime.Today);
                string ratingBadge = aiHazard.QualityScore >= 4 ? "Kualitas Baik" : (aiHazard.QualityScore == 3 ? "Kualitas Cukup" : "Kualitas Rendah");
                string note = $"[{ratingBadge}] AI Prioritas Close: {aiHazard.PriorityLevel} ({aiHazard.PriorityBadgeText}). Batas Waktu: {aiHazard.DueDateFormatted}. {aiHazard.AiReasoning}";
                return (aiHazard.QualityScore, note);
            }
            
            // Standard evaluation for SafetyTalk, Coaching, or legacy fallback
            if (string.IsNullOrWhiteSpace(description) || description.Trim() == "-")
            {
                return (1, "Kualitas Rendah (Formalitas Target). Kolom deskripsi/catatan kosong atau hanya berisi tanda hubung/spasi. Terindikasi pemenuhan target administratif semata tanpa adanya kondisi keselamatan rill.");
            }

            var cleanDesc = description.Trim().ToLowerInvariant();
            var wordCount = cleanDesc.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            var charCount = cleanDesc.Length;

            // 1. Check for extreme target-chasing phrases/keywords
            bool containsTargetChasingWord = TargetChasingKeywords.Any(kw => cleanDesc == kw || cleanDesc.Contains(" " + kw) || cleanDesc.Contains(kw + " "));

            // 2. Length check: if it's very short
            if (charCount < 15 || wordCount < 3)
            {
                if (containsTargetChasingWord)
                {
                    return (1, $"Kualitas Rendah (Kejar Target). Deskripsi sangat singkat ({charCount} karakter) dan hanya berisi frasa klise '{description}'. Tidak mendokumentasikan bahaya nyata atau tindakan korektif di lapangan.");
                }
                return (2, $"Kualitas Kurang (Deskripsi Terbatas). Penjelasan kondisi lapangan terlalu ringkas ({charCount} karakter) untuk sebuah pelaporan SAP yang valid. Informasi mitigasi bahaya tidak memadai.");
            }

            // 3. Moderate target chasing warning
            if (containsTargetChasingWord && charCount < 30)
            {
                return (2, $"Kualitas Kurang (Indikasi Formalitas). Menggunakan klausa aman/klise '{description}' dengan penjelasan yang minim tanpa didukung data temuan mendalam.");
            }

            // 4. Good safety descriptions
            if (charCount >= 50 && wordCount >= 8)
            {
                return (5, $"Kualitas Sangat Baik (Rill & Konstruktif). Deskripsi sangat detail ({charCount} karakter, {wordCount} kata) dengan penjelasan kondisi lapangan yang komprehensif, didukung identifikasi bahaya yang jelas serta usulan tindakan perbaikan.");
            }

            // 5. Standard acceptable quality
            return (4, $"Kualitas Baik (Acuan Standar). Pelaporan menjelaskan kondisi temuan/kegiatan dengan format deskripsi yang memadai ({charCount} karakter). Memenuhi kriteria minimum pelaporan rill.");
        }

        private static (int SuggestedRating, string AiNotes) AssessInspection(string protocolStr)
        {
            var parts = protocolStr.Split('|').Select(p => p.Trim()).ToArray();
            string catatan = "";
            int safeCount = 0;
            int hazardCount = 0;
            int naCount = 0;

            foreach (var part in parts)
            {
                if (part.StartsWith("Catatan:")) catatan = part.Substring("Catatan:".Length).Trim();
                else if (part.StartsWith("YA:")) int.TryParse(part.Substring("YA:".Length).Trim(), out safeCount);
                else if (part.StartsWith("TIDAK:")) int.TryParse(part.Substring("TIDAK:".Length).Trim(), out hazardCount);
                else if (part.StartsWith("NA:")) int.TryParse(part.Substring("NA:".Length).Trim(), out naCount);
            }

            int totalAnswered = safeCount + hazardCount + naCount;
            if (totalAnswered == 0)
            {
                return (1, "Kualitas Rendah (Form Kosong). Seluruh pertanyaan checklist inspeksi belum diisi.");
            }

            bool hasCatatan = !string.IsNullOrWhiteSpace(catatan) && catatan != "-";
            var cleanCatatan = catatan.Trim().ToLowerInvariant();
            bool containsTargetChasingWord = TargetChasingKeywords.Any(kw => cleanCatatan == kw || cleanCatatan.Contains(" " + kw) || cleanCatatan.Contains(kw + " "));

            if (hazardCount > 0)
            {
                if (!hasCatatan)
                {
                    return (2, $"Kualitas Kurang (Catatan Rencana Perbaikan Kosong). Ditemukan {hazardCount} kriteria inspeksi bernilai TIDAK (Rusak/Bahaya), namun tidak dilengkapi catatan rencana tindakan perbaikan.");
                }
                if (catatan.Length < 15 && containsTargetChasingWord)
                {
                    return (3, $"Kualitas Cukup. Ditemukan {hazardCount} temuan bahaya dengan penjelasan rencana perbaikan yang sangat minim.");
                }
                return (5, $"Kualitas Sangat Baik (Temuan & Mitigasi). Laporan inspeksi merekam {hazardCount} temuan bahaya di lapangan dan dilengkapi dengan deskripsi rencana tindakan korektif yang jelas.");
            }
            else
            {
                // No hazards found (Clean inspection)
                if (hasCatatan)
                {
                    if (catatan.Length > 20 && !containsTargetChasingWord)
                    {
                        return (5, $"Kualitas Sangat Baik (Inspeksi & Catatan Lapangan). Seluruh kriteria inspeksi aman, dilengkapi dengan catatan observasi kondisi lapangan tambahan.");
                    }
                }
                return (4, "Kualitas Baik (Kondisi Aman). Seluruh kriteria inspeksi telah diperiksa dan dinyatakan aman/sesuai standar.");
            }
        }

        private static (int SuggestedRating, string AiNotes) AssessObservation(string protocolStr)
        {
            var parts = protocolStr.Split('|').Select(p => p.Trim()).ToArray();
            string kegiatan = "";
            string perihal = "";
            string hasil = "";
            string keterangan = "";

            foreach (var part in parts)
            {
                if (part.StartsWith("Kegiatan:")) kegiatan = part.Substring("Kegiatan:".Length).Trim();
                else if (part.StartsWith("Perihal:")) perihal = part.Substring("Perihal:".Length).Trim();
                else if (part.StartsWith("Hasil:")) hasil = part.Substring("Hasil:".Length).Trim();
                else if (part.StartsWith("Keterangan:")) keterangan = part.Substring("Keterangan:".Length).Trim();
            }

            if (string.IsNullOrWhiteSpace(kegiatan) || kegiatan == "-")
            {
                return (1, "Kualitas Rendah (Data Kosong). Deskripsi kegiatan yang diamati kosong.");
            }

            bool hasKeterangan = !string.IsNullOrWhiteSpace(keterangan) && keterangan != "-";
            var cleanKet = keterangan.Trim().ToLowerInvariant();
            bool containsTargetChasingWord = TargetChasingKeywords.Any(kw => cleanKet == kw || cleanKet.Contains(" " + kw) || cleanKet.Contains(kw + " "));

            bool isNegative = string.Equals(hasil, "Negative", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(hasil, "Violation", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(hasil, "Improvement", StringComparison.OrdinalIgnoreCase);

            if (isNegative)
            {
                if (!hasKeterangan)
                {
                    return (2, $"Kualitas Kurang (Rencana Mitigasi Kosong). Hasil observasi bernilai {hasil} (Ketidaksesuaian/Pelanggaran), namun tidak dilengkapi keterangan perbaikan atau edukasi bagi pekerja.");
                }
                if (keterangan.Length < 15 && containsTargetChasingWord)
                {
                    return (3, $"Kualitas Cukup. Observasi mencatat ketidaksesuaian dengan catatan mitigasi yang sangat singkat.");
                }
                return (5, $"Kualitas Sangat Baik (Korektif & Edukasi). Observasi mencatat ketidaksesuaian perilaku/kondisi kerja ({hasil}) dan disertai deskripsi pembinaan atau rencana tindakan perbaikan.");
            }
            else
            {
                if (hasKeterangan)
                {
                    if (keterangan.Length > 20 && !containsTargetChasingWord)
                    {
                        return (5, $"Kualitas Sangat Baik (Apresiasi Perilaku Aman). Observasi perilaku aman ({perihal}) didukung catatan apresiasi kondisi aman secara mendetail.");
                    }
                }
                return (4, $"Kualitas Baik (Penguatan Positif). Observasi perilaku aman pekerja di lapangan berjalan sesuai prosedur.");
            }
        }
    }
}
