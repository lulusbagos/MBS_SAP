import re

with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Views\Hazard\Index.cshtml', 'r', encoding='utf-8') as f:
    hazard = f.read()

with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Views\Inspection\Index.cshtml', 'r', encoding='utf-8') as f:
    inspection = f.read()

row_pattern = r'<div class="row">\s*<div class="col-md-6 form-group-sap">\s*<label class="form-label-sap" for="areaSearch">Area Utama</label>.*?<small class="text-muted" style="font-size: 10px;">Lokasi Aktual GPS otomatis mengambil koordinat Anda atau Anda bisa mengetiknya secara manual.</small>\s*</div>\s*</div>'
hazard_html_match = re.search(row_pattern, hazard, re.DOTALL)
if hazard_html_match:
    print('Found Hazard HTML')

inspection_html_match = re.search(row_pattern, inspection, re.DOTALL)
if inspection_html_match:
    print('Found Inspection HTML exactly matching Hazard!')
else:
    print('Inspection HTML does not match Hazard!')
