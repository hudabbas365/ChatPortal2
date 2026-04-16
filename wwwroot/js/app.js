// AIInsights - Main App JS
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
        const chatLogoutBtn = document.getElementById('chatLayoutLogoutBtn');
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
            function wireLogout(btn) {
                if (!btn || btn._logoutWired) return;
                btn._logoutWired = true;
                btn.addEventListener('click', function() {
                    fetch('/api/auth/logout', { method: 'POST' }).finally(function() {
                        localStorage.removeItem('cp_user');
                        localStorage.removeItem('cp_token');
                        localStorage.removeItem('cp_plan');
                        window.location.href = '/';
                    });
                });
            }
            wireLogout(logoutBtn);
            wireLogout(chatLogoutBtn);
            if (logoutBtn) logoutBtn.style.display = '';
            if (chatLogoutBtn) chatLogoutBtn.style.display = '';
            if (dashboardBtn) dashboardBtn.style.display = '';
            if (chatAuthBtn) {
                chatAuthBtn.innerHTML = `<i class="bi bi-person-circle me-1"></i>${user.fullName || user.email}`;
                chatAuthBtn.href = '#';
            }
        } else {
            if (authBtn) authBtn.style.display = '';
            if (logoutBtn) logoutBtn.style.display = 'none';
            if (chatLogoutBtn) chatLogoutBtn.style.display = 'none';
            if (dashboardBtn) dashboardBtn.style.display = 'none';
        }

        const settingsLink = document.getElementById('orgSettingsLink');
        const activityLink = document.getElementById('activityLink');
        if (settingsLink && user && user.role !== 'OrgAdmin' && user.role !== 'SuperAdmin') {
            settingsLink.style.display = 'none';
        }
        if (activityLink && user && (user.role === 'OrgAdmin' || user.role === 'SuperAdmin')) {
            activityLink.style.display = '';
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
                        ${w.logoUrl
                            ? `<img src="${_escHtml(w.logoUrl)}" alt="" style="width:18px;height:18px;border-radius:3px;object-fit:cover;margin-right:8px;">`
                            : '<i class="bi bi-folder me-2"></i>'}${_escHtml(w.name)}<span class="wf-ws-status unconfigured" title="Needs setup"></span>
                     </div>`
                ).join('');
                list.querySelectorAll('.panel-list-item').forEach(function (el, i) {
                    el.title = workspaces[i]?.description || '';
                });
            }
        } catch {}
    }

    function wireUpgradeButtons() {
        document.querySelectorAll('.btn-upgrade-now, #upgradeNowBtn').forEach(function(btn) {
            if (btn._upgradeWired) return;
            btn._upgradeWired = true;
            btn.addEventListener('click', function(e) {
                e.preventDefault();
                var user = JSON.parse(localStorage.getItem('cp_user') || 'null');
                if (!user) { window.location.href = '/auth/login'; return; }
                if (user.role !== 'OrgAdmin' && user.role !== 'SuperAdmin') {
                    showContactAdminUpgradeModal(user);
                } else {
                    window.location.href = '/admin/billing';
                }
            });
        });
    }

    function showContactAdminUpgradeModal(user) {
        var existing = document.getElementById('cpUpgradeModalOverlay');
        if (existing) existing.remove();
        var modal = document.createElement('div');
        modal.id = 'cpUpgradeModalOverlay';
        modal.className = 'cp-modal-overlay';
        modal.innerHTML = `
            <div class="cp-modal" style="max-width:420px;background:#fff;padding:20px;border-radius:10px;box-shadow:0 10px 35px rgba(0,0,0,.2)">
                <h5><i class="bi bi-arrow-up-circle me-2"></i>Upgrade Your Plan</h5>
                <p>To upgrade your plan, please contact your Organisation Admin.</p>
                ${user.orgAdminEmail ? `<p><a href="mailto:${_escHtml(user.orgAdminEmail)}" class="btn cp-btn-gradient w-100"><i class="bi bi-envelope me-2"></i>Contact Admin</a></p>` : ''}
                <button class="btn btn-outline-secondary w-100 mt-2" id="cpUpgradeCloseBtn">Close</button>
            </div>`;
        document.body.appendChild(modal);
        modal.addEventListener('click', function(e) {
            if (e.target === modal || e.target.id === 'cpUpgradeCloseBtn') modal.remove();
        });
    }

    function _escHtml(str) {
        var d = document.createElement('div');
        d.appendChild(document.createTextNode(str || ''));
        return d.innerHTML;
    }

    async function checkTokenBudget(orgId) {
        if (!orgId) return;
        try {
            const res = await fetch(`/api/org/token-usage?organizationId=${orgId}`);
            if (!res.ok) return;
            const status = await res.json();
            if (status.isExceeded) {
                disableAiInsightsFeatures(status);
            }
        } catch(e) { /* silent */ }
    }

    function disableAiInsightsFeatures(status) {
        // Disable chat input
        const chatInput = document.querySelector('#chat-input, .chat-input, [data-chat-input], #chatInput');
        if (chatInput) {
            chatInput.disabled = true;
            chatInput.placeholder = 'AI token budget exceeded. Contact your organisation admin.';
        }
        // Disable AI send buttons
        document.querySelectorAll('[data-ai-action], .btn-ai, #btn-send-chat, .btn-send, #chatSendBtn').forEach(function(btn) {
            btn.disabled = true;
            btn.title = 'AI token budget exceeded';
        });
        // Show banner
        const banner = document.createElement('div');
        banner.className = 'alert alert-warning alert-dismissible d-flex align-items-center gap-2 mb-3';
        banner.setAttribute('role', 'alert');
        banner.innerHTML = `
            <i class="bi bi-exclamation-triangle-fill"></i>
            <div><strong>AI Token Budget Exceeded.</strong> Your organisation has used all available AI tokens for this month. Contact your admin to upgrade or wait until next month.</div>
            <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
        `;
        const main = document.querySelector('main, .main-content, #main-content, .workspace-content');
        if (main) main.prepend(banner);
    }

    async function loadWorkspace(workspaceGuid) {
        const res = await fetch(`/api/workspaces/${workspaceGuid}`);
        if (res.status === 403) {
            const body = await res.json().catch(() => ({}));
            showAccessDenied(body.error || 'You do not have access to this workspace.');
            return null;
        }
        if (!res.ok) return null;
        return await res.json();
    }

    function showAccessDenied(msg) {
        document.body.innerHTML = `
            <div class="d-flex flex-column align-items-center justify-content-center min-vh-100 text-center p-4">
                <i class="bi bi-shield-lock display-1 text-danger mb-3"></i>
                <h2 class="fw-bold">Access Denied</h2>
                <p class="text-muted">${msg}</p>
                <a href="/dashboard" class="btn btn-primary mt-3"><i class="bi bi-arrow-left me-2"></i>Back to Dashboard</a>
            </div>`;
    }

    // Expose helpers globally for use by other scripts
    window.checkTokenBudget = checkTokenBudget;
    window.disableAiInsightsFeatures = disableAiInsightsFeatures;
    window.loadWorkspace = loadWorkspace;
    window.showAccessDenied = showAccessDenied;

    document.addEventListener('DOMContentLoaded', function() {
        updateNavAuth();
        loadPlan();
        loadWorkspaces();
        wireUpgradeButtons();

        // Check token budget for current user's org
        const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
        if (user && user.organizationId) {
            checkTokenBudget(user.organizationId);
        }

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
