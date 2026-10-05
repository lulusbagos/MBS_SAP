import pyodbc

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    print("--- SAMPLE OBSERVATION PHOTOS ---")
    cursor.execute("SELECT TOP 3 foto_url, keterangan FROM tbl_t_observation WHERE foto_url IS NOT NULL ORDER BY id DESC")
    for r in cursor.fetchall():
        print(r)
        
    print("\n--- SAMPLE COACHING PHOTOS ---")
    cursor.execute("SELECT TOP 3 foto, tema FROM tbl_t_coaching WHERE foto IS NOT NULL ORDER BY id DESC")
    for r in cursor.fetchall():
        print(r)
        
    print("\n--- SAMPLE SAFETY TALK PHOTOS ---")
    cursor.execute("SELECT TOP 3 foto_diri, foto_kegiatan, judul FROM tbl_t_safety_talk WHERE foto_diri IS NOT NULL ORDER BY id DESC")
    for r in cursor.fetchall():
        print(r)

    print("\n--- SAMPLE INSPECTION CATATAN & LAMPIRAN ---")
    cursor.execute("SELECT TOP 3 catatan, lampiran_json FROM tbl_t_inspection WHERE lampiran_json IS NOT NULL ORDER BY id DESC")
    for r in cursor.fetchall():
        print(r)

if __name__ == "__main__":
    main()
