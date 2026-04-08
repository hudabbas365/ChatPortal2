// ChatPortal2 - Main App JS
(function() {
    'use strict';

    // Attach JWT to all AJAX requests
    const token = localStorage.getItem('cp_token');
    if (token && typeof $ !== 'undefined') {
        $(document).ajaxSend(function(event, jqxhr, settings) {
            jqxhr.setRequestHeader('Authorization', 'Bearer ' + token);
        });
    }

    // Update nav auth button based on login state
    function updateNavAuth() {
        const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
        const authBtn = document.getElementById('navAuthBtn');
        if (!authBtn) return;
        if (user) {
            authBtn.innerHTML = `<i class="bi bi-person-circle me-1"></i>${user.fullName || user.email}`;
            authBtn.href = '#';
            authBtn.addEventListener('click', function(e) {
                e.preventDefault();
                if (confirm('Sign out?')) {
                    fetch('/api/auth/logout', { method: 'POST' }).then(() => {
                        localStorage.removeItem('cp_user');
                        localStorage.removeItem('cp_token');
                        localStorage.removeItem('cp_plan');
                        window.location.href = '/';
                    });
                }
            });
        }
    }

    // Load subscription info
    async function loadPlan() {
        const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
        if (!user) return;
        try {
            const r = await fetch('/api/subscription/' + user.id);
            const plan = await r.json();
            localStorage.setItem('cp_plan', JSON.stringify(plan));
        } catch {}
    }

    document.addEventListener('DOMContentLoaded', function() {
        updateNavAuth();
        loadPlan();

        // Auto-resize textareas
        document.querySelectorAll('textarea[rows="1"]').forEach(function(ta) {
            ta.addEventListener('input', function() {
                this.style.height = 'auto';
                this.style.height = Math.min(this.scrollHeight, 200) + 'px';
            });
        });
    });

    // Global send suggestion helper for chat page
    window.sendSuggestion = function(btn) {
        const input = document.getElementById('chatInput');
        if (input) {
            input.value = btn.textContent;
            input.dispatchEvent(new Event('input'));
            const sendBtn = document.getElementById('chatSendBtn');
            if (sendBtn) sendBtn.click();
        }
    };
})();
