import pyodbc, json, os

conn = pyodbc.connect('DRIVER={ODBC Driver 17 for SQL Server};SERVER=172.16.1.93;DATABASE=DB_SAP;UID=sa;PWD=technical.indexim.123;TrustServerCertificate=Yes')
cursor = conn.cursor()

# 1. Create Tables tbl_m_bbs_document and tbl_m_bbs_document_item if not exist
cursor.execute("""
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tbl_m_bbs_document')
BEGIN
    CREATE TABLE tbl_m_bbs_document (
        id INT IDENTITY(1,1) PRIMARY KEY,
        doc_code NVARCHAR(100) NOT NULL,
        title NVARCHAR(255) NOT NULL,
        category_id INT NULL,
        category_name NVARCHAR(150) NULL,
        topic_name NVARCHAR(150) NULL,
        revision NVARCHAR(50) NULL DEFAULT '00',
        effective_date NVARCHAR(100) NULL,
        department NVARCHAR(150) NULL,
        scope NVARCHAR(255) NULL,
        description NVARCHAR(1000) NULL,
        icon NVARCHAR(50) NULL DEFAULT 'bi-file-earmark-text-fill',
        color NVARCHAR(50) NULL DEFAULT '#0284c7',
        file_name NVARCHAR(255) NULL,
        file_url NVARCHAR(500) NULL,
        total_items INT DEFAULT 0,
        behavior_count INT DEFAULT 0,
        condition_count INT DEFAULT 0,
        sort_order INT DEFAULT 0,
        is_active BIT DEFAULT 1,
        created_at DATETIME DEFAULT GETDATE(),
        updated_at DATETIME NULL
    );
    PRINT 'Created tbl_m_bbs_document';
END
""")

cursor.execute("""
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tbl_m_bbs_document_item')
BEGIN
    CREATE TABLE tbl_m_bbs_document_item (
        id INT IDENTITY(1,1) PRIMARY KEY,
        document_id INT NOT NULL,
        section_name NVARCHAR(200) NOT NULL,
        item_no NVARCHAR(50) NULL,
        item_text NVARCHAR(1000) NOT NULL,
        item_type NVARCHAR(50) NOT NULL DEFAULT 'Perilaku',
        classification NVARCHAR(50) NULL DEFAULT 'At-Risk',
        risk_level NVARCHAR(50) NULL DEFAULT 'Sedang',
        sort_order INT DEFAULT 0,
        is_active BIT DEFAULT 1,
        created_at DATETIME DEFAULT GETDATE(),
        CONSTRAINT FK_tbl_m_bbs_document_item_doc FOREIGN KEY (document_id) REFERENCES tbl_m_bbs_document(id) ON DELETE CASCADE
    );
    PRINT 'Created tbl_m_bbs_document_item';
END
""")
conn.commit()

# 2. Load Categories map
cursor.execute("SELECT id, name FROM tbl_m_bbs_category")
cat_map = {r[1]: r[0] for r in cursor.fetchall()}

# Load clean extracted documents JSON
with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Scripts\extracted_documents_clean.json', 'r', encoding='utf-8') as f:
    docs_data = json.load(f)

print(f"Loaded {len(docs_data)} documents from JSON.")

# 3. Seed / Update tbl_m_bbs_document and tbl_m_bbs_document_item
for idx, d in enumerate(docs_data, start=1):
    doc_code = d['doc_code']
    title = d['title']
    cat_name = d['category']
    topic_name = d['topic']
    cat_id = cat_map.get(cat_name)
    file_name = d['file']
    file_url = f"/uploads/bbs_documents/{file_name}"
    
    cursor.execute("SELECT id FROM tbl_m_bbs_document WHERE doc_code = ?", (doc_code,))
    existing = cursor.fetchone()
    
    if existing:
        doc_id = existing[0]
        cursor.execute("""
            UPDATE tbl_m_bbs_document
            SET title = ?, category_id = ?, category_name = ?, topic_name = ?,
                revision = ?, effective_date = ?, department = ?, icon = ?, color = ?,
                file_name = ?, file_url = ?, total_items = ?, behavior_count = ?,
                condition_count = ?, sort_order = ?, updated_at = GETDATE()
            WHERE id = ?
        """, (
            title, cat_id, cat_name, topic_name,
            d['revision'], d['effective_date'], d['department'], d['icon'], d['color'],
            file_name, file_url, d['total_items'], d['behavior_count'],
            d['condition_count'], idx, doc_id
        ))
        # Clear old items to reinsert fresh
        cursor.execute("DELETE FROM tbl_m_bbs_document_item WHERE document_id = ?", (doc_id,))
    else:
        cursor.execute("""
            INSERT INTO tbl_m_bbs_document (
                doc_code, title, category_id, category_name, topic_name,
                revision, effective_date, department, icon, color,
                file_name, file_url, total_items, behavior_count, condition_count,
                sort_order, is_active, created_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, GETDATE())
        """, (
            doc_code, title, cat_id, cat_name, topic_name,
            d['revision'], d['effective_date'], d['department'], d['icon'], d['color'],
            file_name, file_url, d['total_items'], d['behavior_count'], d['condition_count'],
            idx
        ))
        cursor.execute("SELECT @@IDENTITY")
        doc_id = int(cursor.fetchone()[0])
        
    # Insert items
    item_sort = 1
    for sec in d['sections']:
        sec_name = sec['name']
        sec_type = sec['type']
        for item in sec['items']:
            item_no = item['no']
            item_text = item['text']
            itype = item['type']
            
            # Default classification and risk
            is_risk = any(k in item_text.lower() for k in ['tidak', 'berdiri terlalu dekat', 'tanpa', 'curam', 'blind spot', 'lumpur', 'retakan', 'crack', 'amblas'])
            classif = "At-Risk" if is_risk else "Safe"
            risk_level = "Sangat Tinggi" if any(k in item_text.lower() for k in ['blind spot', 'crest', 'tebing', 'tersengat', 'swing', 'high risk', 'kaki disposal', 'pohon']) else "Sedang"
            
            cursor.execute("""
                INSERT INTO tbl_m_bbs_document_item (
                    document_id, section_name, item_no, item_text, item_type,
                    classification, risk_level, sort_order, is_active, created_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 1, GETDATE())
            """, (doc_id, sec_name, item_no, item_text, itype, classif, risk_level, item_sort))
            item_sort += 1

    print(f"Document [{doc_code}] seeded: {item_sort-1} items.")

conn.commit()

# 4. Sync Topics and Behaviors into tbl_m_bbs_topic and tbl_m_bbs_behavior
for d in docs_data:
    cat_name = d['category']
    topic_name = d['topic']
    cat_id = cat_map.get(cat_name)
    if not cat_id:
        continue
        
    # Check topic
    cursor.execute("SELECT id FROM tbl_m_bbs_topic WHERE category_id = ? AND name = ?", (cat_id, topic_name))
    t_row = cursor.fetchone()
    if not t_row:
        cursor.execute("""
            INSERT INTO tbl_m_bbs_topic (category_id, name, icon, description, sort_order, is_active, created_at)
            VALUES (?, ?, ?, ?, 10, 1, GETDATE())
        """, (cat_id, topic_name, d['icon'], f"Dokumen Acuan: {d['doc_code']} - {d['title']}"))
        cursor.execute("SELECT @@IDENTITY")
        topic_id = int(cursor.fetchone()[0])
        print(f"Added new Topic: [{cat_name}] {topic_name}")
    else:
        topic_id = t_row[0]
        
    # Seed behavior items
    for sec in d['sections']:
        if sec['type'] != 'Perilaku':
            continue
        for it in sec['items']:
            b_text = it['text']
            # Clean "Apakah " from behavior text
            clean_text = b_text
            if clean_text.lower().startswith('apakah '):
                clean_text = clean_text[7:]
            clean_text = clean_text.rstrip('?').strip()
            # Capitalize first letter
            clean_text = clean_text[:1].upper() + clean_text[1:]
            
            # Check if behavior already exists
            cursor.execute("""
                SELECT id FROM tbl_m_bbs_behavior
                WHERE category_id = ? AND (behavior_text = ? OR behavior_text LIKE ?)
            """, (cat_id, clean_text, f"%{clean_text[:50]}%"))
            existing_b = cursor.fetchone()
            
            if not existing_b:
                is_safe = any(k in clean_text.lower() for k in ['memakai', 'menggunakan', 'mematuhi', 'sesuai', 'berfungsi']) and not any(k in clean_text.lower() for k in ['tidak', 'tanpa', 'terlalu dekat'])
                b_class = "Safe" if is_safe else "At-Risk"
                b_risk = "Sangat Tinggi" if any(k in clean_text.lower() for k in ['blind spot', 'crest', 'tebing', 'terlalu dekat', 'high risk', 'swing']) else "Sedang"
                
                cursor.execute("""
                    INSERT INTO tbl_m_bbs_behavior (
                        category_id, topic_id, behavior_text, default_classification,
                        default_risk_level, guidance_note, sort_order, is_active, created_at
                    ) VALUES (?, ?, ?, ?, ?, ?, 1, 1, GETDATE())
                """, (cat_id, topic_id, clean_text[:295], b_class, b_risk, f"Sumber: {d['doc_code']} ({sec['name']})"))
                print(f"  Added behavior: {clean_text[:60]}...")

conn.commit()
print("All documents, topics, and behaviors synced successfully!")
