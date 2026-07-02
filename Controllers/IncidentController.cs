using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using ClosedXML.Excel;
using Microsoft.VisualBasic.FileIO;
using MBS_SAP.Data;
using MBS_SAP.Models;
using System.Security.Claims;
using System.Globalization;

namespace MBS_SAP.Controllers
{
    [Authorize]
    public class IncidentController : Controller
    {
        private readonly AppDbContext _context;
        private readonly MBS_SAP.Services.ImageUploadService _imageUploadService;

        private static readonly string[] IncidentCategoryOptions =
        {
            "Fatality",
            "First Aid Injury",
            "Kebakaran",
            "Medical Treatment Injury",
            "Near Miss",
            "Property Damage"
        };

        public IncidentController(AppDbContext context, MBS_SAP.Services.ImageUploadService imageUploadService)
        {
            _context = context;
            _imageUploadService = imageUploadService;
        }

        private bool IsAdmin() => User.IsInRole("Admin");

        private void SetIncidentCategoryOptions(string? selectedValue = null)
        {
            var normalizedSelected = CanonicalizeIncidentCategory(selectedValue);

            ViewBag.IncidentCategories = IncidentCategoryOptions
                .Select(x => new SelectListItem
                {
                    Value = x,
                    Text = x.ToUpperInvariant(),
                    Selected = string.Equals(x, normalizedSelected, StringComparison.OrdinalIgnoreCase)
                })
                .ToList();
        }

        [HttpGet]
        public async Task<IActionResult> Index(int page = 1, int pageSize = 20)
        {
            ViewData["ActiveTab"] = "Incident";
            ViewData["IsAdmin"] = IsAdmin();

            if (page < 1) page = 1;
            if (pageSize < 5) pageSize = 20;

            var query = _context.IncidentNewsList
                .Where(i => i.IsPublished)
                .OrderByDescending(i => i.Id);

            var totalItems = await query.CountAsync();
            var totalPages = totalItems == 0 ? 1 : (int)Math.Ceiling(totalItems / (double)pageSize);
            if (page > totalPages) page = totalPages;

            var incidents = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalItems = totalItems;
            ViewBag.TotalPages = totalPages;
            ViewBag.NewestIncident = await query.FirstOrDefaultAsync();

            return View(incidents);
        }

        [HttpGet]
        public async Task<IActionResult> Detail(int id)
        {
            ViewData["ActiveTab"] = "Incident";
            ViewData["IsAdmin"] = IsAdmin();

            var incident = await _context.IncidentNewsList
                .FirstOrDefaultAsync(i => i.Id == id);

            if (incident == null || (!incident.IsPublished && !IsAdmin()))
            {
                TempData["ErrorMessage"] = "Berita insiden tidak ditemukan.";
                return RedirectToAction("Index");
            }

            return View(incident);
        }

        [HttpGet]
        public IActionResult Create()
        {
            if (!IsAdmin())
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk membuat berita insiden.";
                return RedirectToAction("Index");
            }

            ViewData["ActiveTab"] = "Incident";
            SetIncidentCategoryOptions("Near Miss");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string judul, string konten, string? lokasi, 
            DateTime? tanggalKejadian, string? kategori, IFormFile? gambar)
        {
            if (!IsAdmin())
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki akses.";
                return RedirectToAction("Index");
            }

            if (string.IsNullOrWhiteSpace(judul) || string.IsNullOrWhiteSpace(konten))
            {
                TempData["ErrorMessage"] = "Judul dan konten wajib diisi!";
                ViewData["ActiveTab"] = "Incident";
                SetIncidentCategoryOptions(kategori);
                return View();
            }

            string? gambarUrl = null;
            if (gambar != null && gambar.Length > 0)
            {
                gambarUrl = await _imageUploadService.UploadAndCompressImageAsync(gambar, "incidents", "inc");
            }

            var incident = new IncidentNews
            {
                Judul = judul,
                Konten = konten,
                GambarUrl = gambarUrl,
                Lokasi = lokasi,
                TanggalKejadian = tanggalKejadian,
                Kategori = CanonicalizeIncidentCategory(kategori) ?? "Near Miss",
                DibuatOleh = User.Identity?.Name ?? "Admin",
                NikPembuat = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0",
                CreatedAt = DateTime.Now
            };

            _context.IncidentNewsList.Add(incident);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Berita insiden berhasil dipublikasikan!";
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            if (!IsAdmin())
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk mengubah berita insiden.";
                return RedirectToAction(nameof(Index));
            }

            ViewData["ActiveTab"] = "Incident";

            var incident = await _context.IncidentNewsList.FirstOrDefaultAsync(i => i.Id == id);
            if (incident == null)
            {
                TempData["ErrorMessage"] = "Data insiden tidak ditemukan.";
                return RedirectToAction(nameof(Index));
            }

            SetIncidentCategoryOptions(incident.Kategori);
            return View(incident);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, string judul, string konten, string? lokasi,
            DateTime? tanggalKejadian, string? kategori, IFormFile? gambar)
        {
            if (!IsAdmin())
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki akses.";
                return RedirectToAction(nameof(Index));
            }

            var incident = await _context.IncidentNewsList.FirstOrDefaultAsync(i => i.Id == id);
            if (incident == null)
            {
                TempData["ErrorMessage"] = "Data insiden tidak ditemukan.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(judul) || string.IsNullOrWhiteSpace(konten))
            {
                TempData["ErrorMessage"] = "Judul dan konten wajib diisi.";
                incident.Kategori = kategori;
                SetIncidentCategoryOptions(kategori);
                return View(incident);
            }

            if (gambar != null && gambar.Length > 0)
            {
                incident.GambarUrl = await _imageUploadService.UploadAndCompressImageAsync(gambar, "incidents", "inc");
            }

            incident.Judul = Truncate(judul, 300);
            incident.Konten = konten;
            incident.Lokasi = Truncate(lokasi, 150);
            incident.TanggalKejadian = tanggalKejadian;
            incident.Kategori = Truncate(CanonicalizeIncidentCategory(kategori) ?? "Near Miss", 100);
            incident.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Data insiden berhasil diperbarui.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportSourceData(IFormFile? excelFile)
        {
            if (!IsAdmin())
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki akses.";
                return RedirectToAction("Index");
            }

            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["ErrorMessage"] = "File Excel wajib diunggah.";
                return RedirectToAction(nameof(Create));
            }

            var fileName = excelFile.FileName ?? string.Empty;
            var isXlsx = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
            var isCsv = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            if (!isXlsx && !isCsv)
            {
                TempData["ErrorMessage"] = "Format file harus .xlsx atau .csv";
                return RedirectToAction(nameof(Create));
            }

            try
            {
                var nik = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0";
                var createdBy = (User.Identity?.Name ?? "Admin") + " (Import Excel)";
                if (createdBy.Length > 150) createdBy = createdBy.Substring(0, 150);

                int inserted;
                int skipped;

                if (isCsv)
                {
                    (inserted, skipped) = await ImportFromCsvAsync(excelFile, nik, createdBy);
                }
                else
                {
                    (inserted, skipped) = await ImportFromXlsxAsync(excelFile, nik, createdBy);
                }

                TempData["SuccessMessage"] = $"Import selesai. Data masuk: {inserted}, dilewati: {skipped}.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Gagal import file: {ex.Message}";
                return RedirectToAction(nameof(Create));
            }
        }

        private async Task<(int inserted, int skipped)> ImportFromXlsxAsync(IFormFile excelFile, string nik, string createdBy)
        {
            using var stream = excelFile.OpenReadStream();
            using var workbook = new XLWorkbook(stream);

            var worksheet = workbook.Worksheets
                .FirstOrDefault(w => string.Equals(w.Name.Trim(), "source data", StringComparison.OrdinalIgnoreCase));

            if (worksheet == null)
            {
                throw new InvalidOperationException("Sheet 'source data' tidak ditemukan.");
            }

            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            var lastCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastRow < 2 || lastCol < 2)
            {
                throw new InvalidOperationException("Sheet 'source data' tidak memiliki data yang cukup.");
            }

            int headerRow = 1;
            for (int r = 1; r <= Math.Min(lastRow, 20); r++)
            {
                bool hasAktual = false;
                for (int c = 1; c <= lastCol; c++)
                {
                    var headerText = NormalizeHeader(worksheet.Cell(r, c).GetString());
                    if (headerText == "aktualinsiden" || headerText == "actualincident")
                    {
                        hasAktual = true;
                        break;
                    }
                }

                if (hasAktual)
                {
                    headerRow = r;
                    break;
                }
            }

            var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int c = 1; c <= lastCol; c++)
            {
                var key = NormalizeHeader(worksheet.Cell(headerRow, c).GetString());
                if (!string.IsNullOrWhiteSpace(key) && !colMap.ContainsKey(key))
                {
                    colMap[key] = c;
                }
            }

            int? colAktual = FindColumn(colMap, "aktualinsiden", "actualincident");
            if (!colAktual.HasValue)
            {
                throw new InvalidOperationException("Kolom 'aktual insiden' tidak ditemukan di sheet source data.");
            }

            int? colTitle = FindColumn(colMap, "judul", "title", "kejadian", "incident");
            int? colDesc = FindColumn(colMap, "konten", "keterangan", "deskripsi", "kronologi", "detail", "uraian");
            int? colLokasi = FindColumn(colMap, "lokasi", "location", "site", "area");
            int? colTanggal = FindColumn(colMap, "tanggalkejadian", "tanggal", "date", "tgl", "tglkejadian");

            int inserted = 0;
            int skipped = 0;

            for (int r = headerRow + 1; r <= lastRow; r++)
            {
                string aktualRaw = GetCellValue(worksheet.Cell(r, colAktual.Value));
                if (string.IsNullOrWhiteSpace(aktualRaw))
                {
                    skipped++;
                    continue;
                }

                string kategori = NormalizeIncidentCategory(aktualRaw);
                string judul = colTitle.HasValue ? GetCellValue(worksheet.Cell(r, colTitle.Value)) : string.Empty;
                string konten = colDesc.HasValue ? GetCellValue(worksheet.Cell(r, colDesc.Value)) : string.Empty;
                string lokasi = colLokasi.HasValue ? GetCellValue(worksheet.Cell(r, colLokasi.Value)) : string.Empty;

                if (string.IsNullOrWhiteSpace(judul))
                {
                    judul = $"{kategori} - Imported Incident";
                }
                if (string.IsNullOrWhiteSpace(konten))
                {
                    konten = $"Data import dari sheet source data dengan aktual insiden: {aktualRaw}.";
                }

                DateTime? tanggalKejadian = null;
                if (colTanggal.HasValue)
                {
                    var dateCell = worksheet.Cell(r, colTanggal.Value);
                    if (dateCell.TryGetValue<DateTime>(out var dt))
                    {
                        tanggalKejadian = dt;
                    }
                    else
                    {
                        var dateRaw = GetCellValue(dateCell);
                        tanggalKejadian = ParseDateFlexible(dateRaw);
                    }
                }

                AddImportedIncident(judul, konten, lokasi, kategori, tanggalKejadian, nik, createdBy);
                inserted++;
            }

            await _context.SaveChangesAsync();
            return (inserted, skipped);
        }

        private async Task<(int inserted, int skipped)> ImportFromCsvAsync(IFormFile csvFile, string nik, string createdBy)
        {
            using var stream = csvFile.OpenReadStream();
            using var parser = new TextFieldParser(stream);
            parser.TextFieldType = FieldType.Delimited;
            parser.SetDelimiters(",");
            parser.HasFieldsEnclosedInQuotes = true;

            Dictionary<string, int>? colMap = null;
            int? colAktual = null;
            int? colTitle = null;
            int? colDesc = null;
            int? colLokasi = null;
            int? colTanggal = null;

            int inserted = 0;
            int skipped = 0;

            while (!parser.EndOfData)
            {
                string[]? fields;
                try
                {
                    fields = parser.ReadFields();
                }
                catch
                {
                    skipped++;
                    continue;
                }

                if (fields == null || fields.Length == 0)
                {
                    skipped++;
                    continue;
                }

                if (colMap == null)
                {
                    var tempMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < fields.Length; i++)
                    {
                        var key = NormalizeHeader(fields[i]);
                        if (!string.IsNullOrWhiteSpace(key) && !tempMap.ContainsKey(key))
                        {
                            tempMap[key] = i;
                        }
                    }

                    var maybeAktual = FindColumn(tempMap, "aktualinsiden", "actualincident");
                    if (maybeAktual.HasValue)
                    {
                        colMap = tempMap;
                        colAktual = maybeAktual;
                        colTitle = FindColumn(tempMap, "judul", "title", "kejadian", "incident", "briefdescription");
                        colDesc = FindColumn(tempMap, "konten", "keterangan", "deskripsi", "kronologi", "detail", "uraian", "description");
                        colLokasi = FindColumn(tempMap, "lokasi", "location", "site", "area");
                        colTanggal = FindColumn(tempMap, "tanggalkejadian", "tanggal", "date", "tgl", "tglkejadian");
                    }

                    continue;
                }

                if (!colAktual.HasValue || colAktual.Value < 0 || colAktual.Value >= fields.Length)
                {
                    skipped++;
                    continue;
                }

                var aktualRaw = (fields[colAktual.Value] ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(aktualRaw))
                {
                    skipped++;
                    continue;
                }

                string kategori = NormalizeIncidentCategory(aktualRaw);
                string judul = GetCsvField(fields, colTitle);
                string konten = GetCsvField(fields, colDesc);
                string lokasi = GetCsvField(fields, colLokasi);
                DateTime? tanggalKejadian = ParseDateFlexible(GetCsvField(fields, colTanggal));

                if (string.IsNullOrWhiteSpace(judul))
                {
                    judul = $"{kategori} - Imported Incident";
                }
                if (string.IsNullOrWhiteSpace(konten))
                {
                    konten = $"Data import CSV dengan aktual insiden: {aktualRaw}.";
                }

                AddImportedIncident(judul, konten, lokasi, kategori, tanggalKejadian, nik, createdBy);
                inserted++;
            }

            if (colMap == null || !colAktual.HasValue)
            {
                throw new InvalidOperationException("Kolom 'ACTUAL INCIDENT' tidak ditemukan pada file CSV.");
            }

            await _context.SaveChangesAsync();
            return (inserted, skipped);
        }

        private void AddImportedIncident(string judul, string konten, string lokasi, string kategori, DateTime? tanggalKejadian, string nik, string createdBy)
        {
            var createdAt = tanggalKejadian ?? DateTime.Now;

            var incident = new IncidentNews
            {
                Judul = Truncate(judul, 300),
                Konten = konten,
                Lokasi = Truncate(lokasi, 150),
                TanggalKejadian = tanggalKejadian,
                Kategori = Truncate(kategori, 100),
                DibuatOleh = createdBy,
                NikPembuat = Truncate(nik, 50),
                IsPublished = true,
                CreatedAt = createdAt
            };

            _context.IncidentNewsList.Add(incident);
        }

        private static string GetCsvField(string[] fields, int? index)
        {
            if (!index.HasValue || index.Value < 0 || index.Value >= fields.Length) return string.Empty;
            return (fields[index.Value] ?? string.Empty).Trim();
        }

        private static DateTime? ParseDateFlexible(string? raw)
        {
            var dateRaw = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(dateRaw)) return null;

            if (DateTime.TryParse(dateRaw, out var parsedDate))
            {
                return parsedDate;
            }

            var formats = new[]
            {
                "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
                "dd-MMM-yy", "d-MMM-yy", "dd-MMM-yyyy", "d-MMM-yyyy",
                "MM/dd/yyyy", "M/d/yyyy"
            };

            foreach (var fmt in formats)
            {
                if (DateTime.TryParseExact(dateRaw, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactDate))
                {
                    return exactDate;
                }
            }

            return null;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (!IsAdmin())
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki akses.";
                return RedirectToAction("Index");
            }

            var incident = await _context.IncidentNewsList.FindAsync(id);
            if (incident != null)
            {
                _context.IncidentNewsList.Remove(incident);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Berita insiden berhasil dihapus.";
            }

            return RedirectToAction("Index");
        }

        private static int? FindColumn(Dictionary<string, int> colMap, params string[] aliases)
        {
            foreach (var alias in aliases)
            {
                if (colMap.TryGetValue(alias, out var col))
                {
                    return col;
                }

                var match = colMap.FirstOrDefault(x => x.Key.Contains(alias, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(match.Key))
                {
                    return match.Value;
                }
            }
            return null;
        }

        private static string NormalizeHeader(string? text)
        {
            return new string((text ?? string.Empty)
                .ToLowerInvariant()
                .Where(ch => char.IsLetterOrDigit(ch))
                .ToArray());
        }

        private static string GetCellValue(IXLCell cell)
        {
            return (cell.GetString() ?? string.Empty).Trim();
        }

        private static string Truncate(string? value, int max)
        {
            var safe = value ?? string.Empty;
            return safe.Length <= max ? safe : safe.Substring(0, max);
        }

        private static string NormalizeIncidentCategory(string aktualInsiden)
        {
            var text = (aktualInsiden ?? string.Empty).Trim().ToLowerInvariant();

            var canonical = CanonicalizeIncidentCategory(text);
            if (!string.IsNullOrWhiteSpace(canonical))
            {
                return canonical;
            }

            if (text.Contains("near miss") || text.Contains("nyaris") || text.Contains("hampir celaka")) return "Near Miss";
            if (text.Contains("property") || text.Contains("damaged") || text.Contains("damage") || text.Contains("kerusakan")) return "Property Damage";
            if (text.Contains("first aid") || text.Contains("p3k") || text.Contains("pertolongan pertama")) return "First Aid Injury";
            if (text.Contains("medical treatment") || text.Contains("rawat jalan") || text.Contains("klinik") || text.Contains("dokter")) return "Medical Treatment Injury";
            if (text.Contains("kebakaran") || text.Contains("fire")) return "Kebakaran";
            if (text.Contains("mati") || text.Contains("fatal") || text.Contains("meninggal") || text.Contains("death")) return "Fatality";

            return "Near Miss";
        }

        private static string? CanonicalizeIncidentCategory(string? rawCategory)
        {
            var text = (rawCategory ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) return null;

            if (text.Equals("fatality", StringComparison.OrdinalIgnoreCase)
                || text.Equals("fatal", StringComparison.OrdinalIgnoreCase)
                || text.Equals("mati", StringComparison.OrdinalIgnoreCase))
            {
                return "Fatality";
            }

            if (text.Equals("first aid injury", StringComparison.OrdinalIgnoreCase)
                || text.Equals("first aid", StringComparison.OrdinalIgnoreCase)
                || text.Equals("p3k", StringComparison.OrdinalIgnoreCase))
            {
                return "First Aid Injury";
            }

            if (text.Equals("kebakaran", StringComparison.OrdinalIgnoreCase)
                || text.Equals("fire", StringComparison.OrdinalIgnoreCase))
            {
                return "Kebakaran";
            }

            if (text.Equals("medical treatment injury", StringComparison.OrdinalIgnoreCase)
                || text.Equals("medical treatment", StringComparison.OrdinalIgnoreCase))
            {
                return "Medical Treatment Injury";
            }

            if (text.Equals("near miss", StringComparison.OrdinalIgnoreCase))
            {
                return "Near Miss";
            }

            if (text.Equals("property damage", StringComparison.OrdinalIgnoreCase)
                || text.Equals("property damaged", StringComparison.OrdinalIgnoreCase))
            {
                return "Property Damage";
            }

            return null;
        }
    }
}
