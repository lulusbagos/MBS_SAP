import re

with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Services\PostgresReplicationService.cs', 'r', encoding='utf-8') as f:
    content = f.read()

content = re.sub(r'return \$"\{\{NormalizeText\(nik\)(.*?)\}\}";', r'return $"{NormalizeText(nik)\1}";', content)

with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Services\PostgresReplicationService.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print("Fix complete.")
