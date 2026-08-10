import pyodbc
import pandas as pd
import datetime

conn = pyodbc.connect('DRIVER={ODBC Driver 17 for SQL Server};SERVER=172.16.1.93;DATABASE=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes')
one_month_ago = datetime.datetime.now() - datetime.timedelta(days=30)

query = """
SELECT *
FROM tbl_t_p2h_report
WHERE no_lambung IN ('IR-020012', 'IR020012', 'IR-02000012') 
  AND tanggal >= ?
ORDER BY tanggal DESC, waktu DESC
"""
df = pd.read_sql(query, conn, params=[one_month_ago])

if len(df) > 0:
    output_path = 'P2H_IR_020012_1Month.xlsx'
    writer = pd.ExcelWriter(output_path, engine='xlsxwriter')
    df.to_excel(writer, index=False, sheet_name='P2H Data')
    
    workbook  = writer.book
    worksheet = writer.sheets['P2H Data']
    
    header_format = workbook.add_format({
        'bold': True,
        'text_wrap': True,
        'valign': 'top',
        'fg_color': '#D7E4BC',
        'border': 1})
        
    for col_num, value in enumerate(df.columns.values):
        worksheet.write(0, col_num, value, header_format)
        worksheet.set_column(col_num, col_num, 15)
        
    writer.close()
    print("Exported to", output_path, "with", len(df), "rows")
else:
    print("No data found")
