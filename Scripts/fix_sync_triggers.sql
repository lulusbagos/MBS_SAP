-- Safe Trigger Definitions for Hazard <-> Action Plan Sync
-- Prevents infinite recursive nesting (Limit 32 error) using TRIGGER_NESTLEVEL guard

IF OBJECT_ID('trg_sync_hazard_to_action_plan', 'TR') IS NOT NULL
    DROP TRIGGER trg_sync_hazard_to_action_plan;
GO

CREATE TRIGGER trg_sync_hazard_to_action_plan
ON tbl_t_hazard_report
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    -- Guard against trigger loops / mutual recursion
    IF (TRIGGER_NESTLEVEL() > 1) RETURN;
    IF NOT EXISTS (SELECT 1 FROM inserted) RETURN;

    -- 1. Jika hazard di-soft delete (is_deleted = 1), otomatis soft-delete action plan terkait dan set status 'Closed'
    UPDATE a
    SET a.is_deleted = 1,
        a.status = 'Closed'
    FROM tbl_t_action_plan a
    INNER JOIN inserted i ON a.item_sap = 'hazard:' + CAST(i.id AS VARCHAR(50))
    WHERE i.is_deleted = 1 AND a.is_deleted = 0;

    -- 2. Jika hazard status_temuan = 'Closed', otomatis sinkronkan status action plan menjadi 'Closed'
    UPDATE a
    SET a.status = 'Closed',
        a.perbaikan = COALESCE(NULLIF(i.perbaikan, ''), a.perbaikan),
        a.foto_perbaikan = COALESCE(NULLIF(i.foto_perbaikan, ''), a.foto_perbaikan)
    FROM tbl_t_action_plan a
    INNER JOIN inserted i ON a.item_sap = 'hazard:' + CAST(i.id AS VARCHAR(50))
    WHERE i.status_temuan = 'Closed' AND a.status <> 'Closed';

    -- 3. Jika hazard diubah PJA-nya, sinkronkan PJA di action plan
    UPDATE a
    SET a.pja = i.pja,
        a.nik_pja = i.nik_pja,
        a.departemen_pja = i.departemen_pja
    FROM tbl_t_action_plan a
    INNER JOIN inserted i ON a.item_sap = 'hazard:' + CAST(i.id AS VARCHAR(50))
    WHERE i.is_deleted = 0
      AND (
          ISNULL(a.pja, '') <> ISNULL(i.pja, '') OR
          ISNULL(a.nik_pja, '') <> ISNULL(i.nik_pja, '') OR
          ISNULL(a.departemen_pja, '') <> ISNULL(i.departemen_pja, '')
      );
END;
GO

IF OBJECT_ID('trg_sync_action_plan_to_hazard', 'TR') IS NOT NULL
    DROP TRIGGER trg_sync_action_plan_to_hazard;
GO

CREATE TRIGGER trg_sync_action_plan_to_hazard
ON tbl_t_action_plan
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    -- Guard against trigger loops / mutual recursion
    IF (TRIGGER_NESTLEVEL() > 1) RETURN;
    IF NOT EXISTS (SELECT 1 FROM inserted) RETURN;

    -- 1. Jika Action Plan di-close, otomatis sinkronkan hazard report menjadi 'Closed'
    UPDATE h
    SET h.status_temuan = 'Closed',
        h.perbaikan = COALESCE(NULLIF(i.perbaikan, ''), h.perbaikan),
        h.foto_perbaikan = COALESCE(NULLIF(i.foto_perbaikan, ''), h.foto_perbaikan)
    FROM tbl_t_hazard_report h
    INNER JOIN inserted i ON i.item_sap = 'hazard:' + CAST(h.id AS VARCHAR(50))
    WHERE (i.status IN ('Closed', 'Close', 'Selesai', 'Complete'))
      AND h.status_temuan <> 'Closed'
      AND h.is_deleted = 0;

    -- 2. Jika Action Plan di-soft delete, sinkronkan jika diperlukan
    UPDATE h
    SET h.is_deleted = 1
    FROM tbl_t_hazard_report h
    INNER JOIN inserted i ON i.item_sap = 'hazard:' + CAST(h.id AS VARCHAR(50))
    WHERE i.is_deleted = 1 AND h.is_deleted = 0;
END;
GO
