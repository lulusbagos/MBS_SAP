import pyodbc
import json

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    nik = "23021900738"
    
    print("=== EXISTING HAZARD REPORT FOR GALIH ===")
    cursor.execute("""
        SELECT TOP 5 id, tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, temuan, kategori_bahaya, jenis_bahaya, tingkat_resiko, status_temuan, perbaikan, tindakan_perbaikan, pja, nik_pja, departemen_pja
        FROM tbl_t_hazard_report 
        WHERE nik = ? AND tanggal >= '2026-09-01'
    """, (nik,))
    cols = [col[0] for col in cursor.description]
    for row in cursor.fetchall():
        print(dict(zip(cols, row)))
        
    print("\n=== EXISTING INSPECTION FOR GALIH ===")
    cursor.execute("""
        SELECT TOP 5 id, tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, jenis_inspeksi, shift
        FROM tbl_t_inspection 
        WHERE nik = ? AND tanggal >= '2026-09-01'
    """, (nik,))
    cols = [col[0] for col in cursor.description]
    for row in cursor.fetchall():
        print(dict(zip(cols, row)))

    print("\n=== EXISTING SAFETY TALKS FOR GALIH ===")
    cursor.execute("""
        SELECT TOP 5 id, tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, judul_materi, jumlah_peserta, shift
        FROM tbl_t_safety_talk 
        WHERE nik = ? AND tanggal >= '2026-09-01'
    """, (nik,))
    cols = [col[0] for col in cursor.description]
    for row in cursor.fetchall():
        print(dict(zip(cols, row)))

    print("\n=== SAMPLE OBSERVATIONS IN HC&GS / INDEXIM ===")
    cursor.execute("""
        SELECT TOP 3 id, date, nama, nik, departemen, area, lokasi, detil_lokasi, kegiatan_yang_diamati, departemen_yang_diamati, resiko_kritis, tingkat_resiko, perihal_yang_diamati, hasil_observasi, rekomendasi
        FROM tbl_t_observation 
        WHERE departemen LIKE '%HUMAN%' OR departemen LIKE '%HC%' OR departemen LIKE '%GENERAL%'
        ORDER BY created_at DESC
    """)
    cols = [col[0] for col in cursor.description]
    for row in cursor.fetchall():
        print(dict(zip(cols, row)))

    print("\n=== SAMPLE COACHINGS IN HC&GS / INDEXIM ===")
    cursor.execute("""
        SELECT TOP 3 id, date, created_at, nama, nik, departemen, area, lokasi, detil_lokasi, topik_coaching, jenis_coaching, ringkasan_coaching, feedback, komitmen
        FROM tbl_t_coaching 
        WHERE departemen LIKE '%HUMAN%' OR departemen LIKE '%HC%' OR departemen LIKE '%GENERAL%'
        ORDER BY created_at DESC
    """)
    cols = [col[0] for col in cursor.description]
    for row in cursor.fetchall():
        print(dict(zip(cols, row)))

    print("\n=== SAMPLE COACHING PARTICIPANTS ===")
    cursor.execute("""
        SELECT TOP 5 cp.id, cp.coaching_id, cp.nik, cp.nama, cp.departemen, cp.jabatan
        FROM tbl_t_coaching_participant cp
        ORDER BY cp.id DESC
    """)
    cols = [col[0] for col in cursor.description]
    for row in cursor.fetchall():
        print(dict(zip(cols, row)))

if __name__ == "__main__":
    main()
