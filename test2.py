import re

with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Views\Inspection\Index.cshtml', 'r', encoding='utf-8') as f:
    inspection = f.read()

row_pattern = r'<div class="row">\s*<div class="col-md-6 form-group-sap">\s*<label class="form-label-sap" for="areaSearch">Area Utama</label>.*?</div>\s*</div>'
inspection_html_match = re.search(row_pattern, inspection, re.DOTALL)
if inspection_html_match:
    print('Found Inspection HTML:')
    print(inspection_html_match.group(0))
else:
    print('Not found')
