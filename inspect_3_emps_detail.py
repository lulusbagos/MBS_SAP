import pyodbc

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    niks = ["24051940986", "24041930970", "24051830994"]
    for nik in niks:
        print(f"\n================ NIK {nik} ================")
        cursor.execute("SELECT id, tanggal, nama, area, lokasi, detil_lokasi, judul FROM tbl_t_safety_talk WHERE nik = ? AND tanggal >= '2026-09-01' AND is_deleted = 0", (nik,))
        for r in cursor.fetchall():
            print(f"SafetyTalk: {r}")
        cursor.execute("SELECT id, tanggal, nama, area, lokasi, detil_lokasi, jenis_inspeksi FROM tbl_t_inspection WHERE nik = ? AND tanggal >= '2026-09-01' AND is_deleted = 0", (nik,))
        for r in cursor.fetchall():
            print(f"Inspection: {r}")
        cursor.execute("SELECT id, tanggal, nama, area, lokasi, detil_lokasi, tema FROM tbl_t_coaching WHERE nik = ? AND tanggal >= '2026-09-01' AND is_deleted = 0", (nik,))
        for r in cursor.fetchall():
            print(f"Coaching: {r}")
        cursor.execute("SELECT cp.id, c.tanggal, cp.nama, c.tema FROM tbl_t_coaching_participant cp JOIN tbl_t_coaching c ON cp.coaching_id = c.id WHERE cp.nik = ? AND c.created_at >= '2026-09-01' AND c.is_deleted = 0", (nik,))
        for r in cursor.fetchall():
            print(f"Coaching Participant: {r}")

if __name__ == "__main__":
    main()
