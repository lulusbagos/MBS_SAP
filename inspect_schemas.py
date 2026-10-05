import pyodbc

conn_str = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"

def main():
    conn = pyodbc.connect(conn_str)
    cursor = conn.cursor()
    
    tables = [
        "tbl_t_hazard_report",
        "tbl_t_inspection",
        "tbl_t_safety_talk",
        "tbl_t_observation",
        "tbl_t_coaching",
        "tbl_t_coaching_participant"
    ]
    
    for tbl in tables:
        print(f"\n--- COLUMNS FOR {tbl} ---")
        cursor.execute(f"SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tbl}'")
        for col in cursor.fetchall():
            print(f"  {col[0]} ({col[1]})")
            
        print(f"\n--- RECENT SAMPLE FROM {tbl} ---")
        try:
            cursor.execute(f"SELECT TOP 1 * FROM {tbl} ORDER BY id DESC")
            cols = [d[0] for d in cursor.description]
            row = cursor.fetchone()
            if row:
                d = dict(zip(cols, row))
                # Truncate string representations for readability
                for k, v in d.items():
                    if isinstance(v, str) and len(v) > 60:
                        d[k] = v[:60] + "..."
                print(d)
        except Exception as ex:
            print("Error querying sample:", ex)

if __name__ == "__main__":
    main()
