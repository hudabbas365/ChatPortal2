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
    async function updateNavAuth() {
        const authBtn = document.getElementById('navAuthBtn');
        const logoutBtn = document.getElementById('navLogoutBtn');
        const dashboardBtn = document.getElementById('navDashboardBtn');
        const chatAuthBtn = document.getElementById('chatAuthBtn');

        let user = JSON.parse(localStorage.getItem('cp_user') || 'null');

        // Verify auth state with server
        try {
            const r = await fetch('/api/auth/me');
            if (r.ok) {
                const data = await r.json();
                user = data;
                localStorage.setItem('cp_user', JSON.stringify(user));
            } else {
                // Server says not authenticated — clear stale local data
                user = null;
                localStorage.removeItem('cp_user');
                localStorage.removeItem('cp_token');
            }
        } catch (e) {
            console.error('Auth verification failed:', e);
        }

        if (user) {
            if (authBtn) authBtn.style.display = 'none';
            if (logoutBtn && !logoutBtn._logoutWired) {
                logoutBtn._logoutWired = true;
                logoutBtn.addEventListener('click', function() {
                    fetch('/api/auth/logout', { method: 'POST' }).finally(function() {
                        localStorage.removeItem('cp_user');
                        localStorage.removeItem('cp_token');
                        localStorage.removeItem('cp_plan');
                        window.location.href = '/';
                    });
                });
            }
            if (logoutBtn) logoutBtn.style.display = '';
            if (dashboardBtn) dashboardBtn.style.display = '';
            if (chatAuthBtn) {
                chatAuthBtn.innerHTML = `<i class="bi bi-person-circle me-1"></i>${user.fullName || user.email}`;
                chatAuthBtn.href = '#';
            }
        } else {
            if (authBtn) authBtn.style.display = '';
            if (logoutBtn) logoutBtn.style.display = 'none';
            if (dashboardBtn) dashboardBtn.style.display = 'none';
        }

        // Update left panel org name
        const orgNameEl = document.getElementById('orgName');
        if (orgNameEl && user) {
            orgNameEl.textContent = user.orgName || user.fullName || 'My Organization';
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

    // Load workspaces into left panel
    async function loadWorkspaces() {
        const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
        if (!user || !user.organizationId) return;
        try {
            const r = await fetch('/api/workspaces?organizationId=' + user.organizationId);
            const workspaces = await r.json();
            const list = document.getElementById('workspaceList');
            if (!list) return;
            if (workspaces.length) {
                list.innerHTML = workspaces.map(w =>
                    `<div class="panel-list-item" data-workspace-id="${w.guid}">
                        <i class="bi bi-folder me-2"></i>${_escHtml(w.name)}<span class="wf-ws-status unconfigured" title="Needs setup"></span>
                     </div>`
                ).join('');
            }
        } catch {}
    }

    function _escHtml(str) {
        var d = document.createElement('div');
        d.appendChild(document.createTextNode(str || ''));
        return d.innerHTML;
    }

    document.addEventListener('DOMContentLoaded', function() {
        updateNavAuth();
        loadPlan();
        loadWorkspaces();

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
