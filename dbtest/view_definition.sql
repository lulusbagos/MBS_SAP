
ALTER   VIEW dbo.vw_r_karyawan_jabatan_mapping_preview
AS
WITH base_data AS
(
    SELECT
        k.id_karyawan AS karyawan_id,
        k.no_nik,
        k.id_perusahaan AS perusahaan_id,
        k.id_departemen AS departemen_id,
        k.id_jabatan AS jabatan_id,
        m.nama_jabatan,
        m.status_jabatan,
        k.level_jabatan,
        UPPER(LTRIM(RTRIM(ISNULL(m.nama_jabatan, '')))) AS nama_jabatan_norm
    FROM ONE_DB_MITRA.dbo.tbl_t_karyawan k
    LEFT JOIN ONE_DB_MITRA.dbo.tbl_m_jabatan m
        ON m.id = k.id_jabatan
    WHERE ISNULL(k.status_aktif, 0) = 1
      AND k.deleted_at IS NULL
),
resolved AS
(
    SELECT
        b.karyawan_id,
        b.no_nik,
        b.perusahaan_id,
        b.departemen_id,
        b.jabatan_id,
        b.nama_jabatan,
        b.status_jabatan,
        b.level_jabatan,
        b.nama_jabatan_norm,
        exact_alias.r_jabatan_id AS exact_r_jabatan_id,
        exact_alias.alias_nama_jabatan AS exact_alias,
        fuzzy_rule.kode_jabatan_standar AS fuzzy_kode,
        fallback_rule.kode_jabatan_standar AS fallback_kode
    FROM base_data b
    OUTER APPLY
    (
        SELECT TOP (1)
            a.r_jabatan_id,
            a.alias_nama_jabatan
        FROM ONE_DB_MITRA.dbo.tbl_r_jabatan_alias a
        WHERE a.is_aktif = 1
          AND (a.perusahaan_id = b.perusahaan_id OR a.perusahaan_id IS NULL)
          AND UPPER(LTRIM(RTRIM(a.alias_nama_jabatan))) = b.nama_jabatan_norm
        ORDER BY
            CASE WHEN a.perusahaan_id = b.perusahaan_id THEN 0 ELSE 1 END,
            a.prioritas
    ) exact_alias
    OUTER APPLY
    (
        SELECT TOP (1)
            x.kode_jabatan_standar
        FROM (VALUES
            ('PROJECT MANAGER', 'GM'),
            ('PENANGGUNG JAWAB OPERASIONAL', 'GM'),
            ('PJO', 'GM'),
            ('DEPUTY PROJECT MANAGER', 'SRM'),
            ('DPM', 'SRM'),
            ('DEPTHEAD', 'MGR'),
            ('DEPARTMENT HEAD', 'MGR'),
            ('DEPT HEAD', 'MGR'),
            ('SECTION HEAD', 'SU'),
            ('SECT HEAD', 'SU'),
            ('GROUP LEADER', 'SPV'),
            ('KORLAP', 'SPV'),
            ('SUPERITENDEN', 'SU'),
            ('SUPERVISIOR', 'SPV'),
            ('DIREKTUR', 'GM'),
            ('DIRECTOR', 'GM'),
            ('SISWA MAGANG', 'OFF'),
            ('PARAMEDIK', 'NST'),
            ('PARAMEDIC', 'NST'),
            ('SURVEYOR', 'OFF'),
            ('SECURITY', 'NST'),
            ('OJT', 'NST'),
            ('MCC', 'NST'),
            ('SIGENMEN', 'NST'),
            ('EMERGENCY RESPON TEAM', 'NST'),
            ('EMERGENCY RESPONSE TEAM', 'NST'),
            ('WELDER', 'NST'),
            ('STOREMAN', 'NST'),
            ('CARPENTER', 'NST'),
            ('PAYROLL STAFF', 'NST'),
            ('HRGA', 'NST'),
            ('TRAFICMAN', 'NST'),
            ('TRAFFICMAN', 'NST'),
            ('GTM', 'NST'),
            ('DRIVER DT', 'NST'),
            ('DT DRIVER', 'NST'),
            ('MEKANIK MCC', 'NST'),
            ('ADMINISTRASI', 'NST'),
            ('ADMIN', 'NST'),
            ('PATROL', 'NST'),
            ('STAFF', 'OFF'),
            ('DRIVER', 'NST'),
            ('OPERATOR', 'NST'),
            ('OPERATION', 'NST'),
            ('ANGGOTA TC', 'NST'),
            ('GENERAL', 'NST'),
            ('CLEANING SERVICE', 'NST'),
            ('CELANING SERVICE', 'NST'),
            ('DRILING', 'NST'),
            ('DRILLING', 'NST'),
            ('MECHANIC', 'NST'),
            ('MEKANIK', 'NST'),
            ('TECHNICIAN', 'NST'),
            ('TECNICIAN', 'NST'),
            ('TEKNISI', 'NST'),
            ('WASHING MAN', 'NST'),
            ('WASING MAN', 'NST'),
            ('HELPER', 'NST'),
            ('CREW', 'NST'),
            ('FORMAN', 'FM')
        ) x(keyword_token, kode_jabatan_standar)
        WHERE b.nama_jabatan_norm LIKE '%' + x.keyword_token + '%'
        ORDER BY
            CASE WHEN x.kode_jabatan_standar = 'NST' THEN 0 ELSE 1 END,
            LEN(x.keyword_token) DESC
    ) fuzzy_rule
    OUTER APPLY
    (
        SELECT
            CASE
                WHEN b.nama_jabatan_norm = '' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%GENERAL MANAGER%' THEN 'GM'
                WHEN b.nama_jabatan_norm LIKE '%DIREKTUR%' OR b.nama_jabatan_norm LIKE '%DIRECTOR%' THEN 'GM'
                WHEN b.nama_jabatan_norm IN ('GENERAL', 'OPERATION', 'OPERATIONS', 'ANGGOTA TC', 'CLEANING SERVICE', 'CELANING SERVICE') THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%SENIOR MANAGER%' THEN 'SRM'
                WHEN b.nama_jabatan_norm LIKE '%MANAGER%' THEN 'MGR'
                WHEN b.nama_jabatan_norm LIKE '%SENIOR SUPERINTENDENT%' THEN 'SRSU'
                WHEN b.nama_jabatan_norm LIKE '%SUPERINTENDENT%' OR b.nama_jabatan_norm LIKE '%SUPERITENDEN%' THEN 'SU'
                WHEN b.nama_jabatan_norm LIKE '%SENIOR SUPERVISOR%' THEN 'SRSP'
                WHEN b.nama_jabatan_norm LIKE '%SUPERVISOR%' OR b.nama_jabatan_norm LIKE '%SUPERVISIOR%' THEN 'SPV'
                WHEN b.nama_jabatan_norm LIKE '%SENIOR OFFICER%' THEN 'SROF'
                WHEN b.nama_jabatan_norm LIKE '%OFFICER%' THEN 'OFF'
                WHEN b.nama_jabatan_norm LIKE '%FOREMAN%' OR b.nama_jabatan_norm LIKE '%FORMAN%' THEN 'FM'
                WHEN b.nama_jabatan_norm LIKE '%OPERATOR%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%OPERATION%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%ANGGOTA TC%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%CLEANING SERVICE%' OR b.nama_jabatan_norm LIKE '%CELANING SERVICE%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%DRILING%' OR b.nama_jabatan_norm LIKE '%DRILLING%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%MECHANIC%'
                    OR b.nama_jabatan_norm LIKE '%MEKANIK%'
                    OR b.nama_jabatan_norm LIKE '%TECHNICIAN%'
                    OR b.nama_jabatan_norm LIKE '%TECNICIAN%'
                    OR b.nama_jabatan_norm LIKE '%TEKNISI%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%WASHING MAN%' OR b.nama_jabatan_norm LIKE '%WASING MAN%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%HELPER%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%CREW%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%DRIVER%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%PATROL%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%STAFF%' AND b.nama_jabatan_norm NOT LIKE '%PAYROLL STAFF%' THEN 'OFF'
                WHEN b.nama_jabatan_norm LIKE '%SECURITY%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%PARAMEDIC%' OR b.nama_jabatan_norm LIKE '%PARAMEDIK%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%OJT%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%MCC%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%SIGENMEN%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%EMERGENCY RESPON%' OR b.nama_jabatan_norm LIKE '%EMERGENCY RESPONSE%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%WELDER%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%STOREMAN%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%CARPENTER%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%PAYROLL STAFF%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%HRGA%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%TRAFICMAN%' OR b.nama_jabatan_norm LIKE '%TRAFFICMAN%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%GTM%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%DRIVER DT%' OR b.nama_jabatan_norm LIKE '%DT DRIVER%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%MEKANIK MCC%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%ADMIN%' THEN 'NST'
                WHEN b.nama_jabatan_norm LIKE '%PEKERJA%' OR b.nama_jabatan_norm LIKE '%WORKER%' THEN 'NST'
                ELSE 'OFF'
            END AS kode_jabatan_standar
    ) fallback_rule
)
SELECT
    r.karyawan_id,
    r.perusahaan_id,
    r.departemen_id,
    r.jabatan_id AS jabatan_id_existing,
    r.nama_jabatan AS nama_jabatan_existing,
    r.status_jabatan AS status_jabatan_existing,
    resolved_map.final_kategori_pengawas AS kategori_pengawas,
    CASE
        WHEN resolved_map.final_kode_jabatan_standar IN ('GM', 'SRM', 'MGR', 'SRSU', 'SU', 'SRSP', 'SPV', 'SROF', 'OFF', 'FM')
            THEN resolved_map.final_kategori_pengawas
        WHEN resolved_map.final_kode_jabatan_standar = 'NST'
            THEN CASE
                    WHEN resolved_map.final_kategori_pengawas = 'Pengawas Support Dept - Office'
                        THEN 'Pengawas Support Dept - Office'
                    ELSE 'Pengawas Support Dept - Non Office'
                 END
        WHEN resolved_map.final_kode_jabatan_standar = 'SPV'
            THEN 'Pengawas Area Operasional'
        ELSE 'Berdasarkan Jabatan'
    END AS kategori_mapping,
    resolved_map.final_r_jabatan_id AS r_jabatan_id,
    resolved_map.final_kode_jabatan_standar AS kode_jabatan_standar,
    resolved_map.final_nama_jabatan_standar AS nama_jabatan_standar,
    CASE
        WHEN title_override_target.r_jabatan_id IS NOT NULL THEN 'title-override'
        WHEN exact_target.r_jabatan_id IS NOT NULL THEN 'alias-exact'
        WHEN fuzzy_target.r_jabatan_id IS NOT NULL THEN 'rule-fuzzy'
        WHEN fallback_target.r_jabatan_id IS NOT NULL THEN 'rule-fallback'
        WHEN kategori_target.r_jabatan_id IS NOT NULL THEN 'kategori-pengawas'
        ELSE 'rule-default'
    END AS metode_mapping,
    CASE
        WHEN title_override_target.r_jabatan_id IS NOT NULL THEN CAST(100.00 AS DECIMAL(5,2))
        WHEN exact_target.r_jabatan_id IS NOT NULL THEN CAST(99.00 AS DECIMAL(5,2))
        WHEN fuzzy_target.r_jabatan_id IS NOT NULL THEN CAST(90.00 AS DECIMAL(5,2))
        WHEN fallback_target.r_jabatan_id IS NOT NULL THEN CAST(75.00 AS DECIMAL(5,2))
        WHEN kategori_target.r_jabatan_id IS NOT NULL THEN CAST(70.00 AS DECIMAL(5,2))
        ELSE CAST(50.00 AS DECIMAL(5,2))
    END AS confidence_score,
    CASE
        WHEN r.perusahaan_id = 4 THEN 0
        ELSE
            CASE
                WHEN nik_override.force_zero_target = 1 THEN 0
                WHEN resolved_map.final_kode_jabatan_standar = 'GM' THEN 1
                WHEN resolved_map.final_kode_jabatan_standar IN ('SRM', 'MGR') THEN 2
                WHEN resolved_map.final_kode_jabatan_standar IN ('SRSU', 'SU')
                    THEN CASE
                            WHEN resolved_map.final_kategori_pengawas = 'Pengawas Area Operasional' THEN 8
                            WHEN resolved_map.final_kategori_pengawas = 'Pengawas Support Dept - Non Office' THEN 4
                            WHEN resolved_map.final_kategori_pengawas = 'Pengawas Support Dept - Office' THEN 2
                            ELSE 4
                         END
                WHEN resolved_map.final_kode_jabatan_standar IN ('SRSP', 'SPV', 'SROF', 'OFF', 'FM')
                    THEN CASE
                            WHEN resolved_map.final_kategori_pengawas = 'Pengawas Area Operasional' THEN 8
                            WHEN resolved_map.final_kategori_pengawas = 'Pengawas Support Dept - Non Office' THEN 4
                            WHEN resolved_map.final_kategori_pengawas = 'Pengawas Support Dept - Office' THEN 2
                            ELSE 4
                         END
                ELSE 0
            END
    END AS target_inspeksi,
    CASE
        WHEN r.perusahaan_id = 4 THEN 0
        ELSE
            CASE
                WHEN nik_override.force_zero_target = 1 THEN 0
                WHEN resolved_map.final_kode_jabatan_standar = 'GM' THEN 1
                WHEN resolved_map.final_kode_jabatan_standar IN ('SRM', 'MGR') THEN 2
                WHEN resolved_map.final_kode_jabatan_standar IN ('SRSU', 'SU')
                    THEN CASE
                            WHEN resolved_map.final_kategori_pengawas = 'Pengawas Area Operasional' THEN 8
                            WHEN resolved_map.final_kategori_pengawas = 'Pengawas Support Dept - Non Office' THEN 4
                            WHEN resolved_map.final_kategori_pengawas = 'Pengawas Support Dept - Office' THEN 2
                            ELSE 4
                         END
                WHEN resolved_map.final_kode_jabatan_standar IN ('SRSP', 'SPV', 'SROF', 'OFF', 'FM')
                    THEN CASE
                            WHEN resolved_map.final_kategori_pengawas = 'Pengawas Area Operasional' THEN 8
                            WHEN resolved_map.final_kategori_pengawas = 'Pengawas Support Dept - Non Office' THEN 4
                            WHEN resolved_map.final_kategori_pengawas = 'Pengawas Support Dept - Office' THEN 2
                            ELSE 4
                         END
                ELSE 0
            END
    END AS target_observasi,
    CASE
        WHEN nik_override.force_zero_target = 1 THEN 0
        WHEN resolved_map.final_kode_jabatan_standar IN ('GM', 'SRM', 'MGR') THEN 1
        WHEN resolved_map.final_kode_jabatan_standar IN ('SRSU', 'SU')
            THEN CASE
                    WHEN resolved_map.final_kategori_pengawas = 'Pengawas Area Operasional' THEN 4
                    WHEN resolved_map.final_kategori_pengawas = 'Pengawas Support Dept - Non Office' THEN 2
                    WHEN resolved_map.final_kategori_pengawas = 'Pengawas Support Dept - Office' THEN 1
                    ELSE 2
                 END
        WHEN resolved_map.final_kode_jabatan_standar IN ('SRSP', 'SPV', 'SROF', 'OFF', 'FM')
            THEN CASE
                    WHEN resolved_map.final_kategori_pengawas = 'Pengawas Area Operasional' THEN 8
                    WHEN resolved_map.final_kategori_pengawas = 'Pengawas Support Dept - Non Office' THEN 4
                    WHEN resolved_map.final_kategori_pengawas = 'Pengawas Support Dept - Office' THEN 1
                    ELSE 4
                 END
        ELSE 0
    END AS target_hazard_report,
    CASE
        WHEN r.perusahaan_id = 4 THEN 0
        ELSE
            CASE
                WHEN nik_override.force_zero_target = 1 THEN 0
                WHEN resolved_map.final_kode_jabatan_standar IN ('GM', 'SRM', 'MGR') THEN 1
                WHEN resolved_map.final_kode_jabatan_standar IN ('SRSU', 'SU')
                    THEN CASE
                            WHEN resolved_map.final_kategori_pengawas = 'Pengawas Area Operasional' THEN 2
                            ELSE 1
                         END
                WHEN resolved_map.final_kode_jabatan_standar IN ('SRSP', 'SPV', 'SROF', 'OFF', 'FM')
                    THEN CASE
                            WHEN resolved_map.final_kategori_pengawas = 'Pengawas Area Operasional' THEN 2
                            ELSE 1
                         END
                ELSE 0
            END
    END AS target_coaching,
    CASE
        WHEN r.perusahaan_id = 4 THEN 0
        ELSE
            CASE
                WHEN nik_override.force_zero_target = 1 THEN 0
                WHEN resolved_map.final_kode_jabatan_standar IN ('GM', 'SRM', 'MGR', 'SRSU', 'SU', 'SRSP', 'SPV', 'SROF', 'OFF', 'FM') THEN 4
                ELSE 0
            END
    END AS target_safety_talk,
    CASE
        WHEN nik_override.force_zero_target = 1
            THEN 'Override daftar NIK/jabatan'
        WHEN r.jabatan_id IS NULL OR LTRIM(RTRIM(ISNULL(r.nama_jabatan, ''))) = ''
            THEN 'Jabatan null/kosong'
        WHEN resolved_map.final_kode_jabatan_standar = 'NST'
            THEN 'Kategori NST (non target)'
        ELSE 'Target berdasarkan mapping jabatan'
    END AS alasan_target_zero
FROM resolved r
OUTER APPLY
(
    SELECT
        CASE
            WHEN r.perusahaan_id IN (336, 339) THEN 1
            WHEN UPPER(LTRIM(RTRIM(ISNULL(r.level_jabatan, '')))) = 'NON SAP'
            THEN 1
            WHEN UPPER(LTRIM(RTRIM(ISNULL(r.no_nik, '')))) IN
            (
                'MGE-2401-0148',
                'MGE-2602-0826',
                'MGE-2602-0822',
                'MGE-2301-0188',
                'MGE-2508-0017'
            )
            OR r.nama_jabatan_norm LIKE '%ADMIN%'
            OR r.nama_jabatan_norm LIKE '%ADMINISTRASI%'
            OR r.nama_jabatan_norm LIKE '%NON STAF%'
            OR r.nama_jabatan_norm LIKE '%NON-STAF%'
            OR r.nama_jabatan_norm LIKE '%NONSTAFF%'
            OR r.nama_jabatan_norm LIKE '%NON STAFF%'
            OR r.nama_jabatan_norm LIKE '%SITE PLANNER%'
            OR r.nama_jabatan_norm LIKE '%TEKNISI%'
            OR r.nama_jabatan_norm LIKE '%TKNISI%'
            OR r.nama_jabatan_norm LIKE '%TECHNICIAN%'
            OR r.nama_jabatan_norm LIKE '%TECNICIAN%'
            OR r.nama_jabatan_norm LIKE '%SAFETY PATROL%'
            OR r.nama_jabatan_norm LIKE '%SAFETY PATRO%'
            OR r.nama_jabatan_norm LIKE '%DRIVER LV%'
            OR r.nama_jabatan_norm LIKE '%TEKNISI AC%'
            OR r.nama_jabatan_norm LIKE '%OPERATOR DT SANY 10R%'
            OR r.nama_jabatan_norm LIKE '%WASHING_MAN%'
            OR r.nama_jabatan_norm LIKE '%WASHING MAN%'
            OR r.nama_jabatan_norm LIKE '%PARAMEDIC%'
            OR r.nama_jabatan_norm LIKE '%PARAMEDIK%'
            OR r.nama_jabatan_norm LIKE '%OJT%'
            OR r.nama_jabatan_norm LIKE '%MCC%'
            OR r.nama_jabatan_norm LIKE '%SIGENMEN%'
            OR r.nama_jabatan_norm LIKE '%EMERGENCY RESPON%'
            OR r.nama_jabatan_norm LIKE '%EMERGENCY RESPONSE%'
            OR r.nama_jabatan_norm LIKE '%WELDER%'
            OR r.nama_jabatan_norm LIKE '%STOREMAN%'
            OR r.nama_jabatan_norm LIKE '%SECURITY%'
            OR r.nama_jabatan_norm LIKE '%CARPENTER%'
            OR r.nama_jabatan_norm LIKE '%PAYROLL STAFF%'
            OR r.nama_jabatan_norm LIKE '%HRGA%'
            OR r.nama_jabatan_norm LIKE '%TRAFICMAN%'
            OR r.nama_jabatan_norm LIKE '%TRAFFICMAN%'
            OR r.nama_jabatan_norm LIKE '%GTM%'
            OR r.nama_jabatan_norm LIKE '%DRIVER DT%'
            OR r.nama_jabatan_norm LIKE '%DT DRIVER%'
            OR r.nama_jabatan_norm LIKE '%MEKANIK MCC%'
            THEN 1
            ELSE 0
        END AS force_zero_target
) nik_override
OUTER APPLY
(
    SELECT
        CASE
            WHEN r.nama_jabatan_norm LIKE '%PATROL%'
                THEN 'NST'
            WHEN r.nama_jabatan_norm LIKE '%STAFF%'
             AND r.nama_jabatan_norm NOT LIKE '%NON STAFF%'
             AND r.nama_jabatan_norm NOT LIKE '%NON-STAFF%'
             AND r.nama_jabatan_norm NOT LIKE '%NONSTAFF%'
                THEN 'OFF'
            WHEN r.nama_jabatan_norm LIKE '%FOREMAN%'
             AND r.nama_jabatan_norm LIKE '%OFFICER%'
             AND ISNULL(NULLIF(LTRIM(RTRIM(r.level_jabatan)), ''), '') <> 'Pengawas Support Dept - Office'
                THEN 'SPV'
            ELSE NULL
        END AS kode_jabatan_standar
) title_override_code
OUTER APPLY
(
    SELECT TOP (1)
        j.r_jabatan_id,
        j.kode_jabatan_standar,
        j.nama_jabatan_standar,
        j.target_inspeksi,
        j.target_observasi,
        j.target_hazard_report,
        j.target_coaching,
        j.target_safety_talk
    FROM ONE_DB_MITRA.dbo.tbl_r_jabatan j
    WHERE j.is_aktif = 1
      AND (j.perusahaan_id = r.perusahaan_id OR j.perusahaan_id IS NULL)
      AND j.kode_jabatan_standar = title_override_code.kode_jabatan_standar
    ORDER BY CASE WHEN j.perusahaan_id = r.perusahaan_id THEN 0 ELSE 1 END
) title_override_target
OUTER APPLY
(
    SELECT
        CASE
            WHEN ISNULL(NULLIF(LTRIM(RTRIM(r.level_jabatan)), ''), '') = 'Pengawas Area Operasional' THEN 'SPV'
            WHEN ISNULL(NULLIF(LTRIM(RTRIM(r.level_jabatan)), ''), '') IN ('Pengawas Support Dept - Non Office', 'Pengawas Support Dept - Office') THEN 'NST'
            ELSE NULL
        END AS kode_jabatan_standar
) kategori_code
OUTER APPLY
(
    SELECT TOP (1)
        j.r_jabatan_id,
        j.kode_jabatan_standar,
        j.nama_jabatan_standar,
        j.target_inspeksi,
        j.target_observasi,
        j.target_hazard_report,
        j.target_coaching,
        j.target_safety_talk
    FROM ONE_DB_MITRA.dbo.tbl_r_jabatan j
    WHERE j.is_aktif = 1
      AND (j.perusahaan_id = r.perusahaan_id OR j.perusahaan_id IS NULL)
      AND j.kode_jabatan_standar = kategori_code.kode_jabatan_standar
    ORDER BY CASE WHEN j.perusahaan_id = r.perusahaan_id THEN 0 ELSE 1 END
) kategori_target
OUTER APPLY
(
    SELECT TOP (1)
        j.r_jabatan_id,
        j.kode_jabatan_standar,
        j.nama_jabatan_standar,
        j.target_inspeksi,
        j.target_observasi,
        j.target_hazard_report,
        j.target_coaching,
        j.target_safety_talk
    FROM ONE_DB_MITRA.dbo.tbl_r_jabatan j
    WHERE j.is_aktif = 1
      AND j.r_jabatan_id = r.exact_r_jabatan_id
) exact_target
OUTER APPLY
(
    SELECT TOP (1)
        j.r_jabatan_id,
        j.kode_jabatan_standar,
        j.nama_jabatan_standar,
        j.target_inspeksi,
        j.target_observasi,
        j.target_hazard_report,
        j.target_coaching,
        j.target_safety_talk
    FROM ONE_DB_MITRA.dbo.tbl_r_jabatan j
    WHERE j.is_aktif = 1
      AND (j.perusahaan_id = r.perusahaan_id OR j.perusahaan_id IS NULL)
      AND j.kode_jabatan_standar = r.fuzzy_kode
    ORDER BY CASE WHEN j.perusahaan_id = r.perusahaan_id THEN 0 ELSE 1 END
) fuzzy_target
OUTER APPLY
(
    SELECT TOP (1)
        j.r_jabatan_id,
        j.kode_jabatan_standar,
        j.nama_jabatan_standar,
        j.target_inspeksi,
        j.target_observasi,
        j.target_hazard_report,
        j.target_coaching,
        j.target_safety_talk
    FROM ONE_DB_MITRA.dbo.tbl_r_jabatan j
    WHERE j.is_aktif = 1
      AND (j.perusahaan_id = r.perusahaan_id OR j.perusahaan_id IS NULL)
      AND j.kode_jabatan_standar = r.fallback_kode
    ORDER BY CASE WHEN j.perusahaan_id = r.perusahaan_id THEN 0 ELSE 1 END
) fallback_target
OUTER APPLY
(
    SELECT
        COALESCE(title_override_target.r_jabatan_id, exact_target.r_jabatan_id, fuzzy_target.r_jabatan_id, fallback_target.r_jabatan_id, kategori_target.r_jabatan_id) AS final_r_jabatan_id,
        COALESCE(title_override_target.kode_jabatan_standar, exact_target.kode_jabatan_standar, fuzzy_target.kode_jabatan_standar, fallback_target.kode_jabatan_standar, kategori_target.kode_jabatan_standar) AS final_kode_jabatan_standar,
        COALESCE(title_override_target.nama_jabatan_standar, exact_target.nama_jabatan_standar, fuzzy_target.nama_jabatan_standar, fallback_target.nama_jabatan_standar, kategori_target.nama_jabatan_standar) AS final_nama_jabatan_standar,
        ISNULL(NULLIF(LTRIM(RTRIM(r.level_jabatan)), ''), 'Pengawas Support Dept - Non Office') AS final_kategori_pengawas
    ) resolved_map;
