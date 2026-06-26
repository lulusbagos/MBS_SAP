using ClosedXML.Excel;
using System;
using System.IO;
using MBS_SAP.Models;

namespace MBS_SAP.Services
{
    public class ExcelService
    {
        private static readonly object _lock = new object();
        private const string FilePath = @"D:\SAP.xlsx";

        private void EnsureFileExists()
        {
            if (!File.Exists(FilePath))
            {
                using (var workbook = new XLWorkbook())
                {
                    // Create basic structure if it is completely missing (unlikely, but safe)
                    var hr = workbook.Worksheets.Add("HAZARD REPORT");
                    hr.Cell(3, 1).Value = "Foto Temuan";
                    hr.Cell(3, 2).Value = "Tanggal";
                    hr.Cell(3, 3).Value = "Time";
                    hr.Cell(3, 4).Value = "Nama";
                    hr.Cell(3, 5).Value = "NIK";
                    hr.Cell(3, 6).Value = "Departemen";
                    hr.Cell(3, 7).Value = "Area";
                    hr.Cell(3, 8).Value = "Lokasi";
                    hr.Cell(3, 9).Value = "Detil Lokasi";
                    hr.Cell(3, 10).Value = "Temuan";
                    hr.Cell(3, 11).Value = "Kategori Bahaya";
                    hr.Cell(3, 12).Value = "Jenis Bahaya";
                    hr.Cell(3, 13).Value = "Jenis Ketidaksesuaian";
                    hr.Cell(3, 14).Value = "Tingkat Resiko";
                    hr.Cell(3, 15).Value = "Perbaikan";
                    hr.Cell(3, 16).Value = "Tindakan Perbaikan";
                    hr.Cell(3, 17).Value = "PJA";
                    hr.Cell(3, 18).Value = "NIK PJA";
                    hr.Cell(3, 19).Value = "Departemen PJA";
                    hr.Cell(3, 20).Value = "Status Temuan";

                    var ins = workbook.Worksheets.Add("INSPECTION");
                    ins.Cell(1, 1).Value = "Tanggal";
                    ins.Cell(1, 2).Value = "Time";
                    ins.Cell(1, 3).Value = "Nama";
                    ins.Cell(1, 4).Value = "NIK";
                    ins.Cell(1, 5).Value = "Departemen";
                    ins.Cell(1, 6).Value = "Area";
                    ins.Cell(1, 7).Value = "Lokasi";
                    ins.Cell(1, 8).Value = "Detil Lokasi";
                    ins.Cell(1, 9).Value = "Jenis Inspeksi";
                    ins.Cell(1, 10).Value = "PJA";
                    ins.Cell(1, 11).Value = "NIK PJA";
                    ins.Cell(1, 12).Value = "Departemen PJA";

                    var ap = workbook.Worksheets.Add("ACTION PLAN");
                    ap.Cell(1, 1).Value = "Foto Temuan";
                    ap.Cell(1, 2).Value = "Foto Perbaikan";
                    ap.Cell(1, 3).Value = "Tanggal";
                    ap.Cell(1, 4).Value = "Time";
                    ap.Cell(1, 5).Value = "Nama";
                    ap.Cell(1, 6).Value = "NIK";
                    ap.Cell(1, 7).Value = "Departemen";
                    ap.Cell(1, 8).Value = "Area";
                    ap.Cell(1, 9).Value = "Lokasi";
                    ap.Cell(1, 10).Value = "Detil Lokasi";
                    ap.Cell(1, 11).Value = "Item SAP";
                    ap.Cell(1, 12).Value = "Kategori Temuan";
                    ap.Cell(1, 13).Value = "Detil Temuan";
                    ap.Cell(1, 14).Value = "Status";
                    ap.Cell(1, 15).Value = "PJA";
                    ap.Cell(1, 16).Value = "NIK PJA";
                    ap.Cell(1, 17).Value = "Departemen PJA";
                    ap.Cell(1, 18).Value = "PIC";
                    ap.Cell(1, 19).Value = "NIK PIC";
                    ap.Cell(1, 20).Value = "Departemen PIC";
                    ap.Cell(1, 21).Value = "Rencana Perbaikan";
                    ap.Cell(1, 22).Value = "Tanggal Rencana Perbaikan";
                    ap.Cell(1, 23).Value = "Perbaikan";
                    ap.Cell(1, 24).Value = "Tanggal Perbaikan";
                    ap.Cell(1, 25).Value = "Overdue";
                    ap.Cell(1, 26).Value = "Alasan Overdue";

                    var st = workbook.Worksheets.Add("SAFETY TALK");
                    st.Cell(1, 1).Value = "Foto Diri";
                    st.Cell(1, 2).Value = "Foto Kegiatan";
                    st.Cell(1, 3).Value = "Tanggal";
                    st.Cell(1, 4).Value = "Time";
                    st.Cell(1, 5).Value = "Nama";
                    st.Cell(1, 6).Value = "NIK";
                    st.Cell(1, 7).Value = "Departemen";
                    st.Cell(1, 8).Value = "Area";
                    st.Cell(1, 9).Value = "Lokasi";
                    st.Cell(1, 10).Value = "Detil Lokasi";
                    st.Cell(1, 11).Value = "Judul";
                    st.Cell(1, 12).Value = "Keterangan";

                    var p5 = workbook.Worksheets.Add("P5M");
                    p5.Cell(1, 1).Value = "Foto Kegiatan";
                    p5.Cell(1, 2).Value = "Tanggal";
                    p5.Cell(1, 3).Value = "Time";
                    p5.Cell(1, 4).Value = "Nama";
                    p5.Cell(1, 5).Value = "NIK";
                    p5.Cell(1, 6).Value = "Departemen";
                    p5.Cell(1, 7).Value = "Area";
                    p5.Cell(1, 8).Value = "Lokasi";
                    p5.Cell(1, 9).Value = "Detil Lokasi";
                    p5.Cell(1, 10).Value = "Topik";
                    p5.Cell(1, 11).Value = "Judul";
                    p5.Cell(1, 12).Value = "Keterangan";
                    p5.Cell(1, 13).Value = "List Pertanyaan";
                    p5.Cell(1, 14).Value = "Jawaban";
                    p5.Cell(1, 15).Value = "Catatan";

                    workbook.SaveAs(FilePath);
                }
            }
        }

        public void AppendHazardReport(HazardReport report)
        {
            lock (_lock)
            {
                EnsureFileExists();
                using (var workbook = new XLWorkbook(FilePath))
                {
                    var worksheet = workbook.Worksheet("HAZARD REPORT");
                    int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 3;
                    int targetRow = lastRow + 1;
                    if (targetRow < 4) targetRow = 4;

                    worksheet.Cell(targetRow, 1).Value = report.FotoTemuan ?? "";
                    worksheet.Cell(targetRow, 2).Value = report.Tanggal;
                    worksheet.Cell(targetRow, 2).Style.DateFormat.Format = "yyyy-MM-dd";
                    worksheet.Cell(targetRow, 3).Value = report.Waktu;
                    worksheet.Cell(targetRow, 3).Style.NumberFormat.Format = "hh:mm";
                    worksheet.Cell(targetRow, 4).Value = report.Nama;
                    worksheet.Cell(targetRow, 5).Value = report.Nik;
                    worksheet.Cell(targetRow, 6).Value = report.Departemen ?? "";
                    worksheet.Cell(targetRow, 7).Value = report.Area ?? "";
                    worksheet.Cell(targetRow, 8).Value = report.Lokasi ?? "";
                    worksheet.Cell(targetRow, 9).Value = report.DetilLokasi ?? "";
                    worksheet.Cell(targetRow, 10).Value = report.Temuan;
                    worksheet.Cell(targetRow, 11).Value = report.KategoriBahaya ?? "";
                    worksheet.Cell(targetRow, 12).Value = report.JenisBahaya ?? "";
                    worksheet.Cell(targetRow, 13).Value = report.JenisKetidaksesuaian ?? "";
                    worksheet.Cell(targetRow, 14).Value = report.TingkatResiko ?? "";
                    worksheet.Cell(targetRow, 15).Value = report.Perbaikan ?? "";
                    worksheet.Cell(targetRow, 16).Value = report.TindakanPerbaikan ?? "";
                    worksheet.Cell(targetRow, 17).Value = report.Pja ?? "";
                    worksheet.Cell(targetRow, 18).Value = report.NikPja ?? "";
                    worksheet.Cell(targetRow, 19).Value = report.DepartemenPja ?? "";
                    worksheet.Cell(targetRow, 20).Value = report.StatusTemuan;

                    workbook.Save();
                }
            }
        }

        public void AppendInspection(Inspection report)
        {
            lock (_lock)
            {
                EnsureFileExists();
                using (var workbook = new XLWorkbook(FilePath))
                {
                    var worksheet = workbook.Worksheet("INSPECTION");
                    int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
                    int targetRow = lastRow + 1;

                    worksheet.Cell(targetRow, 1).Value = report.Tanggal;
                    worksheet.Cell(targetRow, 1).Style.DateFormat.Format = "yyyy-MM-dd";
                    worksheet.Cell(targetRow, 2).Value = report.Waktu;
                    worksheet.Cell(targetRow, 2).Style.NumberFormat.Format = "hh:mm";
                    worksheet.Cell(targetRow, 3).Value = report.Nama;
                    worksheet.Cell(targetRow, 4).Value = report.Nik;
                    worksheet.Cell(targetRow, 5).Value = report.Departemen ?? "";
                    worksheet.Cell(targetRow, 6).Value = report.Area ?? "";
                    worksheet.Cell(targetRow, 7).Value = report.Lokasi ?? "";
                    worksheet.Cell(targetRow, 8).Value = report.DetilLokasi ?? "";
                    worksheet.Cell(targetRow, 9).Value = report.JenisInspeksi ?? "";
                    worksheet.Cell(targetRow, 10).Value = report.Pja ?? "";
                    worksheet.Cell(targetRow, 11).Value = report.NikPja ?? "";
                    worksheet.Cell(targetRow, 12).Value = report.DepartemenPja ?? "";

                    workbook.Save();
                }
            }
        }

        public void AppendActionPlan(ActionPlan report)
        {
            lock (_lock)
            {
                EnsureFileExists();
                using (var workbook = new XLWorkbook(FilePath))
                {
                    var worksheet = workbook.Worksheet("ACTION PLAN");
                    int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
                    int targetRow = lastRow + 1;

                    worksheet.Cell(targetRow, 1).Value = report.FotoTemuan ?? "";
                    worksheet.Cell(targetRow, 2).Value = report.FotoPerbaikan ?? "";
                    worksheet.Cell(targetRow, 3).Value = report.Tanggal;
                    worksheet.Cell(targetRow, 3).Style.DateFormat.Format = "yyyy-MM-dd";
                    worksheet.Cell(targetRow, 4).Value = report.Waktu;
                    worksheet.Cell(targetRow, 4).Style.NumberFormat.Format = "hh:mm";
                    worksheet.Cell(targetRow, 5).Value = report.Nama;
                    worksheet.Cell(targetRow, 6).Value = report.Nik;
                    worksheet.Cell(targetRow, 7).Value = report.Departemen ?? "";
                    worksheet.Cell(targetRow, 8).Value = report.Area ?? "";
                    worksheet.Cell(targetRow, 9).Value = report.Lokasi ?? "";
                    worksheet.Cell(targetRow, 10).Value = report.DetilLokasi ?? "";
                    worksheet.Cell(targetRow, 11).Value = report.ItemSap ?? "";
                    worksheet.Cell(targetRow, 12).Value = report.KategoriTemuan ?? "";
                    worksheet.Cell(targetRow, 13).Value = report.DetilTemuan ?? "";
                    worksheet.Cell(targetRow, 14).Value = report.Status ?? "Open";
                    worksheet.Cell(targetRow, 15).Value = report.Pja ?? "";
                    worksheet.Cell(targetRow, 16).Value = report.NikPja ?? "";
                    worksheet.Cell(targetRow, 17).Value = report.DepartemenPja ?? "";
                    worksheet.Cell(targetRow, 18).Value = report.Pic ?? "";
                    worksheet.Cell(targetRow, 19).Value = report.NikPic ?? "";
                    worksheet.Cell(targetRow, 20).Value = report.DepartemenPic ?? "";
                    worksheet.Cell(targetRow, 21).Value = report.RencanaPerbaikan ?? "";
                    if (report.TanggalRencanaPerbaikan.HasValue)
                    {
                        worksheet.Cell(targetRow, 22).Value = report.TanggalRencanaPerbaikan.Value;
                        worksheet.Cell(targetRow, 22).Style.DateFormat.Format = "yyyy-MM-dd";
                    }
                    else
                    {
                        worksheet.Cell(targetRow, 22).Value = "";
                    }
                    worksheet.Cell(targetRow, 23).Value = report.Perbaikan ?? "";
                    if (report.TanggalPerbaikan.HasValue)
                    {
                        worksheet.Cell(targetRow, 24).Value = report.TanggalPerbaikan.Value;
                        worksheet.Cell(targetRow, 24).Style.DateFormat.Format = "yyyy-MM-dd";
                    }
                    else
                    {
                        worksheet.Cell(targetRow, 24).Value = "";
                    }
                    worksheet.Cell(targetRow, 25).Value = report.Overdue ?? "";
                    worksheet.Cell(targetRow, 26).Value = report.AlasanOverdue ?? "";

                    workbook.Save();
                }
            }
        }

        public void AppendSafetyTalk(SafetyTalk report)
        {
            lock (_lock)
            {
                EnsureFileExists();
                using (var workbook = new XLWorkbook(FilePath))
                {
                    var worksheet = workbook.Worksheet("SAFETY TALK");
                    int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
                    int targetRow = lastRow + 1;

                    worksheet.Cell(targetRow, 1).Value = report.FotoDiri ?? "";
                    worksheet.Cell(targetRow, 2).Value = report.FotoKegiatan ?? "";
                    worksheet.Cell(targetRow, 3).Value = report.Tanggal;
                    worksheet.Cell(targetRow, 3).Style.DateFormat.Format = "yyyy-MM-dd";
                    worksheet.Cell(targetRow, 4).Value = report.Waktu;
                    worksheet.Cell(targetRow, 4).Style.NumberFormat.Format = "hh:mm";
                    worksheet.Cell(targetRow, 5).Value = report.Nama;
                    worksheet.Cell(targetRow, 6).Value = report.Nik;
                    worksheet.Cell(targetRow, 7).Value = report.Departemen ?? "";
                    worksheet.Cell(targetRow, 8).Value = report.Area ?? "";
                    worksheet.Cell(targetRow, 9).Value = report.Lokasi ?? "";
                    worksheet.Cell(targetRow, 10).Value = report.DetilLokasi ?? "";
                    worksheet.Cell(targetRow, 11).Value = report.Judul ?? "";
                    worksheet.Cell(targetRow, 12).Value = report.Keterangan ?? "";

                    workbook.Save();
                }
            }
        }

        public void AppendP5m(P5m report)
        {
            lock (_lock)
            {
                EnsureFileExists();
                using (var workbook = new XLWorkbook(FilePath))
                {
                    var worksheet = workbook.Worksheet("P5M");
                    int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
                    int targetRow = lastRow + 1;

                    worksheet.Cell(targetRow, 1).Value = report.FotoKegiatan ?? "";
                    worksheet.Cell(targetRow, 2).Value = report.Tanggal;
                    worksheet.Cell(targetRow, 2).Style.DateFormat.Format = "yyyy-MM-dd";
                    worksheet.Cell(targetRow, 3).Value = report.Waktu;
                    worksheet.Cell(targetRow, 3).Style.NumberFormat.Format = "hh:mm";
                    worksheet.Cell(targetRow, 4).Value = report.Nama;
                    worksheet.Cell(targetRow, 5).Value = report.Nik;
                    worksheet.Cell(targetRow, 6).Value = report.Departemen ?? "";
                    worksheet.Cell(targetRow, 7).Value = report.Area ?? "";
                    worksheet.Cell(targetRow, 8).Value = report.Lokasi ?? "";
                    worksheet.Cell(targetRow, 9).Value = report.DetilLokasi ?? "";
                    worksheet.Cell(targetRow, 10).Value = report.Topik ?? "";
                    worksheet.Cell(targetRow, 11).Value = report.Judul ?? "";
                    worksheet.Cell(targetRow, 12).Value = report.Keterangan ?? "";
                    worksheet.Cell(targetRow, 13).Value = report.ListPertanyaan ?? "";
                    worksheet.Cell(targetRow, 14).Value = report.Jawaban ?? "";
                    worksheet.Cell(targetRow, 15).Value = report.Catatan ?? "";

                    workbook.Save();
                }
            }
        }

        public void UpdateActionPlan(ActionPlan report)
        {
            lock (_lock)
            {
                EnsureFileExists();
                using (var workbook = new XLWorkbook(FilePath))
                {
                    var worksheet = workbook.Worksheet("ACTION PLAN");
                    var rows = worksheet.RowsUsed().Skip(1); // Skip header row

                    foreach (var row in rows)
                    {
                        var cellNik = row.Cell(6).GetString().Trim();
                        if (cellNik == report.Nik.Trim())
                        {
                            // Match Date and Time for uniqueness
                            bool dateMatch = false;
                            if (row.Cell(3).DataType == XLDataType.DateTime)
                            {
                                dateMatch = row.Cell(3).GetDateTime().Date == report.Tanggal.Date;
                            }
                            else
                            {
                                if (DateTime.TryParse(row.Cell(3).GetString(), out var excelDate))
                                {
                                    dateMatch = excelDate.Date == report.Tanggal.Date;
                                }
                            }

                            if (dateMatch)
                            {
                                // Found row! Update all resolution details
                                row.Cell(2).Value = report.FotoPerbaikan ?? "";
                                row.Cell(14).Value = report.Status ?? "Open";
                                row.Cell(18).Value = report.Pic ?? "";
                                row.Cell(19).Value = report.NikPic ?? "";
                                row.Cell(20).Value = report.DepartemenPic ?? "";
                                row.Cell(21).Value = report.RencanaPerbaikan ?? "";
                                
                                if (report.TanggalRencanaPerbaikan.HasValue)
                                {
                                    row.Cell(22).Value = report.TanggalRencanaPerbaikan.Value;
                                    row.Cell(22).Style.DateFormat.Format = "yyyy-MM-dd";
                                }
                                else
                                {
                                    row.Cell(22).Value = "";
                                }

                                row.Cell(23).Value = report.Perbaikan ?? "";

                                if (report.TanggalPerbaikan.HasValue)
                                {
                                    row.Cell(24).Value = report.TanggalPerbaikan.Value;
                                    row.Cell(24).Style.DateFormat.Format = "yyyy-MM-dd";
                                }
                                else
                                {
                                    row.Cell(24).Value = "";
                                }

                                row.Cell(25).Value = report.Overdue ?? "";
                                row.Cell(26).Value = report.AlasanOverdue ?? "";

                                workbook.Save();
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}
