import re

with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Views\Hazard\Index.cshtml', 'r', encoding='utf-8') as f:
    hazard = f.read()

with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Views\Coaching\Index.cshtml', 'r', encoding='utf-8') as f:
    coaching = f.read()

# 1. Extract HTML
row_pattern = r'<div class="row">\s*<div class="col-md-6 form-group-sap">\s*<label class="form-label-sap" for="areaSearch">Area Utama</label>.*?</div>\s*</div>'
hazard_html_match = re.search(row_pattern, hazard, re.DOTALL)
if hazard_html_match:
    hazard_html = hazard_html_match.group(0)
else:
    print("Failed to extract Hazard HTML")
    exit(1)

# In Coaching, replace the old area/lokasi block
coaching_old_html_pattern = r'<div class="row">\s*<div class="col-md-6 form-group-sap">\s*<label class="form-label-sap" for="area">Area Utama</label>.*?<label class="form-label-sap" for="detilLokasi">Detail Lokasi</label>.*?</div\s*>\s*</div\s*>'
# wait, coaching has Area and Lokasi Spesifik in a row? No, let's just do simple string replacements
