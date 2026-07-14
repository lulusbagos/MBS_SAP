# Dokumentasi Pembaruan: Pop-up Pengaturan Roster Kerja & Cuti

Pembaruan ini menambahkan fitur **Pencatatan Roster Kerja & Cuti** bagi pengguna. Fitur ini dirancang untuk mencatat masa dinas aktif (*onsite*) dan masa cuti (*offsite*) masing-masing NIK ke dalam database, yang nantinya akan digunakan untuk kalkulasi target target KPI keselamatan (K3) secara proporsional.

---

## 1. Struktur Database (`tbl_m_roster`)
Tabel `tbl_m_roster` dibuat secara otomatis pada startup aplikasi via inisialisasi SQL di `Program.cs`.

* **`id`** (`int`, Primary Key, Identity): Identifier unik baris.
* **`nik`** (`nvarchar(50)`, Not Null): Nomor Induk Karyawan pengguna.
* **`awal_dinas`** (`date`, Not Null): Tanggal mulai dinas *onsite*.
* **`akhir_dinas`** (`date`, Not Null): Tanggal akhir dinas *onsite*.
* **`awal_cuti`** (`date`, Not Null): Tanggal mulai cuti *offsite*.
* **`akhir_cuti`** (`date`, Not Null): Tanggal akhir cuti *offsite*.
* **`created_at`** (`datetime`, Default `GETDATE()`): Waktu pembuatan record.
* **`updated_at`** (`datetime`, Null): Waktu pembaruan record terakhir.

---

## 2. File yang Dibuat & Dimodifikasi

### Backend / Database
1. **[Models/Roster.cs](file:///d:/4.%20PROJECT/2.%20Web/MBS_SAP/Models/Roster.cs) (Baru)**:
   - Model Entity Framework Core untuk tabel `tbl_m_roster`.
2. **[Data/AppDbContext.cs](file:///d:/4.%20PROJECT/2.%20Web/MBS_SAP/Data/AppDbContext.cs) (Modifikasi)**:
   - Registrasi `DbSet<Roster> Rosters` dan konfigurasi primary key/table mapping.
3. **[Program.cs](file:///d:/4.%20PROJECT/2.%20Web/MBS_SAP/Program.cs) (Modifikasi)**:
   - Script SQL otomatis untuk mendeteksi dan membuat tabel `tbl_m_roster` saat aplikasi dinyalakan.
4. **[Controllers/HomeController.cs](file:///d:/4.%20PROJECT/2.%20Web/MBS_SAP/Controllers/HomeController.cs) (Modifikasi)**:
   - Mengambil data roster terbaru per NIK.
   - Pengecekan status hari ini: Jika hari ini jatuh di antara masa cuti (`AwalCuti` s.d. `AkhirCuti`), maka parameter `ViewData["ShowRosterPopup"]` diset `false` (popup otomatis tidak akan mengganggu pengguna selama masa cuti).
   - Mengirimkan tanggal dinas/cuti aktif melalui `ViewData` untuk mem-prefill kolom isian form.
5. **[Controllers/ApiController.cs](file:///d:/4.%20PROJECT/2.%20Web/MBS_SAP/Controllers/ApiController.cs) (Modifikasi)**:
   - Endpoint POST `/Api/SaveRoster` untuk menyimpan data roster.
   - Validasi data logis di backend:
     - `AwalDinas` <= `AkhirDinas`
     - `AkhirDinas` < `AwalCuti`
     - `AwalCuti` <= `AkhirCuti`
   - Melakukan insert record roster baru jika roster terakhir sudah kadaluwarsa (untuk merekam sejarah roster masa lalu), atau melakukan update pada record aktif jika siklus masih berjalan.

### Frontend / UI
6. **[Views/Home/Index.cshtml](file:///d:/4.%20PROJECT/2.%20Web/MBS_SAP/Views/Home/Index.cshtml) (Modifikasi)**:
   - **Tombol Roster Kerja di Dashboard**: Menambahkan card menu "Roster Kerja" di area quick action grid dengan badge status dinamis (Aktif, Expired, atau Belum Set) agar user bisa membukanya secara manual kapan saja.
   - **Roster Settings Modal**: UI pop-up premium bernuansa frosted glassmorphic (`backdrop-filter`) yang responsif, lengkap dengan banner gradasi ungu-biru, pembagian card input dinas/cuti, dan info box K3.
   - **Backdrop Overrides**: Memodifikasi opacity tirai hitam modal menjadi `0.4` agar tampilan halaman di belakang pop-up tidak menjadi terlalu gelap/hitam pekat.

---

## 3. Logika & Mekanisme Khusus

### A. Sinkronisasi Berurutan dengan Pop-up Insiden (Anti-Bentrokan)
Untuk mencegah bentrokan visual di mana dua modal terbuka bersamaan pada saat halaman dimuat (membuat backdrop menumpuk dan layar macet/beku):
- Skrip memeriksa keberadaan dan status visibilitas modal insiden (`#incidentPopupModal`).
- Jika pop-up insiden dijadwalkan muncul (belum dibaca di sesi tab saat ini), skrip roster akan menahan kemunculannya secara otomatis, kemudian mendaftarkan trigger event `hidden.bs.modal` pada modal insiden.
- Begitu user menutup pop-up insiden, pop-up pengaturan roster akan muncul secara otomatis secara berurutan.

### B. Pembatasan Pop-up Harian (`localStorage`)
Untuk menjaga kenyamanan pengguna agar pop-up tidak mengganggu setiap kali halaman direfresh atau berpindah menu:
- Ketika pengguna menutup modal roster (klik tombol **Batal** atau tanda silang **X**) tanpa menyimpan, tanggal hari itu disimpan di `localStorage` sebagai `roster_popup_dismissed_date`.
- Pada setiap pemuatan halaman baru, skrip membandingkan tanggal hari ini dengan penyimpanan lokal. Jika cocok, kemunculan otomatis pop-up roster akan disembunyikan sepanjang hari itu.
- Data penolakan harian ini otomatis dihapus saat data roster berhasil disimpan dengan sukses.
