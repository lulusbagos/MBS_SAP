
INSERT INTO tbl_t_hazard_report (foto_temuan, tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, temuan, kategori_bahaya, jenis_bahaya, jenis_ketidaksesuaian, tingkat_resiko, perbaikan, tindakan_perbaikan, pja, nik_pja, departemen_pja, status_temuan, created_at, perusahaan_id, is_deleted)
VALUES 
('', '2026-08-10', '10:00:00', 'MUHAMAD ANDRYAN RASYID', '23091840871', 'SYSTEM INTEGRATIONS', 'Office', 'Office', 'Ruang IT', 'Kabel Berantakan', 'KTA', 'Fisik', 'Lain-lain', 'Rendah', 'Dirapihkan', 'Sudah Dirapihkan', 'PIC', '12345', 'IT', 'Closed', GETDATE(), 1, 0),
('', '2026-08-11', '10:00:00', 'MUHAMMAD ALFIAN YUSTIANDA', '24011950928', 'SYSTEM INTEGRATIONS', 'Office', 'Office', 'Ruang IT', 'Lantai Licin', 'KTA', 'Fisik', 'Lain-lain', 'Rendah', 'Dibersihkan', 'Sudah Dibersihkan', 'PIC', '12345', 'IT', 'Closed', GETDATE(), 1, 0),
('', '2026-08-12', '10:00:00', 'ZANUR PRIHATNA', '24051830994', 'SYSTEM INTEGRATIONS', 'Office', 'Office', 'Ruang IT', 'Kursi Rusak', 'KTA', 'Fisik', 'Lain-lain', 'Rendah', 'Diganti', 'Sudah Diganti', 'PIC', '12345', 'IT', 'Closed', GETDATE(), 1, 0),
('', '2026-08-13', '10:00:00', 'LULUS BAGOS HERMAWAN', '24051940986', 'SYSTEM INTEGRATIONS', 'Office', 'Office', 'Ruang IT', 'Lampu Mati', 'KTA', 'Fisik', 'Lain-lain', 'Rendah', 'Diganti', 'Sudah Diganti', 'PIC', '12345', 'IT', 'Closed', GETDATE(), 1, 0);

INSERT INTO tbl_t_observation (date, nama, nik, departemen, area, lokasi, detil_lokasi, kegiatan_yang_diamati, departemen_yang_diamati, dokumen_pendukung, resiko_kritis, tingkat_resiko, perihal_yang_diamati, hasil_observasi, created_at, foto_url, keterangan, is_deleted)
VALUES
('2026-08-10', 'MUHAMAD ANDRYAN RASYID', '23091840871', 'SYSTEM INTEGRATIONS', 'Office', 'Office', 'Ruang IT', 'Maintenance Server', 'SYSTEM INTEGRATIONS', 'JSA', 'Tidak', 'Rendah', 'Aman', 'Aman', GETDATE(), '', 'Testing KPI', 0),
('2026-08-11', 'MUHAMMAD ALFIAN YUSTIANDA', '24011950928', 'SYSTEM INTEGRATIONS', 'Office', 'Office', 'Ruang IT', 'Maintenance Jaringan', 'SYSTEM INTEGRATIONS', 'JSA', 'Tidak', 'Rendah', 'Aman', 'Aman', GETDATE(), '', 'Testing KPI', 0),
('2026-08-12', 'ZANUR PRIHATNA', '24051830994', 'SYSTEM INTEGRATIONS', 'Office', 'Office', 'Ruang IT', 'Maintenance PC', 'SYSTEM INTEGRATIONS', 'JSA', 'Tidak', 'Rendah', 'Aman', 'Aman', GETDATE(), '', 'Testing KPI', 0),
('2026-08-13', 'LULUS BAGOS HERMAWAN', '24051940986', 'SYSTEM INTEGRATIONS', 'Office', 'Office', 'Ruang IT', 'Maintenance Kabel', 'SYSTEM INTEGRATIONS', 'JSA', 'Tidak', 'Rendah', 'Aman', 'Aman', GETDATE(), '', 'Testing KPI', 0);

