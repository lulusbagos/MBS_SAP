import re

# Read Hazard
with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Views\Hazard\Index.cshtml', 'r', encoding='utf-8') as f:
    hazard = f.read()

# Read Coaching
with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Views\Coaching\Index.cshtml', 'r', encoding='utf-8') as f:
    coaching = f.read()

# 1. Extract HTML from Hazard
row_pattern = r'<div class="row">\s*<div class="col-md-6 form-group-sap">\s*<label class="form-label-sap" for="areaSearch">Area Utama</label>.*?<small class="text-muted" style="font-size: 10px;">Lokasi Aktual GPS otomatis mengambil koordinat Anda atau Anda bisa mengetiknya secara manual.</small>\s*</div>'
hazard_html_match = re.search(row_pattern, hazard, re.DOTALL)
if not hazard_html_match:
    print("Hazard HTML not found")
    exit(1)
hazard_html = hazard_html_match.group(0)

# 2. Extract JS from Hazard
js_start_pattern = r'var globalBenchmarksList = \[\];'
js_end_pattern = r'function saveNewBenchmark\(\) \{.*?\n        \}'
js_match = re.search(js_start_pattern + r'.*?' + js_end_pattern, hazard, re.DOTALL)
if not js_match:
    print("Hazard JS not found")
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
coaching_html_pattern = r'<div class="row">\s*<div class="col-md-6 form-group-sap">\s*<label class="form-label-sap" for="area">Area Utama</label>.*?<label class="form-label-sap" for="detilLokasi">Detail Lokasi</label>.*?</div>\s*</div>'
if re.search(coaching_html_pattern, coaching, re.DOTALL):
    coaching = re.sub(coaching_html_pattern, hazard_html, coaching, flags=re.DOTALL)
else:
    print("Coaching HTML not found, trying alternate")
    # try matching from <div class="row"> area to <div class="form-group-sap"> detilLokasi
    alt_pattern = r'<div class="row">\s*<div class="col-md-6 form-group-sap">\s*<label class="form-label-sap" for="area">Area Utama</label>.*?<small class="text-muted" style="font-size: 10px;">GPS otomatis mengambil kordinat Anda atau Anda bisa mengetiknya secara manual.</small>\s*</div>\s*</div>\s*<div class="form-group-sap">\s*<label class="form-label-sap" for="detilLokasi">Detail Lokasi</label>.*?</div>\s*</div>'
    coaching = re.sub(alt_pattern, hazard_html, coaching, flags=re.DOTALL)

# Add Modal before scripts
coaching = coaching.replace('@section Scripts {', hazard_modal + '\n\n@section Scripts {')

# Add JS functions before closing script
coaching = coaching.replace('</script>', hazard_js + '\n</script>')

# Add Document Ready inside .ready
coaching = coaching.replace('// Auto fetch GPS location on page load', doc_ready_js + '\n\n            // Auto fetch GPS location on page load')

# Save Coaching
with open(r'd:\4. PROJECT\2. Web\MBS_SAP\Views\Coaching\Index.cshtml', 'w', encoding='utf-8') as f:
    f.write(coaching)

print("Coaching Index updated!")
