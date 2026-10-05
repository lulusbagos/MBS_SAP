import pyodbc
import datetime

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    nik = "23021900738"
    nama = "GALIH DWI RESPATI"
    dept = "HUMAN CAPITAL & GENERAL SERVICES"
    company_id = 1
    
    try:
        print("=== INSERTING REAL DATA FOR GALIH DWI RESPATI ===")
        
        # 1. Inspection (1 planned inspection)
        sql_insp = """
        INSERT INTO tbl_t_inspection 
        (tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, jenis_inspeksi, pja, nik_pja, departemen_pja, created_at, perusahaan_id, is_deleted, catatan, q1_1, q1_2, q1_3, q2_1, q2_2, q2_3, q3_1, q3_2, q3_3, q4_1, q4_2, q4_3, q5_1, q5_2, q5_3, lampiran_json)
        VALUES 
        (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, ?)
        """
        cursor.execute(sql_insp, (
            '2026-09-25', '09:30:00', nama, nik, dept,
            'Kantor - Indexim', 'Kantor - Indexim - Sangkulirang Permai', 'AREA OFFICE DAN CANTEEN KM 14',
            'INSPEKSI TERENCANA (PLANNED)', 'DIMAS HARYO SETO WASKITO', '25061831137', dept,
            '2026-09-25 09:45:00', company_id,
            'Inspeksi berkala kebersihan dan fasilitas keselamatan di area kantin dan office Km 14 dalam kondisi baik dan rapi',
            '{"1_1":"/uploads/inspections/2026-09/ce3ace72.jpg"}'
        ))
        print("Inserted 1 Inspection.")
        
        # 2. Safety Talks (2 safety talks)
        sql_st = """
        INSERT INTO tbl_t_safety_talk 
        (foto_diri, foto_kegiatan, tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, judul, keterangan, created_at, perusahaan_id, is_deleted)
        VALUES 
        (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0)
        """
        cursor.execute(sql_st, (
            '/uploads/safetytalks/2026-09/diri_f531b337.jpg', '/uploads/safetytalks/2026-09/keg_a03145f5.jpg',
            '2026-09-23', '06:45:00', nama, nik, dept,
            'Mess & Fasilitas - Indexim', 'Mess & Fasilitas - Indexim - Mess Km 13', 'MESS AREA KM 13',
            'Pentingnya Housekeeping dan Pengelolaan Sampah Domestik di Area Mess',
            'Edukasi kepada seluruh penghuni mess untuk menjaga kebersihan kamar dan pemilahan sampah organik/anorganik.',
            '2026-09-23 07:15:00', company_id
        ))
        
        cursor.execute(sql_st, (
            '/uploads/safetytalks/2026-09/diri_46ca823a.jpg', '/uploads/safetytalks/2026-09/keg_3b418f5d.jpg',
            '2026-09-26', '06:50:00', nama, nik, dept,
            'Kantor - Indexim', 'Kantor - Indexim -', 'OFFICE SANGKULIRANG PERMAI',
            'Penerapan Ergonomi Kerja dan Pencegahan Fatigue di Tempat Kerja',
            'Pembahasan posisi duduk ergonomis saat bekerja di depan komputer dan pentingnya istirahat cukup untuk mencegah kelelahan.',
            '2026-09-26 07:20:00', company_id
        ))
        print("Inserted 2 Safety Talks.")
        
        # 3. Observations (2 observations)
        sql_obs = """
        INSERT INTO tbl_t_observation 
        (date, nama, nik, departemen, area, lokasi, detil_lokasi, kegiatan_yang_diamati, departemen_yang_diamati, dokumen_pendukung, resiko_kritis, tingkat_resiko, perihal_yang_diamati, hasil_observasi, created_at, foto_url, keterangan, is_deleted, perusahaan_id, perusahaan_yang_diamati)
        VALUES 
        (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?, ?)
        """
        cursor.execute(sql_obs, (
            '2026-09-22 10:15:00', nama, nik, dept,
            'Mess & Fasilitas - Indexim', 'Mess & Fasilitas - Indexim - Dapur Mess Km 13', 'AREA DAPUR DAN RUANG MAKAN MESS KM 13',
            'Penyiapan Makanan dan Penggunaan APD Higienitas Katering', dept,
            'Standar Operasional Prosedur (SOP)', 'Kesehatan & Higienitas Lingkungan Kerja', 'Rendah', 'Prosedur Kerja',
            'Positive', '2026-09-22 10:30:00', '',
            'Petugas katering mengenakan sarung tangan, apron, penutup kepala, dan sepatu keselamatan secara lengkap saat mengolah makanan.',
            company_id, 'PT INDEXIM COALINDO'
        ))
        
        cursor.execute(sql_obs, (
            '2026-09-25 14:20:00', nama, nik, dept,
            'Kantor - Indexim', 'Kantor - Indexim - Sangkulirang Permai', 'AREA GUDANG GENERAL SERVICES KM 14',
            'Penataan dan Pemindahan Logistik Inventaris Kantor', dept,
            'Job Safety Analysis (JSA)', 'Manual Handling', 'Rendah', 'Prosedur Kerja',
            'Positive', '2026-09-25 14:40:00', '',
            'Pekerja menerapkan teknik manual handling yang benar dengan menekuk lutut bukan membungkuk saat mengangkat barang seberat 15kg.',
            company_id, 'PT INDEXIM COALINDO'
        ))
        print("Inserted 2 Observations.")
        
        # 4. Coaching (1 coaching + 1 participant)
        sql_coach = """
        INSERT INTO tbl_t_coaching 
        (foto, tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, tema, feedback, komitmen, perusahaan_id, is_deleted, created_at)
        OUTPUT INSERTED.id
        VALUES 
        (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?)
        """
        cursor.execute(sql_coach, (
            '/uploads/CoachingKegiatan/2026-09/24081681018_991a1445.heic',
            '2026-09-24', '11:00:00', nama, nik, dept,
            'Kantor - Indexim', 'Kantor - Indexim - Sangkulirang Permai', 'RUANG MEETING GENERAL SERVICES KM 14',
            'Coaching Kepatuhan Program SAP dan Pelaporan Hazard Report',
            'Memberikan pemahaman mendalam tentang pentingnya identifikasi bahaya di lingkungan mess dan fasilitas kantor, serta cara pelaporan melalui aplikasi MBS SAP.',
            'Peserta berkomitmen untuk aktif mengidentifikasi potensi bahaya di lingkungan kerja masing-masing dan menyelesaikan target bulanan SAP tepat waktu.',
            company_id, '2026-09-24 11:30:00'
        ))
        coach_id = cursor.fetchone()[0]
        print(f"Inserted 1 Coaching with ID: {coach_id}")
        
        sql_part = """
        INSERT INTO tbl_t_coaching_participant 
        (coaching_id, nik, nama)
        VALUES 
        (?, ?, ?)
        """
        cursor.execute(sql_part, (coach_id, '24031780956', 'BAYU RIA KUSTAMAN'))
        print("Inserted Coaching Participant (BAYU RIA KUSTAMAN).")
        
        conn.commit()
        print("\n--> ALL REAL DATA COMMITTED SUCCESSFULLY! <--")
        
    except Exception as e:
        print("Error during insertion:", e)
        conn.rollback()
    finally:
        conn.close()

if __name__ == "__main__":
    main()
