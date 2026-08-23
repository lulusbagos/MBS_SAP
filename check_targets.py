import pyodbc
conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"
try:
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    sql = """
    SELECT m.target_inspeksi, m.target_observasi, m.target_hazard_report, m.target_coaching, m.target_safety_talk
    FROM vw_r_karyawan_jabatan_mapping_preview m
    JOIN vw_karyawan k ON m.karyawan_id = k.id_karyawan
    WHERE k.no_nik = ?
    """
    cursor.execute(sql, ("91006173",))
    row = cursor.fetchone()
    if row:
        print("TARGETS UNTUK NIK 91006173:")
        print(f"Inspeksi: {row[0]}")
        print(f"Observasi: {row[1]}")
        print(f"Hazard: {row[2]}")
        print(f"Coaching: {row[3]}")
        print(f"Safety Talk: {row[4]}")
    else:
        print("NIK not found in join")
except Exception as e:
    print("Error:", e)
