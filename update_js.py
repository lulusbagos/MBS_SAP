import re

file_path = r'D:\4. PROJECT\2. Web\MBS_SAP\Views\Hazard\Index.cshtml'
with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

new_js = """
        var globalBenchmarksList = [];

        function loadBenchmarks(areaUtama, preselectBenchmark) {
            if (!areaUtama) return;
            $('#benchmarkSearch').prop('disabled', true).val('Memuat benchmark...');
            $('#btnAddBenchmark').prop('disabled', true);
            
            $.ajax({
                url: '/Api/GetBenchmarks',
                type: 'GET',
                data: { areaUtama: areaUtama },
                success: function(response) {
                    globalBenchmarksList = response || [];
                    $('#benchmarkSearch').prop('disabled', false).val('');
                    $('#btnAddBenchmark').prop('disabled', false);
                    
                    if (preselectBenchmark) {
                        selectBenchmark(preselectBenchmark);
                    } else {
                        $('#detilLokasi').val('');
                    }
                    renderBenchmarkDropdown('');
                },
                error: function() {
                    $('#benchmarkSearch').prop('disabled', false).val('');
                    $('#btnAddBenchmark').prop('disabled', false);
                }
            });
        }

        function renderBenchmarkDropdown(filterText) {
            var menu = $('#benchmarkDropdownMenu');
            menu.empty();
            var clean = (filterText || '').trim().toLowerCase();
            var filtered = globalBenchmarksList.filter(function(b) {
                return b.namaBenchmark.toLowerCase().includes(clean);
            });

            if (filtered.length > 0) {
                filtered.forEach(function(item) {
                    var btn = $('<button type="button"></button>')
                        .addClass('d-block w-100 text-start py-2 px-3 border-0 area-option-btn')
                        .css({
                            background: 'none',
                            color: 'var(--text-main)',
                            fontSize: '13px',
                            borderBottom: '1px solid var(--border-color)',
                            cursor: 'pointer'
                        })
                        .html('<i class="bi bi-compass-fill me-2 text-primary"></i>' + item.namaBenchmark)
                        .on('mouseenter', function() { $(this).css('background', 'var(--bg-layout)'); })
                        .on('mouseleave', function() { $(this).css('background', 'none'); })
                        .on('click', function() {
                            selectBenchmark(item.namaBenchmark);
                        });
                    menu.append(btn);
                });
            } else if (clean.length > 0) {
                menu.append('<div class="p-3 text-center text-muted" style="font-size:12px;"><i class="bi bi-search me-1"></i>Benchmark "' + filterText + '" tidak ditemukan</div>');
            } else {
                menu.append('<div class="p-3 text-center text-muted" style="font-size:12px;">Belum ada benchmark di area ini. Klik + untuk tambah.</div>');
            }
            
            if (menu.children().length > 0) menu.show(); else menu.hide();
        }

        function selectBenchmark(namaBenchmark) {
            $('#benchmarkSearch').val(namaBenchmark);
            $('#detilLokasi').val(namaBenchmark);
            $('#benchmarkDropdownMenu').hide();
            $('#benchmarkSearch').removeClass('is-invalid');
        }

        function saveNewBenchmark() {
            var btn = $('#btnSaveBenchmark');
            var errorDiv = $('#addBenchmarkError');
            var benchmarkName = $('#newBenchmarkName').val();
            var areaUtama = $('#area').val();

            if (!areaUtama) {
                errorDiv.text('Area utama belum dipilih!').show();
                return;
            }

            if (!benchmarkName || benchmarkName.trim() === '') {
                errorDiv.text('Nama benchmark tidak boleh kosong').show();
                return;
            }

            btn.prop('disabled', true).html('<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Menyimpan...');
            errorDiv.hide();

            $.ajax({
                url: '/Api/AddBenchmark',
                type: 'POST',
                dataType: 'json',
                data: { areaUtama: areaUtama, namaBenchmark: benchmarkName },
                success: function(response) {
                    $('#addBenchmarkModal').modal('hide');
                    $('#newBenchmarkName').val('');
                    var benchmarkToSelect = (response && response.data) ? response.data.namaBenchmark : null;
                    loadBenchmarks(areaUtama, benchmarkToSelect);
                    btn.prop('disabled', false).text('Simpan Benchmark');
                },
                error: function(xhr) {
                    errorDiv.text(xhr.responseText || 'Terjadi kesalahan saat menyimpan benchmark').show();
                    btn.prop('disabled', false).text('Simpan Benchmark');
                }
            });
        }
"""

# Replace saveNewArea with new code
content = re.sub(r'function saveNewArea\(\) \{.*?\n        }', new_js, content, flags=re.DOTALL)

# Add loadBenchmarks call inside selectArea
content = content.replace(
    "$('#areaSearch').removeClass('is-invalid');",
    "$('#areaSearch').removeClass('is-invalid');\n            \n            // Load Benchmarks for this area\n            loadBenchmarks(namaArea);"
)

# Add modal show event listener
modal_js = """
        $(document).ready(function() {
            $('#addBenchmarkModal').on('show.bs.modal', function () {
                var area = $('#area').val();
                if(!area) {
                    alert('Pilih Area Utama terlebih dahulu!');
                    return false; // Prevent modal from opening
                }
                $('#newBenchmarkArea').val(area);
                $('#newBenchmarkName').val('');
                $('#addBenchmarkError').hide();
                
                // Populate existing benchmarks in modal
                var list = $('#existingBenchmarksList');
                list.empty();
                if(globalBenchmarksList.length > 0) {
                    globalBenchmarksList.forEach(function(b) {
                        list.append('<div class="list-group-item" style="font-size: 13px; background: rgba(0,0,0,0.1); border-color: var(--border-color); color: var(--text-main);"><i class="bi bi-compass me-2 text-muted"></i>' + b.namaBenchmark + '</div>');
                    });
                } else {
                    list.append('<div class="p-3 text-center text-muted" style="font-size: 12px;">Belum ada benchmark di area ini</div>');
                }
            });

            // Toggle benchmark dropdown on chevron click
            $('#toggleBenchmarkDropdown').on('click', function() {
                if ($('#benchmarkSearch').prop('disabled')) return;
                var menu = $('#benchmarkDropdownMenu');
                if (menu.is(':visible')) {
                    menu.hide();
                } else {
                    renderBenchmarkDropdown($('#benchmarkSearch').val());
                    menu.show();
                }
            });

            // Filter dropdown on keyup
            $('#benchmarkSearch').on('input keyup', function() {
                renderBenchmarkDropdown($(this).val());
                if($(this).val() === '') $('#detilLokasi').val('');
            });

            // Open dropdown on focus
            $('#benchmarkSearch').on('focus', function() {
                if ($('#benchmarkSearch').prop('disabled')) return;
                renderBenchmarkDropdown($(this).val());
            });

            // Close dropdown when clicking outside
            $(document).on('click', function(e) {
                if (!$(e.target).closest('#benchmarkSearch, #benchmarkDropdownMenu, #toggleBenchmarkDropdown').length) {
                    $('#benchmarkDropdownMenu').hide();
                }
                if (!$(e.target).closest('#areaSearch, #areaDropdownMenu, #toggleAreaDropdown').length) {
                    $('#areaDropdownMenu').hide();
                }
            });
        });
"""

# replace the old $(document).ready for area dropdown
old_doc_ready_pattern = r'\$\(document\)\.ready\(function\(\) \{\n            // Toggle area dropdown on chevron click.*?\n        \}\);'
content = re.sub(old_doc_ready_pattern, modal_js, content, flags=re.DOTALL)

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)
print("JS updated successfully.")
