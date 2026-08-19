import pyodbc
from datetime import datetime, timedelta

conn_str = 'DRIVER={ODBC Driver 17 for SQL Server};SERVER=172.16.1.93;DATABASE=DB_SAP;UID=sa;PWD=technical.indexim.123'
conn = pyodbc.connect(conn_str)
cursor = conn.cursor()

users = [
    ('23091840871', 'MUHAMAD ANDRYAN RASYID'),
    ('24011950928', 'MUHAMMAD ALFIAN YUSTIANDA'),
    ('24051830994', 'ZANUR PRIHATNA'),
    ('24051940986', 'LULUS BAGOS HERMAWAN')
]

start_date = datetime(2026, 8, 1)

for user in users:
    nik, nama = user
    for i in range(12):
        d = start_date + timedelta(days=i)
        date_str = d.strftime('%Y-%m-%d')
        
        # Hazard
        cursor.execute('''
            INSERT INTO tbl_t_hazard_report (foto_temuan, tanggal, waktu, nama, nik, departemen, area, lokasi, detil_lokasi, temuan, kategori_bahaya, jenis_bahaya, jenis_ketidaksesuaian, tingkat_resiko, perbaikan, tindakan_perbaikan, pja, nik_pja, departemen_pja, status_temuan, created_at, perusahaan_id, is_deleted)
            VALUES ('', ?, '10:00:00', ?, ?, 'SYSTEM INTEGRATIONS', 'Office', 'Office', 'Ruang IT', 'Inspeksi Rutin', 'KTA', 'Fisik', 'Lain-lain', 'Rendah', 'Dirapihkan', 'Sudah Dirapihkan', 'PIC', '12345', 'IT', 'Closed', GETDATE(), 1, 0)
        ''', (date_str, nama, nik))
        
        # Observation
        cursor.execute('''
            INSERT INTO tbl_t_observation (date, nama, nik, departemen, area, lokasi, detil_lokasi, kegiatan_yang_diamati, departemen_yang_diamati, dokumen_pendukung, resiko_kritis, tingkat_resiko, perihal_yang_diamati, hasil_observasi, created_at, foto_url, keterangan, is_deleted)
            VALUES (?, ?, ?, 'SYSTEM INTEGRATIONS', 'Office', 'Office', 'Ruang IT', 'Maintenance', 'SYSTEM INTEGRATIONS', 'JSA', 'Tidak', 'Rendah', 'Aman', 'Aman', GETDATE(), '', 'Testing KPI', 0)
        ''', (date_str, nama, nik))

conn.commit()
conn.close()
print("Data insertion complete.")
