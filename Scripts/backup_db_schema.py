import os
import json
import pyodbc
from datetime import datetime
import shutil

# Configuration
CONN_STR = "Driver={ODBC Driver 17 for SQL Server};Server=172.16.1.93;Database=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes;"
BACKUP_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "db_backup"))
TABLES_DIR = os.path.join(BACKUP_ROOT, "tables")
VIEWS_DIR = os.path.join(BACKUP_ROOT, "views")
SQLITE_DIR = os.path.join(BACKUP_ROOT, "sqlite_backup")

os.makedirs(TABLES_DIR, exist_ok=True)
os.makedirs(VIEWS_DIR, exist_ok=True)
os.makedirs(SQLITE_DIR, exist_ok=True)

def format_data_type(type_name, max_length, precision, scale):
    t = type_name.lower()
    if t in ('nvarchar', 'nchar'):
        if max_length == -1:
            return f"[{type_name}](max)"
        else:
            return f"[{type_name}]({max_length // 2})"
    elif t in ('varchar', 'char', 'varbinary', 'binary'):
        if max_length == -1:
            return f"[{type_name}](max)"
        else:
            return f"[{type_name}]({max_length})"
    elif t in ('decimal', 'numeric'):
        return f"[{type_name}]({precision}, {scale})"
    elif t in ('datetime2', 'time', 'datetimeoffset'):
        return f"[{type_name}]({scale})"
    else:
        return f"[{type_name}]"

def main():
    timestamp_str = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    file_timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    
    print(f"Connecting to SQL Server DB_SAP at 172.16.1.93...")
    conn = pyodbc.connect(CONN_STR)
    cursor = conn.cursor()
    
    # 1. Fetch all tables and row counts
    print("Fetching tables and row counts...")
    cursor.execute("""
        SELECT 
            s.name AS schema_name,
            t.name AS table_name,
            t.object_id,
            p.rows AS row_count
        FROM sys.tables t
        JOIN sys.schemas s ON t.schema_id = s.schema_id
        JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
        WHERE t.is_ms_shipped = 0
        ORDER BY s.name, t.name
    """)
    tables_raw = cursor.fetchall()
    tables_meta = {}
    for schema, tbl, obj_id, row_cnt in tables_raw:
        tables_meta[tbl] = {
            "schema": schema,
            "name": tbl,
            "object_id": obj_id,
            "row_count": row_cnt,
            "columns": [],
            "primary_key": None,
            "indexes": [],
            "foreign_keys": []
        }
        
    # 2. Fetch Columns for each table
    print("Fetching column definitions...")
    cursor.execute("""
        SELECT 
            t.name AS table_name,
            c.column_id,
            c.name AS column_name,
            tp.name AS type_name,
            c.max_length,
            c.precision,
            c.scale,
            c.is_nullable,
            c.is_identity,
            CAST(ic.seed_value AS BIGINT) AS seed_value,
            CAST(ic.increment_value AS BIGINT) AS increment_value,
            dc.name AS default_name,
            dc.definition AS default_definition
        FROM sys.tables t
        JOIN sys.columns c ON t.object_id = c.object_id
        JOIN sys.types tp ON c.user_type_id = tp.user_type_id
        LEFT JOIN sys.identity_columns ic ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        LEFT JOIN sys.default_constraints dc ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
        WHERE t.is_ms_shipped = 0
        ORDER BY t.name, c.column_id
    """)
    for row in cursor.fetchall():
        tbl = row[0]
        if tbl in tables_meta:
            col_info = {
                "column_id": row[1],
                "name": row[2],
                "type": row[3],
                "max_length": row[4],
                "precision": row[5],
                "scale": row[6],
                "formatted_type": format_data_type(row[3], row[4], row[5], row[6]),
                "is_nullable": bool(row[7]),
                "is_identity": bool(row[8]),
                "seed_value": row[9],
                "increment_value": row[10],
                "default_name": row[11],
                "default_definition": row[12]
            }
            tables_meta[tbl]["columns"].append(col_info)
            
    # 3. Fetch Primary Keys
    print("Fetching primary key constraints...")
    cursor.execute("""
        SELECT 
            t.name AS table_name,
            kc.name AS pk_name,
            i.type_desc AS index_type,
            c.name AS col_name,
            ic.key_ordinal,
            ic.is_descending_key
        FROM sys.key_constraints kc
        JOIN sys.tables t ON kc.parent_object_id = t.object_id
        JOIN sys.indexes i ON kc.parent_object_id = i.object_id AND kc.unique_index_id = i.index_id
        JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
        JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
        WHERE kc.type = 'PK' AND t.is_ms_shipped = 0
        ORDER BY t.name, ic.key_ordinal
    """)
    for row in cursor.fetchall():
        tbl, pk_name, itype, col_name, kord, is_desc = row
        if tbl in tables_meta:
            if tables_meta[tbl]["primary_key"] is None:
                tables_meta[tbl]["primary_key"] = {
                    "name": pk_name,
                    "type": itype,
                    "columns": []
                }
            tables_meta[tbl]["primary_key"]["columns"].append({
                "column": col_name,
                "descending": bool(is_desc)
            })

    # 4. Fetch Non-PK Indexes
    print("Fetching non-PK indexes...")
    cursor.execute("""
        SELECT 
            t.name AS table_name,
            i.name AS index_name,
            i.is_unique,
            i.type_desc,
            i.has_filter,
            i.filter_definition,
            c.name AS column_name,
            ic.is_included_column,
            ic.key_ordinal,
            ic.is_descending_key
        FROM sys.indexes i
        JOIN sys.tables t ON i.object_id = t.object_id
        JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
        JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
        WHERE i.is_primary_key = 0 AND i.name IS NOT NULL AND t.is_ms_shipped = 0
        ORDER BY t.name, i.name, ic.is_included_column, ic.key_ordinal
    """)
    idx_map = {}
    for row in cursor.fetchall():
        tbl, idx_name, is_uniq, itype, has_filter, filt_def, col_name, is_inc, kord, is_desc = row
        key = (tbl, idx_name)
        if key not in idx_map:
            idx_map[key] = {
                "table": tbl,
                "name": idx_name,
                "is_unique": bool(is_uniq),
                "type": itype,
                "has_filter": bool(has_filter),
                "filter_definition": filt_def,
                "keys": [],
                "included": []
            }
        if is_inc:
            idx_map[key]["included"].append(col_name)
        else:
            idx_map[key]["keys"].append({
                "column": col_name,
                "descending": bool(is_desc)
            })
            
    for (tbl, _), idx_data in idx_map.items():
        if tbl in tables_meta:
            tables_meta[tbl]["indexes"].append(idx_data)

    # 5. Fetch Foreign Keys
    print("Fetching foreign keys...")
    cursor.execute("""
        SELECT 
            fk.name AS fk_name,
            OBJECT_NAME(fk.parent_object_id) AS parent_table,
            c1.name AS parent_col,
            OBJECT_NAME(fk.referenced_object_id) AS ref_table,
            c2.name AS ref_col,
            fk.delete_referential_action_desc,
            fk.update_referential_action_desc
        FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
        JOIN sys.columns c1 ON fkc.parent_object_id = c1.object_id AND fkc.parent_column_id = c1.column_id
        JOIN sys.columns c2 ON fkc.referenced_object_id = c2.object_id AND fkc.referenced_column_id = c2.column_id
        ORDER BY fk.name, fkc.constraint_column_id
    """)
    fk_list = []
    for row in cursor.fetchall():
        fk_name, p_tbl, p_col, r_tbl, r_col, del_act, upd_act = row
        fk_item = {
            "name": fk_name,
            "parent_table": p_tbl,
            "parent_column": p_col,
            "referenced_table": r_tbl,
            "referenced_column": r_col,
            "delete_action": del_act.replace('_', ' '),
            "update_action": upd_act.replace('_', ' ')
        }
        fk_list.append(fk_item)
        if p_tbl in tables_meta:
            tables_meta[p_tbl]["foreign_keys"].append(fk_item)

    # 6. Fetch Views
    print("Fetching view definitions...")
    cursor.execute("""
        SELECT 
            s.name AS schema_name,
            v.name AS view_name,
            OBJECT_DEFINITION(v.object_id) AS view_def
        FROM sys.views v
        JOIN sys.schemas s ON v.schema_id = s.schema_id
        ORDER BY v.name
    """)
    views_meta = []
    for schema, vname, vdef in cursor.fetchall():
        views_meta.append({
            "schema": schema,
            "name": vname,
            "definition": vdef
        })

    # Generate individual table scripts
    print("Writing individual table scripts...")
    for tbl, tdata in tables_meta.items():
        table_sql = []
        table_sql.append(f"-- ================================================================")
        table_sql.append(f"-- Table: [dbo].[{tbl}]")
        table_sql.append(f"-- Rows at backup: {tdata['row_count']:,}")
        table_sql.append(f"-- Backup Date: {timestamp_str}")
        table_sql.append(f"-- ================================================================\n")
        table_sql.append(f"SET ANSI_NULLS ON\nGO\nSET QUOTED_IDENTIFIER ON\nGO\n")
        table_sql.append(f"CREATE TABLE [dbo].[{tbl}] (")
        
        col_lines = []
        for col in tdata["columns"]:
            line = f"    [{col['name']}] {col['formatted_type']}"
            if col["is_identity"]:
                line += f" IDENTITY({col['seed_value']},{col['increment_value']})"
            line += " NOT NULL" if not col["is_nullable"] else " NULL"
            if col["default_definition"]:
                c_name = f"CONSTRAINT [{col['default_name']}] " if col['default_name'] else ""
                line += f" {c_name}DEFAULT {col['default_definition']}"
            col_lines.append(line)
            
        # Add PK constraint inside CREATE TABLE
        if tdata["primary_key"]:
            pk = tdata["primary_key"]
            pk_cols = ", ".join([f"[{c['column']}] {'DESC' if c['descending'] else 'ASC'}" for c in pk["columns"]])
            col_lines.append(f"    CONSTRAINT [{pk['name']}] PRIMARY KEY {pk['type']} ({pk_cols})")
            
        table_sql.append(",\n".join(col_lines))
        table_sql.append(")\nGO\n")
        
        # Add Indexes
        if tdata["indexes"]:
            table_sql.append(f"-- Indexes for [dbo].[{tbl}]")
            for idx in tdata["indexes"]:
                uniq_str = "UNIQUE " if idx["is_unique"] else ""
                keys_str = ", ".join([f"[{k['column']}] {'DESC' if k['descending'] else 'ASC'}" for k in idx["keys"]])
                idx_sql = f"CREATE {uniq_str}{idx['type']} INDEX [{idx['name']}] ON [dbo].[{tbl}] ({keys_str})"
                if idx["included"]:
                    inc_str = ", ".join([f"[{c}]" for c in idx["included"]])
                    idx_sql += f" INCLUDE ({inc_str})"
                if idx["has_filter"] and idx["filter_definition"]:
                    idx_sql += f" WHERE {idx['filter_definition']}"
                table_sql.append(idx_sql + "\nGO")
            table_sql.append("")

        tbl_file_path = os.path.join(TABLES_DIR, f"{tbl}.sql")
        with open(tbl_file_path, "w", encoding="utf-8") as f:
            f.write("\n".join(table_sql))

    # Generate individual view scripts
    print("Writing individual view scripts...")
    for v in views_meta:
        v_sql = []
        v_sql.append(f"-- ================================================================")
        v_sql.append(f"-- View: [dbo].[{v['name']}]")
        v_sql.append(f"-- Backup Date: {timestamp_str}")
        v_sql.append(f"-- ================================================================\n")
        v_sql.append(f"SET ANSI_NULLS ON\nGO\nSET QUOTED_IDENTIFIER ON\nGO\n")
        v_sql.append(f"IF OBJECT_ID('[dbo].[{v['name']}]', 'V') IS NOT NULL\n    DROP VIEW [dbo].[{v['name']}]\nGO\n")
        v_sql.append(v["definition"].strip())
        v_sql.append("\nGO\n")
        
        v_file_path = os.path.join(VIEWS_DIR, f"{v['name']}.sql")
        with open(v_file_path, "w", encoding="utf-8") as f:
            f.write("\n".join(v_sql))

    # Generate Full Combined SQL backup
    print("Writing full combined DB_SAP SQL backup...")
    full_sql = []
    full_sql.append("/" + "*" * 70)
    full_sql.append("  DATABASE SCHEMA BACKUP: DB_SAP")
    full_sql.append(f"  Generated: {timestamp_str}")
    full_sql.append(f"  Server: 172.16.1.93")
    full_sql.append(f"  Database: DB_SAP")
    full_sql.append(f"  Total Tables: {len(tables_meta)}")
    full_sql.append(f"  Total Views: {len(views_meta)}")
    full_sql.append(f"  Total Non-PK Indexes: {len(idx_map)}")
    full_sql.append(f"  Total Foreign Keys: {len(fk_list)}")
    full_sql.append("*" * 70 + "/\n")
    full_sql.append("USE [DB_SAP]\nGO\n")
    
    # Section 1: Tables
    full_sql.append("\n-- ================================================================")
    full_sql.append("-- SECTION 1: TABLES AND PRIMARY KEYS")
    full_sql.append("-- ================================================================\n")
    
    for tbl, tdata in tables_meta.items():
        full_sql.append(f"-- Table [dbo].[{tbl}] ({tdata['row_count']:,} rows)")
        full_sql.append(f"IF OBJECT_ID('[dbo].[{tbl}]', 'U') IS NULL")
        full_sql.append("BEGIN")
        full_sql.append(f"    CREATE TABLE [dbo].[{tbl}] (")
        
        col_lines = []
        for col in tdata["columns"]:
            line = f"        [{col['name']}] {col['formatted_type']}"
            if col["is_identity"]:
                line += f" IDENTITY({col['seed_value']},{col['increment_value']})"
            line += " NOT NULL" if not col["is_nullable"] else " NULL"
            if col["default_definition"]:
                c_name = f"CONSTRAINT [{col['default_name']}] " if col['default_name'] else ""
                line += f" {c_name}DEFAULT {col['default_definition']}"
            col_lines.append(line)
            
        if tdata["primary_key"]:
            pk = tdata["primary_key"]
            pk_cols = ", ".join([f"[{c['column']}] {'DESC' if c['descending'] else 'ASC'}" for c in pk["columns"]])
            col_lines.append(f"        CONSTRAINT [{pk['name']}] PRIMARY KEY {pk['type']} ({pk_cols})")
            
        full_sql.append(",\n".join(col_lines))
        full_sql.append("    )")
        full_sql.append("END\nGO\n")

    # Section 2: Non-PK Indexes
    full_sql.append("\n-- ================================================================")
    full_sql.append("-- SECTION 2: NON-PK INDEXES")
    full_sql.append("-- ================================================================\n")
    for (tbl, idx_name), idx in idx_map.items():
        uniq_str = "UNIQUE " if idx["is_unique"] else ""
        keys_str = ", ".join([f"[{k['column']}] {'DESC' if k['descending'] else 'ASC'}" for k in idx["keys"]])
        idx_sql = f"CREATE {uniq_str}{idx['type']} INDEX [{idx['name']}] ON [dbo].[{tbl}] ({keys_str})"
        if idx["included"]:
            inc_str = ", ".join([f"[{c}]" for c in idx["included"]])
            idx_sql += f" INCLUDE ({inc_str})"
        if idx["has_filter"] and idx["filter_definition"]:
            idx_sql += f" WHERE {idx['filter_definition']}"
            
        full_sql.append(f"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('[dbo].[{tbl}]') AND name = '{idx['name']}')")
        full_sql.append("BEGIN")
        full_sql.append(f"    {idx_sql}")
        full_sql.append("END\nGO\n")

    # Section 3: Foreign Keys
    full_sql.append("\n-- ================================================================")
    full_sql.append("-- SECTION 3: FOREIGN KEYS")
    full_sql.append("-- ================================================================\n")
    for fk in fk_list:
        fk_sql = (
            f"ALTER TABLE [dbo].[{fk['parent_table']}] WITH CHECK ADD CONSTRAINT [{fk['name']}] "
            f"FOREIGN KEY ([{fk['parent_column']}]) REFERENCES [dbo].[{fk['referenced_table']}] ([{fk['referenced_column']}]) "
            f"ON UPDATE {fk['update_action']} ON DELETE {fk['delete_action']}"
        )
        full_sql.append(f"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = '{fk['name']}')")
        full_sql.append("BEGIN")
        full_sql.append(f"    {fk_sql}")
        full_sql.append("END\nGO\n")

    # Section 4: Views
    full_sql.append("\n-- ================================================================")
    full_sql.append("-- SECTION 4: VIEWS")
    full_sql.append("-- ================================================================\n")
    for v in views_meta:
        full_sql.append(f"-- View: [dbo].[{v['name']}]")
        full_sql.append(f"IF OBJECT_ID('[dbo].[{v['name']}]', 'V') IS NOT NULL\n    DROP VIEW [dbo].[{v['name']}]\nGO")
        full_sql.append(v["definition"].strip())
        full_sql.append("\nGO\n")

    # Save timestamped and latest full schema
    full_file_timestamped = os.path.join(BACKUP_ROOT, f"DB_SAP_schema_backup_{file_timestamp}.sql")
    full_file_latest = os.path.join(BACKUP_ROOT, "DB_SAP_schema_backup_latest.sql")
    
    full_sql_text = "\n".join(full_sql)
    with open(full_file_timestamped, "w", encoding="utf-8") as f:
        f.write(full_sql_text)
    with open(full_file_latest, "w", encoding="utf-8") as f:
        f.write(full_sql_text)

    # 7. Write Schema Summary JSON
    print("Writing schema summary JSON...")
    meta_output = {
        "database": "DB_SAP",
        "server": "172.16.1.93",
        "backup_time": timestamp_str,
        "tables_count": len(tables_meta),
        "views_count": len(views_meta),
        "indexes_count": len(idx_map),
        "foreign_keys_count": len(fk_list),
        "tables": tables_meta,
        "views": [{k: v for k, v in vm.items() if k != 'definition'} for vm in views_meta],
        "foreign_keys": fk_list
    }
    json_path = os.path.join(BACKUP_ROOT, "schema_metadata.json")
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(meta_output, f, indent=2, default=str)

    # 8. Copy SQLite db if exists
    sqlite_src = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "mbs_sap.db"))
    if os.path.exists(sqlite_src):
        sqlite_dst = os.path.join(SQLITE_DIR, f"mbs_sap_backup_{file_timestamp}.db")
        shutil.copy2(sqlite_src, sqlite_dst)
        print(f"Copied SQLite db to: {sqlite_dst}")

    # 9. Create README.md
    print("Creating README.md in backup directory...")
    readme_lines = [
        "# Database Schema Backup - DB_SAP (MBS_SAP)",
        "",
        f"- **Tanggal Backup**: {timestamp_str}",
        f"- **Server**: `172.16.1.93`",
        f"- **Database**: `DB_SAP`",
        f"- **Collation**: `SQL_Latin1_General_CP1_CI_AS`",
        f"- **Total Tabel**: {len(tables_meta)}",
        f"- **Total Views**: {len(views_meta)}",
        f"- **Total Non-PK Indexes**: {len(idx_map)}",
        f"- **Total Foreign Keys**: {len(fk_list)}",
        "",
        "## File Utama",
        f"- [`DB_SAP_schema_backup_latest.sql`](./DB_SAP_schema_backup_latest.sql) : DDL lengkap seluruh tabel, primary key, default constraint, non-PK index, foreign key, dan view (idempotent / aman di-run).",
        f"- [`DB_SAP_schema_backup_{file_timestamp}.sql`](./DB_SAP_schema_backup_{file_timestamp}.sql) : Versi snapshot waktu backup.",
        "- [`schema_metadata.json`](./schema_metadata.json) : Metadata struktur skema dalam format JSON (mudah dibaca oleh AI / script).",
        "- [`tables/`](./tables/) : Folder skrip DDL per tabel (29 tabel individual).",
        "- [`views/`](./views/) : Folder skrip DDL per view (11 views individual).",
        "- [`sqlite_backup/`](./sqlite_backup/) : Backup database lokal `mbs_sap.db`.",
        "",
        "## Daftar Tabel & Jumlah Baris Saat Backup",
        "",
        "| No | Nama Tabel | Jumlah Kolom | Jumlah Baris | Primary Key |",
        "|---|---|---|---|---|"
    ]
    
    for idx, (tname, tinfo) in enumerate(sorted(tables_meta.items()), 1):
        pk_cols = ", ".join([c["column"] for c in tinfo["primary_key"]["columns"]]) if tinfo["primary_key"] else "-"
        readme_lines.append(f"| {idx} | `{tname}` | {len(tinfo['columns'])} | {tinfo['row_count']:,} | `{pk_cols}` |")

    readme_lines.extend([
        "",
        "## Daftar Views",
        "",
        "| No | Nama View | Schema |",
        "|---|---|---|"
    ])
    for idx, v in enumerate(sorted(views_meta, key=lambda x: x["name"]), 1):
        readme_lines.append(f"| {idx} | `[dbo].[{v['name']}]` | `{v['schema']}` |")

    readme_lines.extend([
        "",
        "## Cara Restore Skema",
        "Jika terjadi error atau perubahan skema yang tidak diinginkan:",
        "1. Untuk mengembalikan seluruh skema: Buka SQL Server Management Studio (SSMS) atau `sqlcmd` dan eksekusi file `DB_SAP_schema_backup_latest.sql`.",
        "2. Untuk mengembalikan satu tabel atau satu view tertentu saja: Cek file di folder `tables/<nama_tabel>.sql` atau `views/<nama_view>.sql`.",
        "",
        "```bash",
        "# Restore via sqlcmd:",
        "sqlcmd -S 172.16.1.93 -U sa -P technical.indexim.123 -d DB_SAP -i DB_SAP_schema_backup_latest.sql",
        "```"
    ])
    
    readme_path = os.path.join(BACKUP_ROOT, "README.md")
    with open(readme_path, "w", encoding="utf-8") as f:
        f.write("\n".join(readme_lines))
        
    print("\nBackup completed successfully!")
    print(f"Backup folder: {BACKUP_ROOT}")

if __name__ == "__main__":
    main()
