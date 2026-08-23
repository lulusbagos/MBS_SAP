import pyodbc
conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"
try:
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    # 1. Check if employee is in Karyawans query
    sql1 = """
    SELECT k.id_karyawan, k.no_nik, p.nama_lengkap, d.nama_departemen, j.nama_jabatan, k.id_perusahaan
    FROM vw_karyawan k
    JOIN vw_personal p ON k.id_personal = p.id_personal
    LEFT JOIN vw_departemen d ON k.id_departemen = d.departemen_id
    LEFT JOIN vw_jabatan j ON k.id_jabatan = j.jabatan_id
    WHERE k.id_perusahaan = 277 AND k.status_aktif = 1 AND k.no_nik = '91006173'
    """
    cursor.execute(sql1)
    row1 = cursor.fetchone()
    if row1:
        print(f"deptKaryawans query OK: {row1}")
        k_id = row1[0]
        
        # 2. Check mapping dict
        sql2 = "SELECT target_inspeksi, target_observasi, target_hazard_report, target_coaching, target_safety_talk FROM vw_r_karyawan_jabatan_mapping_preview WHERE karyawan_id = ?"
        cursor.execute(sql2, (k_id,))
        row2 = cursor.fetchone()
        if row2:
            print(f"Mapping OK: {row2}")
        else:
            print("No mapping found for this karyawan_id")
    else:
        print("Not found in deptKaryawans query! (Join failed or status_aktif is false or wrong perusahaan)")
except Exception as e:
    print("Error:", e)
