import pyodbc
from collections import defaultdict
from datetime import datetime

# Connection string extracted from appsettings.json
conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    try:
        conn = pyodbc.connect(conn_str)
        cursor = conn.cursor()

        nik = "23091840871"
        
        # Query all records
        cursor.execute("SELECT id, tanggal, temuan, area, created_at, nik FROM tbl_t_hazard_report WHERE is_deleted = 0")
        rows = cursor.fetchall()
        print(f"Total Hazards in DB: {len(rows)}")
        
        # Group by (Nik, CreatedAt)
        groups = defaultdict(list)
        for row in rows:
            nik = row[5]
            created_at = row[4].strftime("%Y-%m-%d %H:%M:%S") if row[4] else "None"
            groups[(nik, created_at)].append(row)
        
        # Filter groups with more than 3 hazards at the same time
        bulk_inserts = {k: v for k, v in groups.items() if len(v) > 3}
        print(f"Bulk insert cases found for Hazards: {len(bulk_inserts)}")
        
        for k, v in bulk_inserts.items():
            print(f"- NIK: {k[0]}, CreatedAt: {k[1]}, Total Inserted: {len(v)}")
            dates = sorted([r[1].strftime("%Y-%m-%d") for r in v if r[1]])
            print(f"   -> Dates covered: {dates[0]} to {dates[-1]}")
        
    except Exception as e:
        print("Error:", e)

if __name__ == "__main__":
    main()
