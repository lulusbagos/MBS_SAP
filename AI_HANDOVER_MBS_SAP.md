# MBS SAP - Project Context & AI Handover (Update 30 Juni 2026)

Dokumen ini berisi informasi kontekstual yang sangat penting mengenai arsitektur, aturan bisnis (business rules), dan perbaikan bug terbaru pada proyek MBS SAP. Dokumen ini dirancang agar agen AI selanjutnya dapat langsung memahami state proyek tanpa perlu meraba-raba dari awal.

## 1. Sistem Autentikasi & Login (PENTING!)
Sebelumnya terdapat bug di mana tombol login/submit tidak merespons (klik di-disable tapi form tidak terkirim).
- **Aturan UI Tombol Submit:** JANGAN PERNAH menggunakan `$(this).prop('disabled', true)` langsung pada event `click` atau `submit` untuk tombol form standar, karena itu akan membatalkan event submit bawaan browser.
- **Solusi yang Diterapkan:** Gunakan CSS `pointer-events: none; opacity: 0.7;` pada `_Layout.cshtml` dan `site.js` untuk mencegah double-submit tanpa membunuh event trigger form.
- Class `.btn-click-lock` menangani ini secara global.

## 2. Company Hierarchy Filter (Keamanan Data)
Aplikasi ini memiliki struktur perusahaan multi-tenant dengan relasi Induk-Anak (Parent-Child).
- **Service:** `CompanyHierarchyService`
- **Aturan Mutlak:** Semua controller yang menampilkan data (`HazardController`, `InspectionController`, `ActionPlanController`, `SafetyTalkController`) **WAJIB** memfilter data berdasarkan hierarki perusahaan user yang login.
- **Admin Tidak Mengecualikan Filter:** Walaupun user adalah "Admin", mereka tetap hanya boleh melihat data dari perusahaan mereka sendiri beserta anak/cucu perusahaannya. **Jangan bypass filter jika `User.IsInRole("Admin")`**.
- **Cara Penggunaan Filter yang Benar:**
  ```csharp
  var companyIdStr = User.FindFirst("CompanyId")?.Value;
  if (int.TryParse(companyIdStr, out var cid) && cid > 0)
  {
      var allowedIds = await _companyHierarchyService.GetAccessibleCompanyIdsAsync(cid);
      query = query.Where(x => x.PerusahaanId.HasValue && allowedIds.Contains(x.PerusahaanId.Value));
  }
  ```

## 3. Sistem Notifikasi (Baru Saja Diupgrade)
Sistem notifikasi telah diupgrade untuk mendukung UI yang dinamis, ikon profesional, dan notifikasi interaksi.
- **Perubahan Model:** Tabel `tbl_t_notifications` (Model `Notification.cs`) telah ditambahkan kolom `notif_type` (tipe: `NVARCHAR(50)`).
- **Nilai Valid `notif_type`:**
  - `hazard_new`, `hazard_reassign`
  - `inspection_new`, `inspection_reassign`
  - `actionplan_new`, `actionplan_reassign`
  - `timeline_like`, `timeline_comment`
  - `general` (default)
- **UI Frontend:** Di dalam `_Layout.cshtml`, fungsi JS `fetchNotifications()` membaca `notifType` untuk mewarnai dan memberikan ikon khusus (`getNotificationConfig`).
- **TODO Database (Penting untuk AI Berikutnya):** Kolom `notif_type` baru saja ditambahkan via EF Core Migration `AddNotifTypeToNotification`. Jika ada error SQL *"Invalid column name 'notif_type'"*, artinya migration belum dijalankan di server SQL (`dotnet ef database update`).

## 4. Interaksi Timeline (SafeFeed)
- Mengomentari atau me-like postingan di `TimelineController` sekarang men-trigger `Notification` ke pemilik postingan.
- Metode `GetItemOwnerNikAsync` digunakan untuk mencari NIK pembuat berdasarkan `ItemType` (`Hazard`, `Inspection`, `P5m`, dll) dan `ItemId`.

## 5. Known Issues / Workflow Constraints
- **File Lock pada .NET Build:** Proses `dotnet run` sering mengunci file `.exe` di Windows (`error MSB3027`). Jika perlu mem-build/merubah kode C#, server **wajib** dimatikan manual oleh user via Terminal (`Ctrl+C`), karena agen AI tidak bisa membunuh proses yang diluncurkan manual oleh user.
- Jangan gunakan *background tasks* untuk menjalankan server secara otomatis jika user keberatan, karena user lebih suka memegang kontrol manual pada proses terminal.

---
*Gunakan file ini sebagai rujukan utama sebelum mengubah controller, sistem login, atau notifikasi.*
