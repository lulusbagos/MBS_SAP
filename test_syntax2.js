
    const deptModal = document.getElementById('deptAchievementModal');
    const deptRows = document.getElementById('deptAchievementRows');
    const deptTitle = document.getElementById('deptAchievementTitle');
    const deptCloseBtn = document.getElementById('deptAchievementClose');

    const empModal = document.getElementById('employeeDetailModal');
    const empRows = document.getElementById('employeeDetailRows');
    const empTitle = document.getElementById('employeeDetailTitle');
    const empCloseBtn = document.getElementById('employeeDetailClose');
    const empLoading = document.getElementById('employeeLoading');
    const empTableContainer = document.getElementById('employeeTableContainer');

    let currentCompanyId = null;

    function getAchievementColor(rate) {
        const val = Number(rate);
        if (val >= 90) return '#16a34a'; // Green
        if (val >= 60) return '#ca8a04'; // Yellow
        if (val >= 30) return '#ea580c'; // Orange
        return '#dc2626'; // Red
    }

    function closeDeptModal() {
        if (!deptModal) return;
        deptModal.classList.remove('show');
        deptModal.setAttribute('aria-hidden', 'true');
    }

    function closeEmpModal() {
        if (!empModal) return;
        empModal.classList.remove('show');
        empModal.setAttribute('aria-hidden', 'true');
    }

    window.viewEmployeeDetails = function(deptName, companyIdOverride) {
        if (!empModal || !empRows || !empTitle || !empLoading || !empTableContainer) return;
        const cid = companyIdOverride || currentCompanyId;

        empTitle.textContent = 'Detail Karyawan & Target MTD - ' + deptName;
        
        empLoading.classList.remove('d-none');
        empTableContainer.classList.add('d-none');
        
        empModal.classList.add('show');
        empModal.setAttribute('aria-hidden', 'false');

        const url = '/Performance/GetDepartmentEmployees?companyId=' + cid + '&departmentName=' + encodeURIComponent(deptName);
        fetch(url)
            .then(res => {
                if (!res.ok) throw new Error('Gagal mengambil data');
                return res.json();
            })
            .then(data => {
                empLoading.classList.add('d-none');
                empTableContainer.classList.remove('d-none');

                if (!Array.isArray(data) || data.length === 0) {
                    empRows.innerHTML = '<tr><td colspan="10" class="text-center text-muted">Tidak ada data karyawan aktif.</td></tr>';
                } else {
                    empRows.innerHTML = data.map((item, idx) => {
                        const name = item.karyawanName || '-';
                        const nik = item.karyawanNik || '-';
                        const jabatan = item.jabatanName || '-';
                        
                        const mtd = Number(item.mtdAchievementRate || 0).toFixed(1);
                        const hazard = item.mtdHazardCount + ' / ' + item.targetHazardMtd;
                        const inspeksi = item.mtdInspeksiCount + ' / ' + item.targetInspeksiMtd;
                        const safetyTalk = item.mtdSafetyTalkCount + ' / ' + item.targetSafetyTalkMtd;
                        const observasi = item.mtdObservasiCount + ' / ' + item.targetObservasiMtd;
                        const coaching = item.mtdCoachingCount + ' / ' + item.targetCoachingMtd;
                        const p5m = item.mtdP5mCount + ' / ' + item.targetP5mMtd;
                        
                        return '<tr>' +
                            '<td class="text-muted fw-bold">' + (idx + 1) + '</td>' +
                            '<td><div class="fw-bold" style="color: #334155;">' + name + '</div><div style="font-size: 10px; color: #64748b;">' + nik + '</div></td>' +
                            '<td>' + jabatan + '</td>' +
                            '<td class="fw-bold" style="color: ' + getAchievementColor(mtd) + ';">' + mtd + '%</td>' +
                            '<td>' + hazard + '</td>' +
                            '<td>' + inspeksi + '</td>' +
                            '<td>' + safetyTalk + '</td>' +
                            '<td>' + observasi + '</td>' +
                            '<td>' + coaching + '</td>' +
                            '<td style="background-color: #fffbeb; border-left: 2px solid #f59e0b;">' + p5m + '</td>' +
                        '</tr>';
                    }).join('');
                }
            })
            .catch(err => {
                empLoading.classList.add('d-none');
                empTableContainer.classList.remove('d-none');
                empRows.innerHTML = '<tr><td colspan="10" class="text-center text-danger">Terjadi kesalahan saat memuat data.</td></tr>';
                console.error(err);
            });
    };

    window.openDeptModal = function(companyId, companyName, deptData) {
        currentCompanyId = companyId;
        if (!deptModal || !deptRows || !deptTitle) return;

        deptTitle.textContent = 'Pencapaian Per Departemen - ' + (companyName || 'Perusahaan');

        if (!Array.isArray(deptData) || deptData.length === 0) {
            deptRows.innerHTML = '<tr><td colspan="13" class="text-center text-muted">Belum ada data departemen.</td></tr>';
        } else {
            const sortedDeptData = deptData.slice().sort(function(a, b) {
                const ytdA = Number(a.ytdAchievementRate || a.YtdAchievementRate || 0);
                const ytdB = Number(b.ytdAchievementRate || b.YtdAchievementRate || 0);
                return ytdB - ytdA;
            });
            
            deptRows.innerHTML = sortedDeptData.map(function (item, idx) {
                const deptName = String(item.departmentName || item.DepartmentName || '-');
                const employeeCount = Number(item.employeeCount || item.EmployeeCount || 0);
                const ytd = Number(item.ytdAchievementRate || item.YtdAchievementRate || 0).toFixed(1);
                const mtd = Number(item.mtdAchievementRate || item.MtdAchievementRate || 0).toFixed(1);
                const week = Number(item.weeklyAchievementRate || item.WeeklyAchievementRate || 0).toFixed(1);
                const hRate = Number(item.ytdHazardRate || item.YtdHazardRate || 0).toFixed(1);
                const iRate = Number(item.ytdInspeksiRate || item.YtdInspeksiRate || 0).toFixed(1);
                const stRate = Number(item.ytdSafetyTalkRate || item.YtdSafetyTalkRate || 0).toFixed(1);
                const oRate = Number(item.ytdObservasiRate || item.YtdObservasiRate || 0).toFixed(1);
                const cRate = Number(item.ytdCoachingRate || item.YtdCoachingRate || 0).toFixed(1);
                const p5mRate = Number(item.ytdP5mRate || item.YtdP5mRate || 0).toFixed(1);

                return '<tr>' +
                    '<td class="text-muted fw-bold">' + (idx + 1) + '</td>' +
                    '<td class="fw-bold" style="color: #334155;">' + deptName + '</td>' +
                    '<td>' + employeeCount + '</td>' +
                    '<td class="dept-achievement-rate fw-bold" style="color: ' + getAchievementColor(ytd) + ';">' + ytd + '%</td>' +
                    '<td class="dept-achievement-rate fw-bold" style="color: ' + getAchievementColor(mtd) + ';">' + mtd + '%</td>' +
                    '<td class="dept-achievement-rate fw-bold" style="color: ' + getAchievementColor(week) + ';">' + week + '%</td>' +
                    '<td class="dept-achievement-rate fw-bold" style="color: ' + getAchievementColor(hRate) + ';">' + hRate + '%</td>' +
                    '<td class="dept-achievement-rate fw-bold" style="color: ' + getAchievementColor(iRate) + ';">' + iRate + '%</td>' +
                    '<td class="dept-achievement-rate fw-bold" style="color: ' + getAchievementColor(stRate) + ';">' + stRate + '%</td>' +
                    '<td class="dept-achievement-rate fw-bold" style="color: ' + getAchievementColor(oRate) + ';">' + oRate + '%</td>' +
                    '<td class="dept-achievement-rate fw-bold" style="color: ' + getAchievementColor(cRate) + ';">' + cRate + '%</td>' +
                    '<td class="dept-achievement-rate fw-bold" style="background-color: #fffbeb; border-left: 2px solid #f59e0b; color: ' + getAchievementColor(p5mRate) + ';">' + p5mRate + '%</td>' +
                    '<td style="text-align: center;">' +
                        '<button type="button" class="btn btn-sm btn-outline-primary" style="font-size: 11px; padding: 2px 8px; border-radius: 4px; font-weight: 600;" onclick="viewEmployeeDetails(\'' + deptName.replace(/\'/g, "\\'") + '\')">' +
                            'Ranking <i class="bi bi-chevron-right ms-1"></i>' +
                        '</button>' +
                    '</td>' +
                    '</tr>';
            }).join('');
        }

        deptModal.classList.add('show');
        deptModal.setAttribute('aria-hidden', 'false');
    };

    // Close buttons binding
    document.addEventListener("DOMContentLoaded", function () {
        const dCloseBtn = document.getElementById('deptAchievementClose');
        const eCloseBtn = document.getElementById('employeeDetailClose');
        const dModal = document.getElementById('deptAchievementModal');
        const eModal = document.getElementById('employeeDetailModal');

        dCloseBtn?.addEventListener('click', closeDeptModal);
        dModal?.addEventListener('click', function (e) {
            const target = e.target;
            if (target && target.getAttribute && target.getAttribute('data-dept-close') === 'true') {
                closeDeptModal();
            }
        });

        eCloseBtn?.addEventListener('click', closeEmpModal);
        eModal?.addEventListener('click', function (e) {
            const target = e.target;
            if (target && target.getAttribute && target.getAttribute('data-emp-close') === 'true') {
                closeEmpModal();
            }
        });
    });

    function ensureHierarchySectionDetached() {
        const hierarchySection = document.getElementById('sectionHierarchy');
        const companySection = document.getElementById('sectionCompany');
        if (!hierarchySection || !companySection) {
            return;
        }

        if (companySection.contains(hierarchySection)) {
            const rootContainer = document.getElementById('sectionMy')?.parentElement;
            if (rootContainer) {
                rootContainer.appendChild(hierarchySection);
            }
        }
    }

    function switchTab(tab) {
        ensureHierarchySectionDetached();

        document.getElementById('sectionMy').style.display = tab === 'my' ? 'block' : 'none';
        document.getElementById('sectionCompany').style.display = tab === 'company' ? 'block' : 'none';
        document.getElementById('sectionHitSafe').style.display = tab === 'hitsafe' ? 'block' : 'none';
        document.getElementById('sectionHierarchy').style.display = tab === 'hierarchy' ? 'block' : 'none';

        document.getElementById('tabMy').className = 'tab-btn px-4 py-2 ' + (tab === 'my' ? 'tab-active' : 'tab-inactive');
        document.getElementById('tabCompany').className = 'tab-btn px-4 py-2 ' + (tab === 'company' ? 'tab-active' : 'tab-inactive');
        document.getElementById('tabHitSafe').className = 'tab-btn px-4 py-2 ' + (tab === 'hitsafe' ? 'tab-active' : 'tab-inactive');
        document.getElementById('tabHierarchy').className = 'tab-btn px-4 py-2 ' + (tab === 'hierarchy' ? 'tab-active' : 'tab-inactive');

        if (tab === 'company' && typeof window._initCompanyCharts === 'function') {
            window._initCompanyCharts();
        }
        if (tab === 'hitsafe' && typeof window.initHitSafeMap === 'function') {
            window.requestAnimationFrame(function () {
                window.initHitSafeMap(20);
            });
        } else if (typeof window.stopHitSafeLocationTracking === 'function') {
            window.stopHitSafeLocationTracking();
        }

        if (tab === 'hierarchy' && typeof window.initHierarchyUI === 'function') {
            window.initHierarchyUI();
        }
    }

    function initHierarchyUI() {
        const treeRoot = document.getElementById('hierarchyOrgTree');
        if (!treeRoot || treeRoot.dataset.initialized === '1') {
            return;
        }

        

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                closeDeptModal();
                closeEmpModal();
            }
        });

        const searchInput = document.getElementById('hierarchySearchInput');
        const modeButtons = Array.from(document.querySelectorAll('#hierarchyModeToggle .hierarchy-mode-btn'));
        let currentMode = 'all';

        function passesMode(nodeEl) {
            if (currentMode !== 'primary') {
                return true;
            }

            return (nodeEl.dataset.mainconGroup || '') !== 'secondary';
        }

        function updateHierarchyView(keyword) {
            const normalized = (keyword || '').trim().toLowerCase();

            function evaluateNode(nodeEl) {
                const selfName = (nodeEl.dataset.companyName || '').toLowerCase();
                const selfId = (nodeEl.dataset.companyId || '').toLowerCase();
                const selfMatch = normalized.length === 0 || selfName.includes(normalized) || selfId.includes(normalized);
                const childNodes = Array.from(nodeEl.querySelectorAll(':scope > ul > li.org-node'));
                let childMatch = false;

                childNodes.forEach(function (child) {
                    if (evaluateNode(child)) {
                        childMatch = true;
                    }
                });

                const searchMatch = normalized.length === 0 ? true : (selfMatch || childMatch);
                const modeMatch = passesMode(nodeEl);
                const visible = modeMatch && searchMatch;
                nodeEl.classList.toggle('org-node-hidden', !visible);

                return visible;
            }

            Array.from(treeRoot.querySelectorAll(':scope > li.org-node')).forEach(function (rootNode) {
                evaluateNode(rootNode);
            });
        }

        treeRoot.addEventListener('click', function (e) {
            const trigger = e.target.closest('.org-node-menu-btn');
            if (!trigger) {
                return;
            }

            e.preventDefault();
            e.stopPropagation();

            const nodeEl = trigger.closest('li.org-node');
            if (!nodeEl) {
                return;
            }

            const companyId = nodeEl.dataset.companyId;
            const companyName = nodeEl.dataset.companyName || 'Perusahaan';
            const raw = nodeEl.dataset.deptAchievements || '[]';
            let parsed = [];

            try {
                parsed = JSON.parse(raw);
            } catch (_) {
                parsed = [];
            }

            openDeptModal(companyId, companyName, parsed);
        });

        searchInput?.addEventListener('input', function (e) {
            updateHierarchyView(e.target.value);
        });

        modeButtons.forEach(function (button) {
            button.addEventListener('click', function () {
                currentMode = button.dataset.mode || 'all';
                modeButtons.forEach(function (btn) {
                    btn.classList.toggle('is-active', btn === button);
                });
                updateHierarchyView(searchInput ? searchInput.value : '');
            });
        });

        updateHierarchyView(searchInput ? searchInput.value : '');
        treeRoot.dataset.initialized = '1';

        // === Premium scroll nav ===
        const wrapper = document.getElementById('orgChartWrapper');
        const scrollLeft = document.getElementById('orgScrollLeft');
        const scrollRight = document.getElementById('orgScrollRight');
        const scrollThumb = document.getElementById('orgScrollThumb');
        const zoomInBtn = document.getElementById('orgZoomIn');
        const zoomOutBtn = document.getElementById('orgZoomOut');
        const zoomResetBtn = document.getElementById('orgZoomReset');
        const zoomValueEl = document.getElementById('orgZoomValue');
        const zoomPresetEl = document.getElementById('orgZoomPreset');
        const ZOOM_MIN = 0.2;
        const ZOOM_MAX = 1.0;
        const ZOOM_STEP = 0.1;
        let currentZoom = 1;

        function updateZoomLabel() {
            if (!zoomValueEl) return;
            zoomValueEl.textContent = Math.round(currentZoom * 100) + '%';
            if (zoomPresetEl) {
                zoomPresetEl.value = currentZoom.toFixed(1).replace(/\.0$/, '');
            }
        }

        function setZoom(nextZoom, keepCenter = true) {
            if (!wrapper || !treeRoot) return;

            const clamped = Math.max(ZOOM_MIN, Math.min(ZOOM_MAX, nextZoom));
            const prev = currentZoom;
            if (Math.abs(clamped - prev) < 0.001) {
                updateZoomLabel();
                return;
            }

            const centerRatioX = wrapper.scrollWidth > 0
                ? (wrapper.scrollLeft + wrapper.clientWidth / 2) / wrapper.scrollWidth
                : 0;
            const centerRatioY = wrapper.scrollHeight > 0
                ? (wrapper.scrollTop + wrapper.clientHeight / 2) / wrapper.scrollHeight
                : 0;

            currentZoom = clamped;
            treeRoot.style.setProperty('--org-zoom', String(currentZoom));
            updateZoomLabel();

            requestAnimationFrame(function () {
                if (keepCenter) {
                    const targetLeft = (wrapper.scrollWidth * centerRatioX) - (wrapper.clientWidth / 2);
                    const targetTop = (wrapper.scrollHeight * centerRatioY) - (wrapper.clientHeight / 2);
                    wrapper.scrollLeft = Math.max(0, targetLeft);
                    wrapper.scrollTop = Math.max(0, targetTop);
                }
                updateScrollThumb();
            });
        }

        function updateScrollThumb() {
            if (!wrapper || !scrollThumb) return;
            const { scrollLeft: sl, scrollWidth: sw, clientWidth: cw } = wrapper;
            if (sw <= cw) { scrollThumb.style.width = '100%'; scrollThumb.style.marginLeft = '0'; return; }
            const ratio = cw / sw;
            const thumbW = Math.max(30, ratio * 100);
            const maxLeft = 100 - thumbW;
            const scrollRatio = sl / (sw - cw);
            scrollThumb.style.width = thumbW + '%';
            scrollThumb.style.marginLeft = (scrollRatio * maxLeft) + '%';
        }

        wrapper?.addEventListener('scroll', updateScrollThumb);
        updateScrollThumb();

        const STEP = 340;
        function smoothScrollBy(px) {
            if (!wrapper) return;
            wrapper.scrollBy({ left: px, behavior: 'smooth' });
        }
        scrollLeft?.addEventListener('click', function () { smoothScrollBy(-STEP); });
        scrollRight?.addEventListener('click', function () { smoothScrollBy(STEP); });
        zoomInBtn?.addEventListener('click', function () { setZoom(currentZoom + ZOOM_STEP); });
        zoomOutBtn?.addEventListener('click', function () { setZoom(currentZoom - ZOOM_STEP); });
        zoomResetBtn?.addEventListener('click', function () { setZoom(1, false); });
        zoomPresetEl?.addEventListener('change', function () {
            const selected = parseFloat(zoomPresetEl.value);
            if (Number.isFinite(selected)) {
                setZoom(selected);
            }
        });

        wrapper?.addEventListener('wheel', function (e) {
            if (!e.ctrlKey) return;
            e.preventDefault();
            const delta = e.deltaY < 0 ? ZOOM_STEP : -ZOOM_STEP;
            setZoom(currentZoom + delta);
        }, { passive: false });

        // Drag-to-pan
        let isDragging = false, dragStartX = 0, dragScrollLeft = 0;
        wrapper?.addEventListener('mousedown', function (e) {
            if (e.button !== 0) return;
            isDragging = true;
            dragStartX = e.pageX - wrapper.offsetLeft;
            dragScrollLeft = wrapper.scrollLeft;
            wrapper.classList.add('is-dragging');
        });
        document.addEventListener('mousemove', function (e) {
            if (!isDragging) return;
            e.preventDefault();
            const x = e.pageX - wrapper.offsetLeft;
            wrapper.scrollLeft = dragScrollLeft - (x - dragStartX);
        });
        document.addEventListener('mouseup', function () {
            if (isDragging) { isDragging = false; wrapper?.classList.remove('is-dragging'); }
        });
        // Touch pan
        let touchStartX = 0, touchScrollLeft = 0;
        wrapper?.addEventListener('touchstart', function (e) {
            touchStartX = e.touches[0].pageX;
            touchScrollLeft = wrapper.scrollLeft;
        }, { passive: true });
        wrapper?.addEventListener('touchmove', function (e) {
            const dx = touchStartX - e.touches[0].pageX;
            wrapper.scrollLeft = touchScrollLeft + dx;
        }, { passive: true });

        setZoom(1, false);
    }

    document.addEventListener("DOMContentLoaded", function () {
        ensureHierarchySectionDetached();

        if (typeof window.initHierarchyUI === 'function') {
            window.initHierarchyUI();
        }

        // Safe high-contrast theme detection
        const isDark = document.documentElement.getAttribute('data-theme') === 'dark' || 
                       document.body.classList.contains('dark') || 
                       document.body.classList.contains('dark-theme');
        
        const textColor = isDark ? '#f8fafc' : '#0f172a';
        const gridColor = isDark ? 'rgba(255,255,255,0.08)' : 'rgba(0,0,0,0.08)';

        // Helper untuk membuat Gauge Chart
        function createGauge(id, value, isGreenHigh) {
            const ctx = document.getElementById(id);
            if (!ctx) return;
            
            const gradColor = isGreenHigh 
                ? ['#ef4444', '#facc15', '#22c55e'] 
                : ['#22c55e', '#facc15', '#ef4444'];

            new Chart(ctx.getContext('2d'), {
                type: 'doughnut',
                data: {
                    datasets: [{
                        data: [value, 100 - value],
                        backgroundColor: [
                            value > 66 ? gradColor[2] : (value > 33 ? gradColor[1] : gradColor[0]),
                            isDark ? 'rgba(255,255,255,0.05)' : '#f1f5f9'
                        ],
                        borderWidth: 0,
                        circumference: 180,
                        rotation: 270,
                        cutout: '80%',
                        needleValue: value
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { display: false },
                        tooltip: { enabled: false }
                    }
                },
                plugins: [{
                    id: 'gaugeNeedle',
                    afterDatasetDraw(chart) {
                        const { ctx, width, chartArea: { top, bottom, left, right } } = chart;
                        ctx.save();
                        const value = chart.data.datasets[0].needleValue;
                        const clampedValue = Math.max(0, Math.min(value, 100));
                        const angle = Math.PI + (clampedValue / 100 * Math.PI);
                        
                        const metaset = chart.getDatasetMeta(0);
                        const arc = metaset.data[0];
                        if (!arc) return;
                        const cx = arc.x;
                        const cy = arc.y;
                        const radius = arc.outerRadius - (arc.outerRadius - arc.innerRadius)/2;

                        ctx.translate(cx, cy);
                        ctx.rotate(angle);
                        
                        ctx.beginPath();
                        ctx.moveTo(0, -3);
                        ctx.lineTo(radius, 0);
                        ctx.lineTo(0, 3);
                        ctx.fillStyle = isDark ? '#f8fafc' : '#1e293b';
                        ctx.fill();
                        
                        ctx.beginPath();
                        ctx.arc(0, 0, 6, 0, Math.PI * 2);
                        ctx.fillStyle = isDark ? '#f8fafc' : '#1e293b';
                        ctx.fill();
                        
                        ctx.restore();
                    }
                }]
            });
        }

        // 1. Chart Proporsi Aktivitas Saya
        const myProporsiEl = document.getElementById('myProporsiChart');
        if (myProporsiEl) {
            const ctxMyProp = myProporsiEl.getContext('2d');
            new Chart(ctxMyProp, {
                type: 'doughnut',
                data: {
                    labels: ['Hazard', 'Inspeksi', 'Safety Talk', 'P5M', 'Observasi'],
                    datasets: [{
                        data: [0, 0, 0, 0, 0],
                        backgroundColor: ['#f43f5e', '#3b82f6', '#a855f7', '#10b981', '#fb923c'],
                        borderWidth: 0
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: {
                            position: 'bottom',
                            labels: { color: textColor, font: { weight: 'bold', size: 11 } }
                        }
                    }
                }
            });
        }

        // 2. Initialize pyramid progress bar widths per group (accident vs safety)
        function applyPyramidProgress(selector, fullFillForPositive = false) {
            const bars = Array.from(document.querySelectorAll(selector));
            if (!bars.length) return;

            const values = bars.map(bar => parseInt(bar.getAttribute('data-val')) || 0);
            const maxVal = Math.max(...values, 0);

            bars.forEach(bar => {
                const val = parseInt(bar.getAttribute('data-val')) || 0;
                const progress = bar.querySelector('.pyramid-progress');
                if (!progress) return;

                bar.classList.toggle('has-fill', val > 0);
                bar.classList.toggle('is-zero', val <= 0);

                if (maxVal > 0) {
                    const pct = fullFillForPositive
                        ? (val > 0 ? 100 : 0)
                        : ((val / maxVal) * 100);
                    progress.style.width = pct + '%';
                } else {
                    progress.style.width = '0%';
                }
            });
        }

        applyPyramidProgress('.accident-pyramid-bar', true);
        applyPyramidProgress('.safety-pyramid-bar');

        // 3. Chart Tren Bulanan & 4. Leaderboard Chart
        // --> Diinisialisasi saat tab "Kinerja Perusahaan" diklik (lazy init)
        //     agar tidak crash karena canvas dalam display:none

        // Track whether company charts are already initialized
        window._companyChartsInitialized = false;

        // Pre-populate trend data at load time (Razor server-side as JSON)
        const _trendLabels  = "{}".ToList()));
        const _trendHazard  = "{}".ToList()));
        const _trendInspect = "{}".ToList()));
        const _trendTalk    = "{}".ToList()));
        const _trendP5m     = "{}".ToList()));
        const _hitSafeHazardPointsRaw = "{}"));
        const _hitSafeInspectionPointsRaw = "{}"));
        const _hitSafeP5mPointsRaw = "{}"));
        const _hitSafeSafetyTalkPointsRaw = "{}"));
        const _hitSafeCanViewPhoto = 0;

        window._initCompanyCharts = function() {
            if (window._companyChartsInitialized) return;
            window._companyChartsInitialized = true;

            const trendEl = document.getElementById('trendChart');
            if (trendEl) {
                new Chart(trendEl.getContext('2d'), {
                    type: 'line',
                    data: {
                        labels: _trendLabels,
                        datasets: [
                            { label: 'Hazard',      data: _trendHazard,  borderColor: '#f43f5e', backgroundColor: 'rgba(244,63,94,0.1)',   fill: true, tension: 0.3, borderWidth: 3 },
                            { label: 'Inspeksi',    data: _trendInspect, borderColor: '#3b82f6', backgroundColor: 'rgba(59,130,246,0.1)',  fill: true, tension: 0.3, borderWidth: 3 },
                            { label: 'Safety Talk', data: _trendTalk,    borderColor: '#a855f7', backgroundColor: 'rgba(168,85,247,0.1)', fill: true, tension: 0.3, borderWidth: 3 },
                            { label: 'P5M',         data: _trendP5m,     borderColor: '#10b981', backgroundColor: 'rgba(16,185,129,0.1)', fill: true, tension: 0.3, borderWidth: 3 }
                        ]
                    },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        plugins: {
                            legend: { position: 'top', labels: { color: textColor, font: { weight: 'bold' } } }
                        },
                        scales: {
                            x: { grid: { color: gridColor }, ticks: { color: textColor, font: { weight: 'bold' } } },
                            y: { grid: { color: gridColor }, ticks: { color: textColor, font: { weight: 'bold' } } }
                        }
                    }
                });
            }
            createGauge('gaugeComplianceClose', 0.ToString(System.Globalization.CultureInfo.InvariantCulture)), true);
            createGauge('gaugeOverdue', 0.ToString(System.Globalization.CultureInfo.InvariantCulture)), false);
            createGauge('gaugeComplianceRisk', 0.ToString(System.Globalization.CultureInfo.InvariantCulture)), false);
            createGauge('gaugeRRI', 0.ToString(System.Globalization.CultureInfo.InvariantCulture)), true);
            createGauge('gaugeRHR', 0.ToString(System.Globalization.CultureInfo.InvariantCulture)), false);
            createGauge('gaugeHighRisk', 0.ToString(System.Globalization.CultureInfo.InvariantCulture)), true);

            // Inisialisasi Chart Top 5 Repeated Hazards secara lazy
            const repeatedEl = document.getElementById('repeatedHazardsChart');
            if (repeatedEl) {
                const repeatedLabels = [];
                const repeatedData = [];
                @if (ViewBag.TopRepeatedLabels != null) {
                    foreach (var label in ViewBag.TopRepeatedLabels)
                    {
                        <text>repeatedLabels.push('"{}"');</text>
                    }
                    foreach (var count in ViewBag.TopRepeatedData)
                    {
                        <text>repeatedData.push(@count);</text>
                    }
                }

                new Chart(repeatedEl.getContext('2d'), {
                    type: 'bar',
                    data: {
                        labels: repeatedLabels,
                        datasets: [{
                            label: 'Jumlah Hazard Berulang per Lokasi',
                            data: repeatedData,
                            backgroundColor: 'rgba(168, 85, 247, 0.85)',
                            borderRadius: 6
                        }]
                    },
                    options: {
                        indexAxis: 'y',
                        responsive: true,
                        maintainAspectRatio: false,
                        plugins: {
                            legend: { display: false }
                        },
                        scales: {
                            x: {
                                grid: { color: gridColor },
                                ticks: { color: textColor, precision: 0 }
                            },
                            y: {
                                grid: { display: false },
                                ticks: { color: textColor, font: { size: 10, weight: 'bold' } }
                            }
                        }
                    }
                });
            }

            // Inisialisasi Kategori Bahaya (KTA vs TTA) secara lazy
            const ctxKategori = document.getElementById('chartKategoriBahaya');
            if (ctxKategori) {
                new Chart(ctxKategori.getContext('2d'), {
                    type: 'doughnut',
                    data: {
                        labels: ['Tindakan Tidak Aman (KTA)', 'Kondisi Tidak Aman (TTA)'],
                        datasets: [{
                            data: [0, 0],
                            backgroundColor: ['rgba(245, 158, 11, 0.85)', 'rgba(59, 130, 246, 0.85)'],
                            borderWidth: 2,
                            borderColor: isDark ? '#1e293b' : '#ffffff'
                        }]
                    },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        plugins: {
                            legend: { position: 'bottom', labels: { color: textColor } }
                        }
                    }
                });
            }

            // Inisialisasi Chart Perbandingan Kepatuhan Perusahaan secara lazy
            const leaderboardEl = document.getElementById('leaderboardChart');
            if (leaderboardEl) {
                const leaderboardLabels = [];
                const leaderboardData = [];
                @if (ViewBag.Leaderboard != null) {
                    foreach (var comp in ViewBag.Leaderboard)
                    {
                        <text>leaderboardLabels.push('"{}"');</text>
                        <text>leaderboardData.push(@comp.AchievementRate.ToString(System.Globalization.CultureInfo.InvariantCulture));</text>
                    }
                }

                new Chart(leaderboardEl.getContext('2d'), {
                    type: 'bar',
                    data: {
                        labels: leaderboardLabels,
                        datasets: [{
                            label: 'Pencapaian Kepatuhan (%)',
                            data: leaderboardData,
                            backgroundColor: 'rgba(56, 189, 248, 0.85)',
                            borderRadius: 6
                        }]
                    },
                    options: {
                        indexAxis: 'y',
                        responsive: true,
                        maintainAspectRatio: false,
                        plugins: {
                            legend: { display: false }
                        },
                        scales: {
                            x: {
                                grid: { color: gridColor },
                                ticks: { color: textColor },
                                suggestedMax: 100 // Safe suggested max value for scale
                            },
                            y: {
                                grid: { display: false },
                                ticks: { color: textColor, font: { weight: 'bold', size: 10 } }
                            }
                        }
                    }
                });
            }
            // SA Type Chart Initialization
            const saTypeEl = document.getElementById('saTypeChart');
            if (saTypeEl) {
                const saLabels = [];
                const saTargetData = [];
                const saRealData = [];
                @if (ViewBag.SaTypeLabels != null) {
                    foreach (var label in ViewBag.SaTypeLabels) { <text>saLabels.push('"{}"');</text> }
                    foreach (var val in ViewBag.SaTypeTargetData) { <text>saTargetData.push(@val);</text> }
                    foreach (var val in ViewBag.SaTypeRealData) { <text>saRealData.push(@val);</text> }
                }

                new Chart(saTypeEl.getContext('2d'), {
                    type: 'radar',
                    data: {
                        labels: saLabels,
                        datasets: [
                            {
                                label: 'Realisasi (YTD)',
                                data: saRealData,
                                backgroundColor: 'rgba(56, 189, 248, 0.4)',
                                borderColor: 'rgba(56, 189, 248, 1)',
                                borderWidth: 2,
                                pointBackgroundColor: 'rgba(56, 189, 248, 1)'
                            },
                            {
                                label: 'Target (YTD)',
                                data: saTargetData,
                                backgroundColor: 'rgba(244, 63, 94, 0.2)',
                                borderColor: 'rgba(244, 63, 94, 1)',
                                borderWidth: 2,
                                pointBackgroundColor: 'rgba(244, 63, 94, 1)'
                            }
                        ]
                    },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        plugins: {
                            legend: { position: 'top', labels: { color: textColor } }
                        },
                        scales: {
                            r: {
                                angleLines: { color: gridColor },
                                grid: { color: gridColor },
                                pointLabels: { color: textColor, font: { weight: 'bold' } },
                                ticks: { backdropColor: 'transparent', color: textColor, beginAtZero: true }
                            }
                        }
                    }
                });
            }

            // Company Achievement Chart Initialization
            const companyAchievementEl = document.getElementById('companyAchievementChart');
            if (companyAchievementEl) {
                const caLabels = [];
                const caRealData = [];
                const caTargetData = [];
                @if (ViewBag.Leaderboard != null) {
                    foreach (var comp in ViewBag.Leaderboard) {
                        <text>caLabels.push('"{}"');</text>
                        <text>caRealData.push(@comp.TotalSubmissions);</text>
                        <text>caTargetData.push(@comp.TargetSubmissions);</text>
                    }
                }

                new Chart(companyAchievementEl.getContext('2d'), {
                    type: 'bar',
                    data: {
                        labels: caLabels,
                        datasets: [
                            {
                                label: 'Realisasi Total',
                                data: caRealData,
                                backgroundColor: 'rgba(16, 185, 129, 0.85)',
                                borderRadius: 4
                            },
                            {
                                label: 'Target SAP',
                                data: caTargetData,
                                backgroundColor: 'rgba(100, 116, 139, 0.5)',
                                borderRadius: 4
                            }
                        ]
                    },
                    options: {
                        indexAxis: 'y',
                        responsive: true,
                        maintainAspectRatio: false,
                        plugins: {
                            legend: { position: 'top', labels: { color: textColor } }
                        },
                        scales: {
                            x: {
                                grid: { color: gridColor },
                                ticks: { color: textColor }
                            },
                            y: {
                                grid: { display: false },
                                ticks: { color: textColor, font: { size: 10, weight: 'bold' } }
                            }
                        }
                    }
                });
            }
        }; // end window._initCompanyCharts

        let hitSafeMap = null;
        let hitSafeAnchorLayer = null;
        let hitSafeReportLayer = null;
        let hitSafeClusterLayer = null;
        let hitSafeUserMarker = null;
        let hitSafeUserAccuracyCircle = null;
        let hitSafeGeolocAttempted = false;
        let hitSafeWatchId = null;
        let hitSafeAutoCenter = true;
        let hitSafeAllPoints = [];
        let hitSafeFilteredPoints = [];
        let hitSafeMarkerByKey = new Map();
        let hitSafeFiltersInitialized = false;
        const hitSafeUserIcon = L.divIcon({
            className: 'hitsafe-user-marker-wrap',
            html: '<div class="hitsafe-user-marker-icon"><i class="bi bi-person-fill"></i></div>',
            iconSize: [30, 30],
            iconAnchor: [15, 15]
        });
        const kaliorangLat = -0.9907;
        const kaliorangLon = 117.9006;
        const hitSafeHazardPoints = Array.isArray(_hitSafeHazardPointsRaw) ? _hitSafeHazardPointsRaw : [];
        const hitSafeInspectionPoints = Array.isArray(_hitSafeInspectionPointsRaw) ? _hitSafeInspectionPointsRaw : [];
        const hitSafeP5mPoints = Array.isArray(_hitSafeP5mPointsRaw) ? _hitSafeP5mPointsRaw : [];
        const hitSafeSafetyTalkPoints = Array.isArray(_hitSafeSafetyTalkPointsRaw) ? _hitSafeSafetyTalkPointsRaw : [];

        function escapeHtml(text) {
            return String(text ?? '')
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        function normalizePoint(point) {
            const lat = Number(point.lat ?? point.Lat);
            const lon = Number(point.lon ?? point.Lon);
            if (!Number.isFinite(lat) || !Number.isFinite(lon)) {
                return null;
            }
            return {
                id: point.id ?? point.Id,
                lat: lat,
                lon: lon,
                tanggal: point.tanggal ?? point.Tanggal,
                nama: point.nama ?? point.Nama,
                area: point.area ?? point.Area,
                detail: point.detail ?? point.Detail,
                resiko: point.resiko ?? point.Resiko,
                status: point.status ?? point.Status,
                photoUrl: point.photoUrl ?? point.PhotoUrl
            };
        }

        function normalizeText(value) {
            return String(value ?? '').trim().toLowerCase();
        }

        function buildHitSafeSourcePoints() {
            const points = [];
            let sequence = 0;
            const seenSignatures = new Set();
            const seenIds = new Set();

            function registerPoint(type, normalized) {
                const normalizedId = String(normalized.id ?? '').trim();
                if (normalizedId) {
                    const idKey = type + '|' + normalizedId;
                    if (seenIds.has(idKey)) {
                        return;
                    }
                    seenIds.add(idKey);
                }

                const signature = [
                    type,
                    String(normalized.lat ?? ''),
                    String(normalized.lon ?? ''),
                    String(normalized.tanggal ?? ''),
                    String(normalized.nama ?? ''),
                    String(normalized.detail ?? ''),
                    String(normalized.area ?? '')
                ].join('|');

                if (!normalizedId && seenSignatures.has(signature)) {
                    return;
                }
                if (!normalizedId) {
                    seenSignatures.add(signature);
                }

                points.push({
                    key: type + '-' + String(normalized.id ?? sequence) + '-' + String(sequence),
                    type: type,
                    ...normalized
                });
                sequence += 1;
            }

            hitSafeHazardPoints.forEach(function(raw) {
                const normalized = normalizePoint(raw);
                if (!normalized) {
                    return;
                }
                registerPoint('hazard', normalized);
            });

            hitSafeInspectionPoints.forEach(function(raw) {
                const normalized = normalizePoint(raw);
                if (!normalized) {
                    return;
                }
                registerPoint('inspection', normalized);
            });

            hitSafeP5mPoints.forEach(function(raw) {
                const normalized = normalizePoint(raw);
                if (!normalized) {
                    return;
                }
                registerPoint('p5m', normalized);
            });

            hitSafeSafetyTalkPoints.forEach(function(raw) {
                const normalized = normalizePoint(raw);
                if (!normalized) {
                    return;
                }
                registerPoint('safetytalk', normalized);
            });

            return points;
        }

        function getHitSafeFilterValues() {
            const typeEl = document.getElementById('hitsafeFilterType');
            const statusEl = document.getElementById('hitsafeFilterStatus');
            const riskEl = document.getElementById('hitsafeFilterRisk');
            const keywordEl = document.getElementById('hitsafeFilterKeyword');

            return {
                type: typeEl ? typeEl.value : 'all',
                status: statusEl ? statusEl.value : 'all',
                risk: riskEl ? riskEl.value : 'all',
                keyword: normalizeText(keywordEl ? keywordEl.value : '')
            };
        }

        function filterHitSafePoints() {
            const filters = getHitSafeFilterValues();

            return hitSafeAllPoints.filter(function(point) {
                if (filters.type !== 'all' && point.type !== filters.type) {
                    return false;
                }

                if (filters.status !== 'all') {
                    const status = normalizeText(point.status);
                    if (status !== filters.status) {
                        return false;
                    }
                }

                if (filters.risk !== 'all') {
                    const risk = normalizeText(point.resiko);
                    if (filters.risk === 'low' && !(risk.includes('rendah') || risk.includes('low'))) {
                        return false;
                    }
                    if (filters.risk === 'medium' && !(risk.includes('sedang') || risk.includes('medium'))) {
                        return false;
                    }
                    if (filters.risk === 'high' && !(risk.includes('tinggi') || risk.includes('high'))) {
                        return false;
                    }
                    if (filters.risk === 'extreme' && !(risk.includes('ekstrem') || risk.includes('extreme'))) {
                        return false;
                    }
                }

                if (filters.keyword) {
                    const corpus = [point.area, point.nama, point.detail, point.status, point.resiko].map(normalizeText).join(' ');
                    if (!corpus.includes(filters.keyword)) {
                        return false;
                    }
                }

                return true;
            });
        }

        function applyHitSafeFiltersAndRender(shouldFit) {
            const summary = renderHitSafeReportPoints();

            if (shouldFit && hitSafeMap) {
                if (summary.bounds) {
                    hitSafeMap.fitBounds(summary.bounds.pad(0.2));
                } else {
                    hitSafeMap.setView([kaliorangLat, kaliorangLon], 11);
                }
            }
        }

        function initHitSafeFilters() {
            if (hitSafeFiltersInitialized) {
                return;
            }

            const ids = ['hitsafeFilterType', 'hitsafeFilterStatus', 'hitsafeFilterRisk', 'hitsafeFilterKeyword'];
            ids.forEach(function(id) {
                const element = document.getElementById(id);
                if (!element) {
                    return;
                }
                const eventName = id === 'hitsafeFilterKeyword' ? 'input' : 'change';
                element.addEventListener(eventName, function() {
                    applyHitSafeFiltersAndRender(true);
                });
            });

            const resetButton = document.getElementById('hitsafeFilterReset');
            if (resetButton) {
                resetButton.addEventListener('click', function() {
                    const typeEl = document.getElementById('hitsafeFilterType');
                    const statusEl = document.getElementById('hitsafeFilterStatus');
                    const riskEl = document.getElementById('hitsafeFilterRisk');
                    const keywordEl = document.getElementById('hitsafeFilterKeyword');
                    if (typeEl) typeEl.value = 'all';
                    if (statusEl) statusEl.value = 'all';
                    if (riskEl) riskEl.value = 'all';
                    if (keywordEl) keywordEl.value = '';
                    applyHitSafeFiltersAndRender(true);
                });
            }

            const locateButton = document.getElementById('hitsafeLocateBtn');
            if (locateButton) {
                locateButton.addEventListener('click', function() {
                    hitSafeAutoCenter = true;
                    locateButton.classList.add('btn-success', 'text-white');
                    locateButton.style.background = '';
                    locateButton.style.color = '';
                    locateButton.style.borderColor = '';
                    locateButton.innerHTML = '<i class="bi bi-crosshair text-success"></i> Mengikuti...';
                    updateHitSafeToUserLocation();
                });
            }

            hitSafeFiltersInitialized = true;
        }

        function buildPointPopup(type, point) {
            const badgeMap = {
                hazard: { text: 'Hazard', className: 'hitsafe-popup-badge-hazard', detailLabel: 'Detail Temuan' },
                inspection: { text: 'Inspection', className: 'hitsafe-popup-badge-inspection', detailLabel: 'Jenis Inspeksi' },
                p5m: { text: 'P5M', className: 'hitsafe-popup-badge-p5m', detailLabel: 'Topik P5M' },
                safetytalk: { text: 'Safety Talk', className: 'hitsafe-popup-badge-safetytalk', detailLabel: 'Judul / Materi' }
            };
            const meta = badgeMap[type] || badgeMap.inspection;
            const isHazard = type === 'hazard';

            const rows = [];
            if (point.area) {
                rows.push('<div class="hitsafe-popup-label">Area</div><div class="hitsafe-popup-value">' + escapeHtml(point.area) + '</div>');
            }
            if (point.tanggal) {
                rows.push('<div class="hitsafe-popup-label">Tanggal</div><div class="hitsafe-popup-value">' + escapeHtml(point.tanggal) + '</div>');
            }
            if (point.nama) {
                rows.push('<div class="hitsafe-popup-label">Pelapor</div><div class="hitsafe-popup-value">' + escapeHtml(point.nama) + '</div>');
            }
            if (isHazard && point.resiko) {
                rows.push('<div class="hitsafe-popup-label">Risiko</div><div class="hitsafe-popup-value">' + escapeHtml(point.resiko) + '</div>');
            }
            if (isHazard && point.status) {
                rows.push('<div class="hitsafe-popup-label">Status</div><div class="hitsafe-popup-value">' + escapeHtml(point.status) + '</div>');
            }

            const detailBlock = point.detail
                ? '<div class="hitsafe-popup-detail"><strong>' + meta.detailLabel + ':</strong><br>' + escapeHtml(point.detail) + '</div>'
                : '';

            const photoBlock = (_hitSafeCanViewPhoto && point.photoUrl)
                ? '<div class="hitsafe-popup-photo-wrap"><img class="hitsafe-popup-photo" src="' + escapeHtml(point.photoUrl) + '" alt="Foto laporan" loading="lazy" /></div>'
                : '';

            return [
                '<div class="hitsafe-popup">',
                '<div class="hitsafe-popup-header">',
                '<div class="hitsafe-popup-title">Laporan Area</div>',
                '<span class="hitsafe-popup-badge ' + meta.className + '">' + meta.text + '</span>',
                '</div>',
                '<div class="hitsafe-popup-grid">',
                rows.join(''),
                '</div>',
                photoBlock,
                detailBlock,
                '</div>'
            ].join('');
        }

        function renderHitSafeReportPoints() {
            if (!hitSafeReportLayer) {
                return { count: 0, bounds: null };
            }

            hitSafeReportLayer.clearLayers();
            hitSafeMarkerByKey.clear();
            if (hitSafeClusterLayer) {
                hitSafeClusterLayer.clearLayers();
            }

            hitSafeFilteredPoints = filterHitSafePoints();
            const bounds = [];

            hitSafeFilteredPoints.forEach(function(point) {
                const popup = buildPointPopup(point.type, point);
                const markerColors = {
                    hazard: '#f43f5e',
                    inspection: '#3b82f6',
                    p5m: '#10b981',
                    safetytalk: '#f59e0b'
                };
                const markerColor = markerColors[point.type] || '#3b82f6';
                const marker = L.marker([point.lat, point.lon], {
                    icon: L.divIcon({
                        className: 'hitsafe-map-pin-wrapper',
                        html: '<span style="display:block;width:14px;height:14px;border-radius:50%;border:2px solid #ffffff;background:' + markerColor + ';box-shadow:0 0 0 1px rgba(15,23,42,0.7);"></span>',
                        iconSize: [14, 14],
                        iconAnchor: [7, 7]
                    })
                }).bindPopup(popup, {
                    maxWidth: 320,
                    className: 'hitsafe-popup-shell'
                });

                hitSafeMarkerByKey.set(point.key, marker);

                if (hitSafeClusterLayer) {
                    hitSafeClusterLayer.addLayer(marker);
                } else {
                    marker.addTo(hitSafeReportLayer);
                }

                bounds.push([point.lat, point.lon]);
            });

            return {
                count: bounds.length,
                bounds: bounds.length > 0 ? L.latLngBounds(bounds) : null
            };
        }

        function renderKaliorangFallback() {
            if (!hitSafeAnchorLayer) {
                return;
            }

            hitSafeAnchorLayer.clearLayers();
            L.marker([kaliorangLat, kaliorangLon])
                .bindPopup('<strong>Kaliorang, Kalimantan Timur</strong><br>Area fokus Safe Map.')
                .addTo(hitSafeAnchorLayer);

            L.circle([kaliorangLat, kaliorangLon], {
                radius: 1800,
                color: '#0ea5e9',
                weight: 2,
                fillColor: '#38bdf8',
                fillOpacity: 0.15
            }).addTo(hitSafeAnchorLayer);
        }

        function applyUserLocation(position) {
            const lat = position.coords.latitude;
            const lon = position.coords.longitude;
            const accuracy = Math.max(25, Math.round(position.coords.accuracy || 25));

            if (hitSafeUserMarker) {
                hitSafeMap.removeLayer(hitSafeUserMarker);
                hitSafeUserMarker = null;
            }
            if (hitSafeUserAccuracyCircle) {
                hitSafeMap.removeLayer(hitSafeUserAccuracyCircle);
                hitSafeUserAccuracyCircle = null;
            }

            hitSafeUserMarker = L.marker([lat, lon], {
                icon: hitSafeUserIcon
            }).addTo(hitSafeMap)
                .bindPopup('<strong>Lokasi Anda Saat Ini</strong><br>Map diposisikan ke GPS browser.');
            hitSafeUserAccuracyCircle = L.circle([lat, lon], {
                radius: accuracy,
                color: '#16a34a',
                weight: 2,
                fillColor: '#4ade80',
                fillOpacity: 0.18
            }).addTo(hitSafeMap);

            if (hitSafeAutoCenter) {
                hitSafeMap.setView([lat, lon], 15);
            }
            window.setTimeout(function() {
                if (hitSafeMap) {
                    hitSafeMap.invalidateSize();
                }
            }, 120);
        }

        function fallbackToKaliorang() {
            if (!hitSafeMap) {
                return;
            }

            if (hitSafeUserMarker) {
                hitSafeMap.removeLayer(hitSafeUserMarker);
                hitSafeUserMarker = null;
            }
            if (hitSafeUserAccuracyCircle) {
                hitSafeMap.removeLayer(hitSafeUserAccuracyCircle);
                hitSafeUserAccuracyCircle = null;
            }

            const reportSummary = renderHitSafeReportPoints();
            if (reportSummary.bounds) {
                hitSafeMap.fitBounds(reportSummary.bounds.pad(0.2));
            } else {
                renderKaliorangFallback();
                hitSafeMap.setView([kaliorangLat, kaliorangLon], 11);
            }
        }

        function updateHitSafeToUserLocation() {
            if (!hitSafeMap || typeof navigator === 'undefined' || !navigator.geolocation) {
                return;
            }

            navigator.geolocation.getCurrentPosition(function(position) {
                applyUserLocation(position);
            }, function() {
                fallbackToKaliorang();
            }, {
                enableHighAccuracy: true,
                timeout: 10000,
                maximumAge: 15000
            });
        }

        window.stopHitSafeLocationTracking = function() {
            if (typeof navigator !== 'undefined' && navigator.geolocation && hitSafeWatchId !== null) {
                navigator.geolocation.clearWatch(hitSafeWatchId);
            }
            hitSafeWatchId = null;
        };

        function startHitSafeLocationTracking() {
            if (!hitSafeMap || typeof navigator === 'undefined' || !navigator.geolocation || hitSafeWatchId !== null) {
                return;
            }

            hitSafeWatchId = navigator.geolocation.watchPosition(function(position) {
                applyUserLocation(position);
            }, function() {
                fallbackToKaliorang();
                window.stopHitSafeLocationTracking();
            }, {
                enableHighAccuracy: true,
                timeout: 15000,
                maximumAge: 10000
            });
        }

        window.initHitSafeMap = function(retryCount) {
            const container = document.getElementById('hitsafeLeafletMap');
            if (!container) {
                return;
            }

            if (typeof L === 'undefined') {
                container.innerHTML = '<div style="height:100%;display:flex;align-items:center;justify-content:center;font-weight:700;color:var(--text-muted);">Leaflet belum termuat.</div>';
                return;
            }

            const rect = container.getBoundingClientRect();
            if (rect.width <= 0 || rect.height <= 0) {
                const attemptsLeft = typeof retryCount === 'number' ? retryCount : 15;
                if (attemptsLeft > 0) {
                    window.setTimeout(function() {
                        window.initHitSafeMap(attemptsLeft - 1);
                    }, 120);
                }
                return;
            }

            if (!hitSafeMap) {
                hitSafeMap = L.map('hitsafeLeafletMap', {
                    attributionControl: false
                }).setView([kaliorangLat, kaliorangLon], 11);
                L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
                    maxZoom: 19,
                    attribution: '&copy; OpenStreetMap contributors'
                }).addTo(hitSafeMap);
                hitSafeAnchorLayer = L.layerGroup().addTo(hitSafeMap);
                hitSafeReportLayer = L.layerGroup().addTo(hitSafeMap);
                if (typeof L.markerClusterGroup === 'function') {
                    hitSafeClusterLayer = L.markerClusterGroup({
                        showCoverageOnHover: false,
                        spiderfyOnMaxZoom: true,
                        maxClusterRadius: 40
                    }).addTo(hitSafeMap);
                }

                hitSafeAllPoints = buildHitSafeSourcePoints();
                initHitSafeFilters();
                applyHitSafeFiltersAndRender(true);

                // Disable auto-centering when the user interacts with the map
                hitSafeMap.on('dragstart zoomstart movestart', function() {
                    hitSafeAutoCenter = false;
                    const locateButton = document.getElementById('hitsafeLocateBtn');
                    if (locateButton) {
                        locateButton.classList.remove('btn-success', 'text-white');
                        locateButton.style.background = '#0f172a';
                        locateButton.style.color = '#f8fafc';
                        locateButton.style.borderColor = '#1e293b';
                        locateButton.innerHTML = '<i class="bi bi-crosshair"></i> Lokasi Saya';
                    }
                });

                if (hitSafeFilteredPoints.length === 0) {
                    renderKaliorangFallback();
                } else {
                    hitSafeAnchorLayer.clearLayers();
                }
            }

            if (!hitSafeGeolocAttempted) {
                hitSafeGeolocAttempted = true;
                updateHitSafeToUserLocation();
                startHitSafeLocationTracking();
            } else if (hitSafeUserMarker) {
                const userPoint = hitSafeUserMarker.getLatLng();
                hitSafeMap.setView([userPoint.lat, userPoint.lng], 15);
                startHitSafeLocationTracking();
            } else {
                updateHitSafeToUserLocation();
                startHitSafeLocationTracking();
            }

            window.setTimeout(function() {
                if (hitSafeMap) {
                    hitSafeMap.invalidateSize();
                }
            }, 120);
        };

        if (document.getElementById('sectionHitSafe') && document.getElementById('sectionHitSafe').style.display === 'block') {
            window.requestAnimationFrame(function() {
                window.initHitSafeMap(20);
            });
        }

    });

