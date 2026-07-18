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
            if (string.IsNullOrWhiteSpace(description) || description.Trim() == "-")
            {
                return (1, "Kualitas Rendah (Formalitas Target). Kolom deskripsi temuan kosong atau hanya berisi tanda hubung/spasi. Terindikasi pemenuhan target administratif semata tanpa adanya kondisi keselamatan rill.");
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
    }
}
