document.addEventListener('DOMContentLoaded', function () {
    const themeToggleBtn = document.getElementById('themeToggle');
    const themeIcon = document.getElementById('themeIcon');
    
    // Function to update the icon based on current class
    function updateThemeIcon() {
        if (themeIcon) {
            if (document.body.classList.contains('light-theme')) {
                themeIcon.className = 'bi bi-sun-fill';
                themeIcon.style.color = '#eab308'; // Amber sun color
            } else {
                themeIcon.className = 'bi bi-moon-stars-fill';
                themeIcon.style.color = ''; // Default CSS color
            }
        }
    }

    // Initialize icon status on page load
    updateThemeIcon();

    if (themeToggleBtn) {
        themeToggleBtn.addEventListener('click', function () {
            // Toggle light theme class
            document.body.classList.toggle('light-theme');
            
            // Determine active theme
            const activeTheme = document.body.classList.contains('light-theme') ? 'light' : 'dark';
            
            // Save selection to LocalStorage
            localStorage.setItem('sap-theme', activeTheme);
            
            // Update UI icon
            updateThemeIcon();
        });
    }

    // Prevent double form submissions globally
    document.querySelectorAll('form').forEach(function(form) {
        form.addEventListener('submit', function(e) {
            if (form.classList.contains('is-submitting')) {
                e.preventDefault();
                return;
            }
            form.classList.add('is-submitting');
            
            const submitBtn = form.querySelector('button[type="submit"]');
            if (submitBtn) {
                setTimeout(function() {
                    // Use pointer-events instead of disabling to avoid aborting form submission
                    submitBtn.style.pointerEvents = 'none';
                    if (submitBtn.innerHTML.indexOf('spinner-border') === -1) {
                        const originalText = submitBtn.innerText;
                        submitBtn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true" style="margin-right: 8px;"></span>Menyimpan...';
                        submitBtn.setAttribute('data-original-text', originalText);
                    }
                }, 10);
            }
        });
    });
});

