import pyodbc

conn = pyodbc.connect('DRIVER={ODBC Driver 17 for SQL Server};SERVER=172.16.1.93;DATABASE=DB_SAP;UID=sa;PWD=technical.indexim.123')
cursor = conn.cursor()

alter_table_sql = """
IF NOT EXISTS (
    SELECT * FROM sys.columns 
    WHERE object_id = OBJECT_ID('tbl_t_action_plan') 
    AND name = 'reassign_note'
)
BEGIN
    ALTER TABLE tbl_t_action_plan ADD reassign_note NVARCHAR(1000) NULL;
    PRINT 'Column reassign_note added to tbl_t_action_plan successfully.';
END
ELSE
BEGIN
    PRINT 'Column reassign_note already exists in tbl_t_action_plan.';
END
"""
try:
    cursor.execute(alter_table_sql)
    conn.commit()
    print("Database alteration executed successfully.")
except Exception as e:
    print(f"Error: {e}")
finally:
    cursor.close()
    conn.close()
