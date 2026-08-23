import pyodbc

conn = pyodbc.connect('DRIVER={ODBC Driver 17 for SQL Server};SERVER=172.16.1.93;DATABASE=DB_SAP;UID=sa;PWD=technical.indexim.123')
cursor = conn.cursor()

create_table_sql = """
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='tbl_m_benchmark' and xtype='U')
BEGIN
    CREATE TABLE tbl_m_benchmark (
        id INT IDENTITY(1,1) PRIMARY KEY,
        perusahaan_id INT NULL,
        area_utama NVARCHAR(255) NULL,
        nama_benchmark NVARCHAR(255) NULL,
        created_by_nik NVARCHAR(50) NULL,
        created_by_name NVARCHAR(255) NULL,
        created_at DATETIME2 NULL DEFAULT GETDATE()
    );
    PRINT 'Table tbl_m_benchmark created successfully.';
END
ELSE
BEGIN
    PRINT 'Table tbl_m_benchmark already exists.';
END
"""
try:
    cursor.execute(create_table_sql)
    conn.commit()
    print("Database modification executed successfully.")
except Exception as e:
    print(f"Error: {e}")
finally:
    cursor.close()
    conn.close()
