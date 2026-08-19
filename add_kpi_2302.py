import pyodbc

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    nik = "23021900738"
    nama = "Karyawan (Dummy)"
    
    try:
        # Hazard: 1
        sql_hazard = """
        INSERT INTO tbl_t_hazard_report 
        (tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, temuan, kategori_bahaya, jenis_bahaya, jenis_ketidaksesuaian, tingkat_resiko, perbaikan, tindakan_perbaikan, pja, nik_pja, departemen_pja, status_temuan, created_at, perusahaan_id, is_deleted)
        VALUES 
        ('2026-08-19', '10:00:00', ?, ?, 'Dept', 'Mining', 'Loc', 'Detail', 'Temuan', 'KTA', 'Fisik', 'Ketidaksesuaian', 'Low', 'Tindakan', 'Tindakan', 'PJA', '123', 'Dept PJA', 'Open', GETDATE(), 1, 0)
        """
        cursor.execute(sql_hazard, (nama, nik))
        print("Inserted 1 Hazard.")
        
        # Inspection: 2
        sql_insp = """
        INSERT INTO tbl_t_inspection 
        (tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, jenis_inspeksi, created_at, perusahaan_id, is_deleted)
        VALUES 
        ('2026-08-19', '10:00:00', ?, ?, 'Dept', 'Mining', 'Loc', 'Detail', 'Rutin', GETDATE(), 1, 0)
        """
        cursor.execute(sql_insp, (nama, nik))
        cursor.execute(sql_insp, (nama, nik))
        print("Inserted 2 Inspections.")
        
        # Observation: 1
        sql_obs = """
        INSERT INTO tbl_t_observation 
        (date, nama, nik, departemen, area, lokasi, detil_lokasi, kegiatan_yang_diamati, departemen_yang_diamati, resiko_kritis, tingkat_resiko, perihal_yang_diamati, hasil_observasi, created_at, is_deleted)
        VALUES 
        ('2026-08-19', ?, ?, 'Dept', 'Mining', 'Loc', 'Detail', 'Kegiatan', 'Dept', 'Low', 'Low', 'Perihal', 'Safe', GETDATE(), 0)
        """
        cursor.execute(sql_obs, (nama, nik))
        print("Inserted 1 Observation.")
        
        conn.commit()
        print("All records inserted successfully!")
    except Exception as e:
        print("Error:", e)
        conn.rollback()
    finally:
        conn.close()

if __name__ == "__main__":
    main()
