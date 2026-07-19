using System;
using System.Collections.Generic;
using System.Linq;

namespace MBS_SAP.Services
{
    public class SapQualityMlEngine
    {
        private static readonly HashSet<string> TargetChasingKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "aman", "oke", "ok", "sip", "tidak ada", "no", "clean", "tidak ada temuan", "normal", "ready", "bagus", "baik", "kondisi aman", "nihil", "tidakada", "semua aman"
        };

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
            
            // Standard evaluation for Hazard, SafetyTalk, Coaching, or legacy fallback
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
            // Format: "INSPECTION_AUDIT | Catatan: [Catatan] | YA: [SafeCount] | TIDAK: [HazardCount] | NA: [NaCount]"
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
            // Format: "OBSERVATION_AUDIT | Kegiatan: [Kegiatan] | Perihal: [Perihal] | Hasil: [Hasil] | Keterangan: [Keterangan]"
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
                // Positive observation
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
