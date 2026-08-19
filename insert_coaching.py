import pyodbc
from datetime import datetime

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

niks = [
    "24051830994",
    "23091840871",
    "24011950928",
    "24051940986",
    "24041930970"
]

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    try:
        for nik in niks:
            print(f"Checking NIK {nik}...")
            # Check current count of active coachings in August 2026
            cursor.execute("SELECT COUNT(*) FROM tbl_t_coaching WHERE nik = ? AND is_deleted = 0 AND MONTH(tanggal) = 8 AND YEAR(tanggal) = 2026", (nik,))
            count = cursor.fetchone()[0]
            
            if count == 0:
                print(f"  -> Count is 0. Inserting dummy coaching record...")
                
                nama = "Karyawan"
                perusahaan_id = 1
                departemen = "System Integrations"
                
                # Insert
                sql = """
                INSERT INTO tbl_t_coaching 
                (foto, tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, tema, feedback, komitmen, perusahaan_id, is_deleted, created_at)
                VALUES 
                (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """
                
                now = datetime.now()
                # Use a date in August 2026
                tanggal = '2026-08-10'
                waktu = '10:00:00'
                
                cursor.execute(sql, (
                    '', tanggal, waktu, nama, nik, departemen, 'Area 1', 'Lokasi 1', 'Detil', 'Tema Coaching Dummy', 'Feedback Dummy', 'Komitmen Dummy', perusahaan_id, 0, now
                ))
                
                print(f"  -> Inserted 1 coaching for {nik}.")
            else:
                print(f"  -> Count is {count}. Skipped.")
                
        conn.commit()
        print("Data successfully updated!")
    except Exception as e:
        print("Error:", e)
        conn.rollback()
    finally:
        conn.close()

if __name__ == "__main__":
    main()
