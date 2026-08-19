import pyodbc

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
            print(f"Fixing inspection for NIK {nik}...")
            
            # Delete all inspections for this user
            cursor.execute("UPDATE tbl_t_inspection SET is_deleted = 1 WHERE nik = ?", (nik,))
            
            # Select the most recent 1 inspection
            cursor.execute("SELECT TOP 1 id FROM tbl_t_inspection WHERE nik = ? ORDER BY created_at DESC", (nik,))
            row = cursor.fetchone()
            
            if row:
                cursor.execute("UPDATE tbl_t_inspection SET is_deleted = 0 WHERE id = ?", (row[0],))
                print(f"  -> Restored 1 inspection (ID: {row[0]})")
            else:
                print(f"  -> No inspection records found to restore.")
                
        conn.commit()
        print("Data successfully updated!")
    except Exception as e:
        print("Error:", e)
        conn.rollback()
    finally:
        conn.close()

if __name__ == "__main__":
    main()
