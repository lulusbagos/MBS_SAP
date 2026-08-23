import re

with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Views\Hazard\Index.cshtml', 'r', encoding='utf-8') as f:
    hazard = f.read()

with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Views\Coaching\Index.cshtml', 'r', encoding='utf-8') as f:
    coaching = f.read()

# 1. Extract HTML from Hazard
row_pattern = r'<div class="row">\s*<div class="col-md-6 form-group-sap">\s*<label class="form-label-sap" for="areaSearch">Area Utama</label>.*?</small>\s*</div>'
hazard_html_match = re.search(row_pattern, hazard, re.DOTALL)
if not hazard_html_match:
    print('Hazard HTML not found')
    exit(1)
hazard_html = hazard_html_match.group(0)

# 2. Extract JS from Hazard
js_start_pattern = r'var globalBenchmarksList = \[\];'
js_end_pattern = r'function saveNewBenchmark\(\) \{.*?\n        \}'
js_match = re.search(js_start_pattern + r'.*?' + js_end_pattern, hazard, re.DOTALL)
if not js_match:
    print('Hazard JS not found')
    exit(1)
hazard_js = js_match.group(0)

# Extract Modal from Hazard
modal_pattern = r'<!-- Modal Tambah Benchmark -->.*?</div>\s*</div>\s*</div>'
modal_match = re.search(modal_pattern, hazard, re.DOTALL)
hazard_modal = modal_match.group(0) if modal_match else ''

# Extract Document Ready JS for Benchmark
doc_ready_pattern = r"\$\('#addBenchmarkModal'\)\.on\('show\.bs\.modal', function \(\) \{.*?\n            \}\);"
doc_ready_match = re.search(doc_ready_pattern, hazard, re.DOTALL)
doc_ready_js = doc_ready_match.group(0) if doc_ready_match else ''

# 3. Replace HTML in Coaching
alt_pattern = r'<div class="row">\s*<div class="col-md-6 form-group-sap">\s*<label class="form-label-sap" for="area">Area Utama</label>.*?</small>\s*</div>\s*</div>\s*<div class="form-group-sap">\s*<label class="form-label-sap" for="detilLokasi">Detail Lokasi</label>.*?</div>\s*</div>'
coaching, count = re.subn(alt_pattern, hazard_html, coaching, flags=re.DOTALL)
print(f'HTML replaced: {count} times')

# Add Modal before scripts
coaching = coaching.replace('@section Scripts {', hazard_modal + '\n\n@section Scripts {')

# Add JS functions before closing script
coaching = coaching.replace('</script>', hazard_js + '\n</script>')

# Add Document Ready inside $(document).ready
coaching = coaching.replace('// Auto fetch GPS location on page load', doc_ready_js + '\n\n            // Auto fetch GPS location on page load')

# Save Coaching
with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Views\Coaching\Index.cshtml', 'w', encoding='utf-8') as f:
    f.write(coaching)

print('Coaching Index updated!')
