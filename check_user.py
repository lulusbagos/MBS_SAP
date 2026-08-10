import pyodbc

# Connect to DB_SAP
conn_str = 'DRIVER={ODBC Driver 17 for SQL Server};SERVER=172.16.1.93;DATABASE=DB_SAP;UID=sa;PWD=technical.indexim.123;'
try:
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    cursor.execute("""
        SELECT id_karyawan, no_nik, id_perusahaan, status_aktif
        FROM vw_karyawan
        WHERE no_nik = 'MGE-2401-008-2'
    """)
    rows = cursor.fetchall()
    print('vw_karyawan in DB_SAP:')
    for r in rows:
        print(r)
        
    cursor.execute("""
        SELECT *
        FROM vw_karyawan
        WHERE no_nik = 'MGE-2401-008-2'
    """)
    rows = cursor.fetchall()
    print('vw_karyawan ALL rows:')
    for r in rows:
        print(r)
        
    cursor.execute("""
        SELECT username, is_aktif
        FROM vw_pengguna
        WHERE username = 'MGE-2401-008-2'
    """)
    penggunas = cursor.fetchall()
    print('vw_pengguna for this NIK:')
    for p in penggunas:
        print(p)
except Exception as e:
    print('Error connecting to DB_SAP:', e)
