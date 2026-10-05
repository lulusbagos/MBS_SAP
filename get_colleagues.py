import pyodbc

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    cursor.execute("""
        SELECT k.no_nik, p.nama_lengkap, j.nama_jabatan
        FROM vw_karyawan k
        JOIN vw_personal p ON k.id_personal = p.id_personal
        LEFT JOIN vw_jabatan j ON k.id_jabatan = j.jabatan_id
        WHERE k.id_perusahaan = 1 AND k.id_departemen = 11 AND k.status_aktif = 1
        ORDER BY p.nama_lengkap
    """)
    for r in cursor.fetchall():
        print(f"{r.no_nik} - {r.nama_lengkap} ({r.nama_jabatan})")

if __name__ == "__main__":
    main()
