CREATE TABLE [table_m_quis] (
    [id] int NOT NULL IDENTITY,
    [nik] nvarchar(50) NOT NULL,
    [nama] nvarchar(150) NOT NULL,
    [score] int NOT NULL,
    [platform] nvarchar(50) NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_table_m_quis] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_m_area_utama] (
    [id] int NOT NULL IDENTITY,
    [nama_area] nvarchar(150) NOT NULL,
    [perusahaan_id] int NOT NULL,
    [created_by_nik] nvarchar(50) NOT NULL,
    [created_by_name] nvarchar(150) NOT NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_m_area_utama] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_m_dpa_driver] (
    [id] int NOT NULL IDENTITY,
    [driver_nama] nvarchar(150) NOT NULL,
    [driver_nama_normalized] nvarchar(150) NOT NULL,
    [perusahaan_id] int NULL,
    [created_by_nik] nvarchar(50) NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_m_dpa_driver] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_m_p2h_vehicle] (
    [id] int NOT NULL IDENTITY,
    [no_lambung] nvarchar(100) NOT NULL,
    [jenis_kendaraan] nvarchar(100) NOT NULL,
    [merek] nvarchar(200) NOT NULL,
    [is_deleted] bit NOT NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_m_p2h_vehicle] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_m_pengguna_sandi] (
    [nrp] nvarchar(50) NOT NULL,
    [kata_sandi] nvarchar(200) NOT NULL,
    [diubah_pada] datetime2 NOT NULL,
    [profile_picture] nvarchar(1000) NULL,
    [has_agreed_to_terms] bit NOT NULL,
    CONSTRAINT [PK_tbl_m_pengguna_sandi] PRIMARY KEY ([nrp])
);
GO


CREATE TABLE [tbl_m_running_text] (
    [id] int NOT NULL IDENTITY,
    [pesan] nvarchar(max) NOT NULL,
    [is_aktif] bit NOT NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_m_running_text] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_t_action_plan] (
    [id] int NOT NULL IDENTITY,
    [foto_temuan] nvarchar(500) NULL,
    [foto_perbaikan] nvarchar(500) NULL,
    [tanggal] datetime2 NOT NULL,
    [waktu] time NOT NULL,
    [nama] nvarchar(150) NOT NULL,
    [nik] nvarchar(50) NOT NULL,
    [departemen] nvarchar(150) NULL,
    [area] nvarchar(150) NULL,
    [lokasi] nvarchar(150) NULL,
    [detil_lokasi] nvarchar(250) NULL,
    [item_sap] nvarchar(100) NULL,
    [kategori_temuan] nvarchar(150) NULL,
    [detil_temuan] nvarchar(max) NULL,
    [status] nvarchar(50) NOT NULL,
    [pja] nvarchar(150) NULL,
    [nik_pja] nvarchar(50) NULL,
    [departemen_pja] nvarchar(150) NULL,
    [pic] nvarchar(150) NULL,
    [nik_pic] nvarchar(50) NULL,
    [departemen_pic] nvarchar(150) NULL,
    [rencana_perbaikan] nvarchar(max) NULL,
    [tanggal_rencana_perbaikan] datetime2 NULL,
    [perbaikan] nvarchar(max) NULL,
    [tanggal_perbaikan] datetime2 NULL,
    [overdue] nvarchar(50) NULL,
    [alasan_overdue] nvarchar(max) NULL,
    [reassigned_from] nvarchar(300) NULL,
    [reassigned_to] nvarchar(300) NULL,
    [reassigned_at] datetime2 NULL,
    [perusahaan_id] int NULL,
    [is_deleted] bit NOT NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_t_action_plan] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_t_app_user] (
    [nik] nvarchar(50) NOT NULL,
    [nama] nvarchar(100) NOT NULL,
    [departemen] nvarchar(100) NOT NULL,
    [perusahaan] nvarchar(100) NOT NULL,
    [id_perusahaan] int NULL,
    [karyawan_id] int NULL,
    [last_login] datetime2 NOT NULL,
    [role] nvarchar(50) NULL,
    CONSTRAINT [PK_tbl_t_app_user] PRIMARY KEY ([nik])
);
GO


CREATE TABLE [tbl_t_attendance_event] (
    [id] int NOT NULL IDENTITY,
    [event_name] nvarchar(160) NOT NULL,
    [event_location] nvarchar(220) NULL,
    [event_description] nvarchar(1200) NULL,
    [start_at] datetime2 NOT NULL,
    [end_at] datetime2 NOT NULL,
    [qr_token] nvarchar(80) NOT NULL,
    [created_by] nvarchar(100) NULL,
    [is_active] bit NOT NULL,
    [is_deleted] bit NOT NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_t_attendance_event] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_t_coaching] (
    [id] int NOT NULL IDENTITY,
    [foto] nvarchar(500) NULL,
    [tanggal] datetime2 NOT NULL,
    [waktu] time NOT NULL,
    [nama] nvarchar(150) NOT NULL,
    [nik] nvarchar(50) NOT NULL,
    [departemen] nvarchar(150) NULL,
    [area] nvarchar(150) NULL,
    [lokasi] nvarchar(150) NULL,
    [detil_lokasi] nvarchar(250) NULL,
    [tema] nvarchar(100) NULL,
    [feedback] nvarchar(max) NULL,
    [komitmen] nvarchar(max) NULL,
    [perusahaan_id] int NULL,
    [is_deleted] bit NOT NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_t_coaching] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_t_comments] (
    [id] int NOT NULL IDENTITY,
    [item_type] nvarchar(50) NOT NULL,
    [item_id] int NOT NULL,
    [comment_text] nvarchar(1000) NOT NULL,
    [nik] nvarchar(50) NULL,
    [nama_pengguna] nvarchar(150) NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_t_comments] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_t_dpa_report] (
    [id] int NOT NULL IDENTITY,
    [assessor_nik] nvarchar(50) NOT NULL,
    [assessor_nama] nvarchar(150) NOT NULL,
    [assessor_departemen] nvarchar(150) NULL,
    [driver_nik] nvarchar(50) NOT NULL,
    [driver_nama] nvarchar(150) NOT NULL,
    [driver_departemen] nvarchar(150) NULL,
    [tanggal_penilaian] datetime2 NOT NULL,
    [jenis_perjalanan] nvarchar(100) NOT NULL,
    [rute] nvarchar(200) NULL,
    [no_lambung] nvarchar(100) NULL,
    [safety_driving_json] nvarchar(max) NULL,
    [driving_skill_json] nvarchar(max) NULL,
    [behavior_json] nvarchar(max) NULL,
    [service_quality_json] nvarchar(max) NULL,
    [score_penumpang] float NOT NULL,
    [score_gps] float NOT NULL,
    [score_lenzguard] float NOT NULL,
    [score_final] float NOT NULL,
    [kategori] nvarchar(50) NULL,
    [keterangan] nvarchar(1000) NULL,
    [perusahaan_id] int NULL,
    [is_deleted] bit NOT NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_t_dpa_report] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_t_hazard_report] (
    [id] int NOT NULL IDENTITY,
    [foto_temuan] nvarchar(500) NULL,
    [tanggal] datetime2 NOT NULL,
    [waktu] time NOT NULL,
    [nama] nvarchar(150) NOT NULL,
    [nik] nvarchar(50) NOT NULL,
    [departemen] nvarchar(150) NULL,
    [area] nvarchar(150) NULL,
    [lokasi] nvarchar(150) NULL,
    [detil_lokasi] nvarchar(250) NULL,
    [temuan] nvarchar(max) NOT NULL,
    [kategori_bahaya] nvarchar(100) NULL,
    [jenis_bahaya] nvarchar(100) NULL,
    [jenis_ketidaksesuaian] nvarchar(150) NULL,
    [tingkat_resiko] nvarchar(50) NULL,
    [perbaikan] nvarchar(max) NULL,
    [tindakan_perbaikan] nvarchar(max) NULL,
    [pja] nvarchar(150) NULL,
    [nik_pja] nvarchar(50) NULL,
    [departemen_pja] nvarchar(150) NULL,
    [status_temuan] nvarchar(50) NOT NULL,
    [perusahaan_id] int NULL,
    [is_deleted] bit NOT NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_t_hazard_report] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_t_incident_news] (
    [id] int NOT NULL IDENTITY,
    [judul] nvarchar(300) NOT NULL,
    [konten] nvarchar(max) NOT NULL,
    [gambar_url] nvarchar(500) NULL,
    [lokasi] nvarchar(150) NULL,
    [tanggal_kejadian] datetime2 NULL,
    [kategori] nvarchar(100) NULL,
    [perusahaan_id] int NULL,
    [dibuat_oleh] nvarchar(150) NOT NULL,
    [nik_pembuat] nvarchar(50) NOT NULL,
    [is_published] bit NOT NULL,
    [created_at] datetime2 NOT NULL,
    [updated_at] datetime2 NULL,
    CONSTRAINT [PK_tbl_t_incident_news] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_t_inspection] (
    [id] int NOT NULL IDENTITY,
    [tanggal] datetime2 NOT NULL,
    [waktu] time NOT NULL,
    [nama] nvarchar(150) NOT NULL,
    [nik] nvarchar(50) NOT NULL,
    [departemen] nvarchar(150) NULL,
    [area] nvarchar(150) NULL,
    [lokasi] nvarchar(150) NULL,
    [detil_lokasi] nvarchar(250) NULL,
    [jenis_inspeksi] nvarchar(150) NOT NULL,
    [pja] nvarchar(150) NULL,
    [nik_pja] nvarchar(50) NULL,
    [departemen_pja] nvarchar(150) NULL,
    [perusahaan_id] int NULL,
    [q1_1] int NOT NULL,
    [q1_2] int NOT NULL,
    [q1_3] int NOT NULL,
    [q2_1] int NOT NULL,
    [q2_2] int NOT NULL,
    [q2_3] int NOT NULL,
    [q3_1] int NOT NULL,
    [q3_2] int NOT NULL,
    [q3_3] int NOT NULL,
    [q4_1] int NOT NULL,
    [q4_2] int NOT NULL,
    [q4_3] int NOT NULL,
    [q5_1] int NOT NULL,
    [q5_2] int NOT NULL,
    [q5_3] int NOT NULL,
    [catatan] nvarchar(2000) NULL,
    [lampiran_json] nvarchar(max) NULL,
    [is_deleted] bit NOT NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_t_inspection] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_t_likes] (
    [id] int NOT NULL IDENTITY,
    [item_type] nvarchar(50) NOT NULL,
    [item_id] int NOT NULL,
    [nik] nvarchar(50) NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_t_likes] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_t_notifications] (
    [id] int NOT NULL IDENTITY,
    [recipient_nik] nvarchar(50) NOT NULL,
    [title] nvarchar(200) NOT NULL,
    [message] nvarchar(1000) NOT NULL,
    [url] nvarchar(500) NULL,
    [is_read] bit NOT NULL,
    [notif_type] nvarchar(50) NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_t_notifications] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_t_observation] (
    [id] int NOT NULL IDENTITY,
    [date] datetime2 NOT NULL,
    [nama] nvarchar(150) NOT NULL,
    [nik] nvarchar(50) NOT NULL,
    [departemen] nvarchar(100) NOT NULL,
    [area] nvarchar(100) NOT NULL,
    [lokasi] nvarchar(150) NOT NULL,
    [detil_lokasi] nvarchar(max) NULL,
    [kegiatan_yang_diamati] nvarchar(max) NULL,
    [departemen_yang_diamati] nvarchar(100) NULL,
    [dokumen_pendukung] nvarchar(100) NULL,
    [resiko_kritis] nvarchar(100) NULL,
    [tingkat_resiko] nvarchar(50) NULL,
    [perihal_yang_diamati] nvarchar(150) NULL,
    [hasil_observasi] nvarchar(50) NULL,
    [keterangan] nvarchar(2000) NULL,
    [foto_url] nvarchar(500) NULL,
    [is_deleted] bit NOT NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_t_observation] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_t_p2h_report] (
    [id] int NOT NULL IDENTITY,
    [nik] nvarchar(50) NOT NULL,
    [nama] nvarchar(150) NOT NULL,
    [tanggal] datetime2 NOT NULL,
    [waktu] time NOT NULL,
    [jenis_kendaraan] nvarchar(100) NOT NULL,
    [no_lambung] nvarchar(100) NOT NULL,
    [kilometer] float NOT NULL,
    [merek] nvarchar(200) NOT NULL,
    [simper_kimper] nvarchar(10) NOT NULL,
    [foto_speedometer] nvarchar(500) NULL,
    [gol_a_json] nvarchar(max) NULL,
    [gol_b_json] nvarchar(max) NULL,
    [gol_c_json] nvarchar(max) NULL,
    [is_deleted] bit NOT NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_t_p2h_report] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_t_p5m] (
    [id] int NOT NULL IDENTITY,
    [foto_kegiatan] nvarchar(500) NULL,
    [tanggal] datetime2 NOT NULL,
    [waktu] time NOT NULL,
    [nama] nvarchar(150) NOT NULL,
    [nik] nvarchar(50) NOT NULL,
    [departemen] nvarchar(150) NULL,
    [area] nvarchar(150) NULL,
    [lokasi] nvarchar(150) NULL,
    [detil_lokasi] nvarchar(250) NULL,
    [topik] nvarchar(250) NULL,
    [judul] nvarchar(250) NULL,
    [keterangan] nvarchar(max) NULL,
    [list_pertanyaan] nvarchar(max) NULL,
    [jawaban] nvarchar(100) NULL,
    [catatan] nvarchar(max) NULL,
    [perusahaan_id] int NULL,
    [is_deleted] bit NOT NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_t_p5m] PRIMARY KEY ([id])
);
GO


CREATE TABLE [tbl_t_safety_talk] (
    [id] int NOT NULL IDENTITY,
    [foto_diri] nvarchar(500) NULL,
    [foto_kegiatan] nvarchar(500) NULL,
    [tanggal] datetime2 NOT NULL,
    [waktu] time NOT NULL,
    [nama] nvarchar(150) NOT NULL,
    [nik] nvarchar(50) NOT NULL,
    [departemen] nvarchar(150) NULL,
    [area] nvarchar(150) NULL,
    [lokasi] nvarchar(150) NULL,
    [detil_lokasi] nvarchar(250) NULL,
    [judul] nvarchar(250) NULL,
    [keterangan] nvarchar(max) NULL,
    [perusahaan_id] int NULL,
    [is_deleted] bit NOT NULL,
    [created_at] datetime2 NOT NULL,
    CONSTRAINT [PK_tbl_t_safety_talk] PRIMARY KEY ([id])
);
GO


CREATE TABLE [table_m_quis_detail] (
    [id] int NOT NULL IDENTITY,
    [quiz_id] int NOT NULL,
    [item_id] int NOT NULL,
    [question] nvarchar(max) NOT NULL,
    [correct_key] nvarchar(10) NULL,
    [correct_answer_text] nvarchar(max) NULL,
    [selected_answer] nvarchar(10) NULL,
    [selected_answer_text] nvarchar(max) NULL,
    [points_earned] int NOT NULL,
    CONSTRAINT [PK_table_m_quis_detail] PRIMARY KEY ([id]),
    CONSTRAINT [FK_table_m_quis_detail_table_m_quis_quiz_id] FOREIGN KEY ([quiz_id]) REFERENCES [table_m_quis] ([id]) ON DELETE CASCADE
);
GO


CREATE TABLE [tbl_t_attendance_record] (
    [id] int NOT NULL IDENTITY,
    [attendance_event_id] int NOT NULL,
    [nik] nvarchar(80) NOT NULL,
    [nama] nvarchar(180) NULL,
    [jabatan] nvarchar(180) NULL,
    [perusahaan] nvarchar(180) NULL,
    [scan_at] datetime2 NOT NULL,
    [source] nvarchar(60) NOT NULL,
    [latitude] float NULL,
    [longitude] float NULL,
    CONSTRAINT [PK_tbl_t_attendance_record] PRIMARY KEY ([id]),
    CONSTRAINT [FK_tbl_t_attendance_record_tbl_t_attendance_event_attendance_event_id] FOREIGN KEY ([attendance_event_id]) REFERENCES [tbl_t_attendance_event] ([id]) ON DELETE CASCADE
);
GO


CREATE TABLE [tbl_t_coaching_participant] (
    [id] int NOT NULL IDENTITY,
    [coaching_id] int NOT NULL,
    [nik] nvarchar(50) NOT NULL,
    [nama] nvarchar(150) NOT NULL,
    CONSTRAINT [PK_tbl_t_coaching_participant] PRIMARY KEY ([id]),
    CONSTRAINT [FK_tbl_t_coaching_participant_tbl_t_coaching_coaching_id] FOREIGN KEY ([coaching_id]) REFERENCES [tbl_t_coaching] ([id]) ON DELETE CASCADE
);
GO


CREATE INDEX [IX_table_m_quis_detail_quiz_id] ON [table_m_quis_detail] ([quiz_id]);
GO


CREATE UNIQUE INDEX [IX_tbl_m_dpa_driver_driver_nama_normalized] ON [tbl_m_dpa_driver] ([driver_nama_normalized]);
GO


CREATE UNIQUE INDEX [IX_tbl_t_attendance_event_qr_token] ON [tbl_t_attendance_event] ([qr_token]);
GO


CREATE UNIQUE INDEX [IX_tbl_t_attendance_record_attendance_event_id_nik] ON [tbl_t_attendance_record] ([attendance_event_id], [nik]);
GO


CREATE INDEX [IX_tbl_t_coaching_participant_coaching_id] ON [tbl_t_coaching_participant] ([coaching_id]);
GO


