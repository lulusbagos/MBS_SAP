import pyodbc

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    print("=== SAMPLE HAZARDS IN SYSTEM INTEGRATIONS / IT ===")
    cursor.execute("""
        SELECT TOP 3 id, tanggal, nama, nik, departemen, area, lokasi, detil_lokasi, temuan, kategori_bahaya, jenis_bahaya, jenis_ketidaksesuaian, tingkat_resiko, perbaikan, pja, nik_pja, departemen_pja
        FROM tbl_t_hazard_report 
        WHERE departemen LIKE '%SYSTEM%' OR departemen LIKE '%INTEGRATION%' OR departemen LIKE '%IT%'
        ORDER BY id DESC
    """)
    cols = [col[0] for col in cursor.description]
    for row in cursor.fetchall():
        print(dict(zip(cols, row)))
        
    print("\n=== SAMPLE INSPECTIONS IN SYSTEM INTEGRATIONS / IT ===")
    cursor.execute("""
        SELECT TOP 3 id, tanggal, nama, nik, departemen, area, lokasi, detil_lokasi, jenis_inspeksi, catatan
        FROM tbl_t_inspection 
        WHERE departemen LIKE '%SYSTEM%' OR departemen LIKE '%INTEGRATION%' OR departemen LIKE '%IT%'
        ORDER BY id DESC
    """)
    cols = [col[0] for col in cursor.description]
    for row in cursor.fetchall():
        print(dict(zip(cols, row)))

    print("\n=== SAMPLE SAFETY TALKS IN SYSTEM INTEGRATIONS / IT ===")
    cursor.execute("""
        SELECT TOP 3 id, tanggal, nama, nik, departemen, area, lokasi, detil_lokasi, judul, keterangan
        FROM tbl_t_safety_talk 
        WHERE departemen LIKE '%SYSTEM%' OR departemen LIKE '%INTEGRATION%' OR departemen LIKE '%IT%'
        ORDER BY id DESC
    """)
    cols = [col[0] for col in cursor.description]
    for row in cursor.fetchall():
        print(dict(zip(cols, row)))

    print("\n=== SAMPLE OBSERVATIONS IN SYSTEM INTEGRATIONS / IT ===")
    cursor.execute("""
        SELECT TOP 3 id, date, nama, nik, departemen, area, lokasi, detil_lokasi, kegiatan_yang_diamati, departemen_yang_diamati, resiko_kritis, tingkat_resiko, perihal_yang_diamati, hasil_observasi, keterangan
        FROM tbl_t_observation 
        WHERE departemen LIKE '%SYSTEM%' OR departemen LIKE '%INTEGRATION%' OR departemen LIKE '%IT%'
        ORDER BY id DESC
    """)
    cols = [col[0] for col in cursor.description]
    for row in cursor.fetchall():
        print(dict(zip(cols, row)))

if __name__ == "__main__":
    main()
