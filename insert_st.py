import pyodbc
from datetime import datetime

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    nik = "24051940986"
    judul = "bahaya dehidrasi"
    
    try:
        nama = "LULUS BAGOS HERMAWAN"
        perusahaan_id = 1
        departemen = "System Integrations"
        
        sql = """
        INSERT INTO tbl_t_safety_talk
        (foto_diri, foto_kegiatan, tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, judul, keterangan, created_at, perusahaan_id, is_deleted)
        VALUES
        (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """
        
        tanggal = "2026-08-19"
        waktu = "08:00:00"
        now = datetime.now()
        
        cursor.execute(sql, (
            "", "", tanggal, waktu, nama, nik, departemen, "Mining", "Lokasi Spesifik", "Detil Lokasi", judul, "Keterangan safety talk", now, perusahaan_id, 0
        ))
        
        conn.commit()
        print(f"Safety talk '{judul}' for NIK {nik} inserted.")
        
    except Exception as e:
        print("Error:", e)
        conn.rollback()
    finally:
        conn.close()

if __name__ == "__main__":
    main()
