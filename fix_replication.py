import re

with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Services\PostgresReplicationService.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# Replace invocations
# Hazard
content = content.replace(
    "BuildHazardKey(h.Nik ?? string.Empty, h.Tanggal, h.Waktu, h.Temuan ?? string.Empty, h.Area, h.Lokasi, h.PerusahaanId, null)",
    "BuildHazardKey(h.Nik ?? string.Empty, h.Tanggal, h.Waktu, h.Temuan ?? string.Empty, h.Area, h.Lokasi, h.PerusahaanId)"
)
content = content.replace(
    "BuildHazardKey(row.Nik, row.Tanggal, row.Waktu, row.Temuan, row.Area, row.Lokasi, perusahaanId, row.SourceCode)",
    "BuildHazardKey(row.Nik, row.Tanggal, row.Waktu, row.Temuan, row.Area, row.Lokasi, perusahaanId)"
)

# Inspection
content = content.replace(
    "BuildInspectionKey(i.Nik ?? string.Empty, i.Tanggal, i.Waktu, i.JenisInspeksi ?? string.Empty, i.Lokasi, i.PerusahaanId, null)",
    "BuildInspectionKey(i.Nik ?? string.Empty, i.Tanggal, i.Waktu, i.JenisInspeksi ?? string.Empty, i.Lokasi, i.PerusahaanId)"
)
content = content.replace(
    "BuildInspectionKey(row.Nik, row.Tanggal, row.Waktu, row.JenisInspeksi, row.Lokasi, perusahaanId, row.SourceCode)",
    "BuildInspectionKey(row.Nik, row.Tanggal, row.Waktu, row.JenisInspeksi, row.Lokasi, perusahaanId)"
)

# Coaching
content = content.replace(
    "BuildCoachingKey(c.Nik, c.Tanggal, c.Waktu, c.Tema, null)",
    "BuildCoachingKey(c.Nik, c.Tanggal, c.Waktu, c.Tema)"
)
content = content.replace(
    "BuildCoachingKey(row.TrainerNik, row.Tanggal, row.Waktu, row.Tema, null)",
    "BuildCoachingKey(row.TrainerNik, row.Tanggal, row.Waktu, row.Tema)"
)
content = content.replace(
    "BuildCoachingKey(first.TrainerNik, first.Tanggal, first.Waktu, first.Tema, first.SourceCode)",
    "BuildCoachingKey(first.TrainerNik, first.Tanggal, first.Waktu, first.Tema)"
)

# Observation
content = content.replace(
    "BuildObservationKey(o.Nik, o.Date.Date, o.Date.TimeOfDay, o.KegiatanYangDiamati, o.PerihalYangDiamati, null)",
    "BuildObservationKey(o.Nik, o.Date.Date, o.Date.TimeOfDay, o.KegiatanYangDiamati, o.PerihalYangDiamati)"
)
content = content.replace(
    "BuildObservationKey(row.Nik, row.Tanggal, row.Waktu, row.Kegiatan, row.Perihal, row.SourceCode)",
    "BuildObservationKey(row.Nik, row.Tanggal, row.Waktu, row.Kegiatan, row.Perihal)"
)

# P2H
content = content.replace(
    "BuildP2hKey(p.Nik, p.Tanggal, p.Waktu, p.NoLambung, null)",
    "BuildP2hKey(p.Nik, p.Tanggal, p.Waktu, p.NoLambung)"
)
content = content.replace(
    "BuildP2hKey(row.Nik, row.Tanggal, row.Waktu, row.NoLambung, null)",
    "BuildP2hKey(row.Nik, row.Tanggal, row.Waktu, row.NoLambung)"
)
content = content.replace(
    "BuildP2hKey(first.Nik, first.Tanggal, first.Waktu, first.NoLambung, first.SourceCode)",
    "BuildP2hKey(first.Nik, first.Tanggal, first.Waktu, first.NoLambung)"
)

# P5M
content = content.replace(
    "BuildP5mKey(p.Nik, p.Tanggal, p.Waktu, p.ListPertanyaan, null)",
    "BuildP5mKey(p.Nik, p.Tanggal, p.Waktu, p.ListPertanyaan)"
)
content = content.replace(
    "BuildP5mKey(row.Nik, row.Tanggal, row.Waktu, row.ListPertanyaan, row.SourceCode)",
    "BuildP5mKey(row.Nik, row.Tanggal, row.Waktu, row.ListPertanyaan)"
)

# SafetyTalk
content = content.replace(
    "BuildSafetyTalkKey(s.Nik, s.Tanggal, s.Waktu, s.Judul, null)",
    "BuildSafetyTalkKey(s.Nik, s.Tanggal, s.Waktu, s.Judul)"
)
content = content.replace(
    "BuildSafetyTalkKey(row.Nik, row.Tanggal, row.Waktu, row.Judul, row.SourceCode)",
    "BuildSafetyTalkKey(row.Nik, row.Tanggal, row.Waktu, row.Judul)"
)

# Replace definitions
content = re.sub(
    r'private static string BuildHazardKey\((.*?), string\? sourceCode\)\s*\{\s*var companyKey.*?var timeKey.*?var sourceKey.*?;.*?return \$\"\{(.*?)\}\|\{sourceKey\}\";\s*\}',
    r'private static string BuildHazardKey(\1)\n        {\n            var companyKey = perusahaanId?.ToString() ?? "0";\n            var timeKey = $"{waktu.Hours:D2}:{waktu.Minutes:D2}:{waktu.Seconds:D2}";\n            return $"{{\2}}";\n        }',
    content, flags=re.DOTALL
)

content = re.sub(
    r'private static string BuildInspectionKey\((.*?), string\? sourceCode\)\s*\{\s*var companyKey.*?var timeKey.*?var sourceKey.*?;.*?return \$\"\{(.*?)\}\|\{sourceKey\}\";\s*\}',
    r'private static string BuildInspectionKey(\1)\n        {\n            var companyKey = perusahaanId?.ToString() ?? "0";\n            var timeKey = $"{waktu.Hours:D2}:{waktu.Minutes:D2}:{waktu.Seconds:D2}";\n            return $"{{\2}}";\n        }',
    content, flags=re.DOTALL
)

content = re.sub(
    r'private static string BuildCoachingKey\((.*?), string\? sourceCode\)\s*\{\s*var sourceKey.*?;.*?return \$\"\{(.*?)\}\|\{sourceKey\}\";\s*\}',
    r'private static string BuildCoachingKey(\1)\n        {\n            return $"{{\2}}";\n        }',
    content, flags=re.DOTALL
)

content = re.sub(
    r'private static string BuildObservationKey\((.*?), string\? sourceCode\)\s*\{\s*var sourceKey.*?;.*?return \$\"\{(.*?)\}\|\{sourceKey\}\";\s*\}',
    r'private static string BuildObservationKey(\1)\n        {\n            return $"{{\2}}";\n        }',
    content, flags=re.DOTALL
)

content = re.sub(
    r'private static string BuildP2hKey\((.*?), string\? sourceCode\)\s*\{\s*var sourceKey.*?;.*?return \$\"\{(.*?)\}\|\{sourceKey\}\";\s*\}',
    r'private static string BuildP2hKey(\1)\n        {\n            return $"{{\2}}";\n        }',
    content, flags=re.DOTALL
)

content = re.sub(
    r'private static string BuildP5mKey\((.*?), string\? sourceCode\)\s*\{\s*var sourceKey.*?;.*?return \$\"\{(.*?)\}\|\{sourceKey\}\";\s*\}',
    r'private static string BuildP5mKey(\1)\n        {\n            return $"{{\2}}";\n        }',
    content, flags=re.DOTALL
)

content = re.sub(
    r'private static string BuildSafetyTalkKey\((.*?), string\? sourceCode\)\s*\{\s*var sourceKey.*?;.*?return \$\"\{(.*?)\}\|\{sourceKey\}\";\s*\}',
    r'private static string BuildSafetyTalkKey(\1)\n        {\n            return $"{{\2}}";\n        }',
    content, flags=re.DOTALL
)

with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Services\PostgresReplicationService.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print("Replacement complete.")
