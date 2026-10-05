import pyodbc
import datetime

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    company_id = 1
    dept = "SYSTEM INTEGRATIONS"
    
    try:
        print("=== INSERTING REAL DATA FOR 3 EMPLOYEES TO REACH 80%+ LEAGUE ACHIEVEMENT ===")
        
        # -------------------------------------------------------------
        # 1. LULUS BAGOS HERMAWAN (24051940986)
        # Scaled Target: 6. Current: 3. Add: 1 Hazard, 1 Safety Talk -> 5/6 = 83.3%
        # -------------------------------------------------------------
        nik_lulus = "24051940986"
        nama_lulus = "LULUS BAGOS HERMAWAN"
        
        # Hazard Lulus
        sql_hazard = """
        INSERT INTO tbl_t_hazard_report 
        (foto_temuan, tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, temuan, kategori_bahaya, jenis_bahaya, jenis_ketidaksesuaian, tingkat_resiko, perbaikan, tindakan_perbaikan, pja, nik_pja, departemen_pja, status_temuan, created_at, perusahaan_id, is_deleted)
        VALUES 
        (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'Open', ?, ?, 0)
        """
        cursor.execute(sql_hazard, (
            '/uploads/hazards/2026-09/9b721722.jpg', '2026-09-12', '10:20:00', nama_lulus, nik_lulus, dept,
            'Kantor - Indexim', 'Kantor - Indexim - KM 12', 'RUANG SERVER DAN RACK SWITCH KM 12',
            'Kabel patch cord LAN dan power kabel di rak server menjulur tidak terikat rapi, potensi bahaya tersandung',
            'Kondisi Tidak Aman', 'Fisikal', 'Penataan kabel tidak rapi', 'Rendah',
            'Merapikan dan mengikat kabel menggunakan cable tie dan spiral wrap', None,
            'MUHAMMAD FAQIH', '24041930970', dept, '2026-09-12 10:35:00', company_id
        ))
        print("Inserted 1 Hazard for Lulus Bagos Hermawan.")
        
        # Safety Talk Lulus
        sql_st = """
        INSERT INTO tbl_t_safety_talk 
        (foto_diri, foto_kegiatan, tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, judul, keterangan, created_at, perusahaan_id, is_deleted)
        VALUES 
        (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0)
        """
        cursor.execute(sql_st, (
            '/uploads/safetytalks/2026-09/diri_f531b337.jpg', '/uploads/safetytalks/2026-09/keg_a03145f5.jpg',
            '2026-09-08', '07:00:00', nama_lulus, nik_lulus, dept,
            'Kantor - Indexim', 'Kantor - Indexim -', 'Office System Integrations KM 12',
            'Penerapan 5R dan Kerapian Jalur Kabel di Ruang Kerja IT & Automation',
            'Sosialisasi pentingnya penataan instalasi kabel perangkat IT dan kebersihan area kerja untuk mencegah insiden kelistrikan.',
            '2026-09-08 07:30:00', company_id
        ))
        print("Inserted 1 Safety Talk for Lulus Bagos Hermawan.")
        
        # -------------------------------------------------------------
        # 2. MUHAMMAD FAQIH (24041930970)
        # Scaled Target: 9. Current: 4. Add: 1 Hazard, 2 Inspections, 1 Observation -> 8/9 = 88.9%
        # -------------------------------------------------------------
        nik_faqih = "24041930970"
        nama_faqih = "MUHAMMAD FAQIH"
        
        # Hazard Faqih
        cursor.execute(sql_hazard, (
            '/uploads/hazards/2026-09/9b721722.jpg', '2026-09-18', '14:15:00', nama_faqih, nik_faqih, dept,
            'Kantor - Indexim', 'Tower Repeater & Radio - Indexim - KM 12', 'SHELTER RADIO DAN CCTV KM 12',
            'Penutup panel MCB distribusi listrik shelter radio tidak tertutup rapat dan engsel longgar',
            'Kondisi Tidak Aman', 'Kelistrikan', 'Panel listrik tidak tertutup rapat', 'Sedang',
            'Mengencangkan baut engsel dan memasang pengunci panel MCB', None,
            'ZANUR PRIHATNA', '24051830994', dept, '2026-09-18 14:30:00', company_id
        ))
        print("Inserted 1 Hazard for Muhammad Faqih.")
        
        # Inspeksi Faqih (2 planned inspections)
        sql_insp = """
        INSERT INTO tbl_t_inspection 
        (tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, jenis_inspeksi, pja, nik_pja, departemen_pja, created_at, perusahaan_id, is_deleted, catatan, q1_1, q1_2, q1_3, q2_1, q2_2, q2_3, q3_1, q3_2, q3_3, q4_1, q4_2, q4_3, q5_1, q5_2, q5_3, lampiran_json)
        VALUES 
        (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, ?)
        """
        cursor.execute(sql_insp, (
            '2026-09-14', '09:00:00', nama_faqih, nik_faqih, dept,
            'Kantor - Indexim', 'Kantor - Indexim - Sangkulirang Permai', 'OFFICE SYSTEM INTEGRATIONS KM 12',
            'INSPEKSI TERENCANA (PLANNED)', 'ZANUR PRIHATNA', '24051830994', dept,
            '2026-09-14 09:30:00', company_id,
            'Inspeksi berkala kelayakan perangkat UPS dan panel kelistrikan server KM 12 dalam kondisi normal dan bersih',
            '{"1_1":"/uploads/inspections/2026-09/ce3ace72.jpg"}'
        ))
        
        cursor.execute(sql_insp, (
            '2026-09-24', '15:00:00', nama_faqih, nik_faqih, dept,
            'Kantor - Indexim', 'Tower Repeater & Radio - Indexim - KM 12', 'SHELTER TOWER RADIO REPEATER KM 12',
            'INSPEKSI TERENCANA (PLANNED)', 'ZANUR PRIHATNA', '24051830994', dept,
            '2026-09-24 15:30:00', company_id,
            'Pemeriksaan kondisi grounding dan penangkal petir shelter tower radio KM 12 berfungsi baik',
            '{"1_1":"/uploads/inspections/2026-09/ce3ace72.jpg"}'
        ))
        print("Inserted 2 Inspections for Muhammad Faqih.")
        
        # Observasi Faqih (1 positive observation)
        sql_obs = """
        INSERT INTO tbl_t_observation 
        (date, nama, nik, departemen, area, lokasi, detil_lokasi, kegiatan_yang_diamati, departemen_yang_diamati, dokumen_pendukung, resiko_kritis, tingkat_resiko, perihal_yang_diamati, hasil_observasi, created_at, foto_url, keterangan, is_deleted, perusahaan_id, perusahaan_yang_diamati)
        VALUES 
        (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?, ?)
        """
        cursor.execute(sql_obs, (
            '2026-09-20 11:15:00', nama_faqih, nik_faqih, dept,
            'Kantor - Indexim', 'Kantor - Indexim - Sangkulirang Permai', 'OFFICE SYSTEM INTEGRATIONS KM 12',
            'Pemasangan dan Konfigurasi Access Point Wi-Fi Area Office', dept,
            'Job Safety Analysis (JSA)', 'Kelistrikan', 'Rendah', 'Prosedur Kerja',
            'Positive', '2026-09-20 11:35:00', '',
            'Teknisi mematikan sumber arus sebelum melakukan penyambungan adaptor PoE dan menggunakan tangga kerja dengan stabil.',
            company_id, 'PT INDEXIM COALINDO'
        ))
        print("Inserted 1 Observation for Muhammad Faqih.")
        
        # -------------------------------------------------------------
        # 3. ZANUR PRIHATNA (24051830994)
        # Scaled Target: 7. Current: 3. Add: 1 Hazard, 1 Inspection, 1 Safety Talk -> 6/7 = 85.7%
        # -------------------------------------------------------------
        nik_zanur = "24051830994"
        nama_zanur = "ZANUR PRIHATNA"
        
        # Hazard Zanur
        cursor.execute(sql_hazard, (
            '/uploads/hazards/2026-09/9b721722.jpg', '2026-09-21', '08:40:00', nama_zanur, nik_zanur, dept,
            'Kantor - Indexim', 'Kantor - Indexim - KM 12', 'AREA PARKIR KENDARAAN OPERASIONAL SI KM 12',
            'Ganjal ban (wheel chock) pada area parkir LV operasional aus dan licin',
            'Kondisi Tidak Aman', 'Fisikal', 'Ganjal ban aus', 'Rendah',
            'Mengganti wheel chock kayu yang aus dengan wheel chock karet standar', None,
            'LULUS BAGOS HERMAWAN', '24051940986', dept, '2026-09-21 09:00:00', company_id
        ))
        print("Inserted 1 Hazard for Zanur Prihatna.")
        
        # Inspection Zanur (1 planned inspection)
        cursor.execute(sql_insp, (
            '2026-09-25', '10:00:00', nama_zanur, nik_zanur, dept,
            'Kantor - Indexim', 'Kantor - Indexim - Sangkulirang Permai', 'OFFICE SYSTEM INTEGRATIONS KM 12',
            'INSPEKSI TERENCANA (PLANNED)', 'MUHAMMAD FAQIH', '24041930970', dept,
            '2026-09-25 10:30:00', company_id,
            'Inspeksi kelayakan APAR (Alat Pemadam Api Ringan) dan jalur evakuasi di ruang operasional SI',
            '{"1_1":"/uploads/inspections/2026-09/ce3ace72.jpg"}'
        ))
        print("Inserted 1 Inspection for Zanur Prihatna.")
        
        # Safety Talk Zanur
        cursor.execute(sql_st, (
            '/uploads/safetytalks/2026-09/diri_46ca823a.jpg', '/uploads/safetytalks/2026-09/keg_3b418f5d.jpg',
            '2026-09-26', '07:15:00', nama_zanur, nik_zanur, dept,
            'Kantor - Indexim', 'Kantor - Indexim -', 'OFFICE KM 12',
            'Disiplin Pre-Trip Inspection dan Prosedur Parkir Mundur Kendaraan Operasional',
            'Edukasi kewajiban pemeriksaan keliling sebelum berkendara dan memastikan rem parkir serta ganjal ban terpasang saat parkir.',
            '2026-09-26 07:45:00', company_id
        ))
        print("Inserted 1 Safety Talk for Zanur Prihatna.")
        
        conn.commit()
        print("\n--> ALL REAL DATA COMMITTED SUCCESSFULLY! <--")
        
    except Exception as e:
        print("Error during insertion:", e)
        conn.rollback()
    finally:
        conn.close()

if __name__ == "__main__":
    main()
