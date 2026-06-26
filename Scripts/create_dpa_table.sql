-- Create DPA Report Table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='tbl_t_dpa_report' AND xtype='U')
BEGIN
    CREATE TABLE [dbo].[tbl_t_dpa_report] (
        [id] INT IDENTITY(1,1) PRIMARY KEY,
        [assessor_nik] NVARCHAR(50) NOT NULL,
        [assessor_nama] NVARCHAR(150) NOT NULL,
        [assessor_departemen] NVARCHAR(150) NULL,
        [driver_nik] NVARCHAR(50) NOT NULL,
        [driver_nama] NVARCHAR(150) NOT NULL,
        [driver_departemen] NVARCHAR(150) NULL,
        [tanggal_penilaian] DATETIME2 NOT NULL,
        [jenis_perjalanan] NVARCHAR(100) NOT NULL,
        [rute] NVARCHAR(200) NULL,
        [no_lambung] NVARCHAR(100) NULL,
        [safety_driving_json] NVARCHAR(MAX) NULL,
        [driving_skill_json] NVARCHAR(MAX) NULL,
        [behavior_json] NVARCHAR(MAX) NULL,
        [service_quality_json] NVARCHAR(MAX) NULL,
        [score_penumpang] FLOAT NOT NULL DEFAULT 0,
        [score_gps] FLOAT NOT NULL DEFAULT 0,
        [score_lenzguard] FLOAT NOT NULL DEFAULT 0,
        [score_final] FLOAT NOT NULL DEFAULT 0,
        [kategori] NVARCHAR(50) NULL,
        [keterangan] NVARCHAR(1000) NULL,
        [perusahaan_id] INT NULL,
        [is_deleted] BIT NOT NULL DEFAULT 0,
        [created_at] DATETIME2 NOT NULL DEFAULT GETDATE()
    );
    PRINT 'Table tbl_t_dpa_report created successfully.';
END
ELSE
BEGIN
    PRINT 'Table tbl_t_dpa_report already exists.';
END
