import pyodbc

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    nik = "23091840871"
    
    try:
        # Update the restored inspection to have a date in August 2026
        cursor.execute("UPDATE tbl_t_inspection SET tanggal = '2026-08-19' WHERE id = 15165358")
        
        # Also just in case, insert a dummy one directly if it was missing
        sql = """
        INSERT INTO tbl_t_inspection 
        (tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, jenis_inspeksi, created_at, perusahaan_id, is_deleted)
        VALUES 
        (?, ?, ?, ?, ?, ?, ?, ?, ?, GETDATE(), ?, 0)
        """
        cursor.execute(sql, ('2026-08-19', '10:00:00', 'MUHAMAD ANDRYAN RASYID', nik, 'System Integrations', 'Mining', 'Pit A', 'Detail', 'Rutin', 1))
        
        conn.commit()
        print("Done fixing inspection date and adding another.")
    except Exception as e:
        print("Error:", e)
        conn.rollback()
    finally:
        conn.close()

if __name__ == "__main__":
    main()
