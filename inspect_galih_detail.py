import pyodbc

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    nik = "23021900738"
    
    print("=== INSPECTIONS FOR GALIH ===")
    cursor.execute("SELECT * FROM tbl_t_inspection WHERE nik = ? AND tanggal >= '2026-09-01' AND is_deleted = 0", (nik,))
    cols = [c[0] for c in cursor.description]
    for row in cursor.fetchall():
        print(dict(zip(cols, row)))
        
    print("\n=== SAFETY TALKS FOR GALIH ===")
    cursor.execute("SELECT * FROM tbl_t_safety_talk WHERE nik = ? AND tanggal >= '2026-09-01' AND is_deleted = 0", (nik,))
    cols = [c[0] for c in cursor.description]
    for row in cursor.fetchall():
        print(dict(zip(cols, row)))

if __name__ == "__main__":
    main()
