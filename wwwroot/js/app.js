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

    // Apply nav auth UI state (no network calls). Safe to call multiple times.
    function applyNavAuthUi(user) {
        const authBtn = document.getElementById('navAuthBtn');
        const dashboardBtn = document.getElementById('navDashboardBtn');
        const userMenu = document.getElementById('navUserMenu');
        const userMenuBtn = document.getElementById('navUserMenuBtn');
        const userMenuDropdown = document.getElementById('navUserMenuDropdown');
        const userNameEl = document.getElementById('navUserName');
        const menuSettings = document.getElementById('navUserMenuSettings');
        const menuActivity = document.getElementById('navUserMenuActivity');
        const logoutBtn = document.getElementById('navLogoutBtn');

        if (user) {
            if (authBtn) authBtn.style.display = 'none';
            if (dashboardBtn) dashboardBtn.style.display = '';
            if (userMenu) userMenu.style.display = '';
            if (userNameEl) userNameEl.textContent = user.fullName || user.email || 'User';
            const isAdminRole = user.role === 'OrgAdmin' || user.role === 'SuperAdmin';
            if (menuSettings) menuSettings.style.display = isAdminRole ? '' : 'none';
            if (menuActivity) menuActivity.style.display = isAdminRole ? '' : 'none';

            // Sidebar links — apply synchronously from cached user to avoid blink
            const sidebarActivity = document.getElementById('activityLink');
            const sidebarSettings = document.getElementById('orgSettingsLink');
            const sidebarSuperAdmin = document.getElementById('superAdminLink');
            if (sidebarActivity) sidebarActivity.style.display = isAdminRole ? '' : 'none';
            if (sidebarSettings) sidebarSettings.style.display = isAdminRole ? '' : 'none';
            if (sidebarSuperAdmin) sidebarSuperAdmin.style.display = user.role === 'SuperAdmin' ? '' : 'none';

            // Sidebar org name
            const orgNameEl = document.getElementById('orgName');
            if (orgNameEl) {
                const displayName = user.orgName || user.fullName || 'My Organization';
                orgNameEl.textContent = displayName;
                orgNameEl.title = displayName;
            }

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

            if (userMenuBtn && userMenuDropdown && !userMenuBtn._menuWired) {
                userMenuBtn._menuWired = true;
                const closeMenu = () => {
                    userMenuDropdown.hidden = true;
                    userMenu.classList.remove('open');
                    userMenuBtn.setAttribute('aria-expanded', 'false');
                };
                const openMenu = () => {
                    userMenuDropdown.hidden = false;
                    userMenu.classList.add('open');
                    userMenuBtn.setAttribute('aria-expanded', 'true');
                };
                userMenuBtn.addEventListener('click', (e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    if (userMenu.classList.contains('open')) closeMenu(); else openMenu();
                });
                document.addEventListener('click', (e) => {
                    if (!userMenu.contains(e.target)) closeMenu();
                });
                document.addEventListener('keydown', (e) => {
                    if (e.key === 'Escape') closeMenu();
                });
            }
        } else {
            if (authBtn) authBtn.style.display = '';
            if (userMenu) userMenu.style.display = 'none';
            if (dashboardBtn) dashboardBtn.style.display = 'none';
        }
    }

    // Update nav auth button based on login state
    async function updateNavAuth() {
        let user = JSON.parse(localStorage.getItem('cp_user') || 'null');
        const hasToken = !!localStorage.getItem('cp_token');

        // Apply cached state synchronously to eliminate flicker
        applyNavAuthUi(hasToken ? user : null);

        // Verify auth state with server (only if we have a token)
        if (hasToken) {
            try {
                const r = await fetch('/api/auth/me');
                if (r.ok) {
                    user = await r.json();
                    localStorage.setItem('cp_user', JSON.stringify(user));
                    applyNavAuthUi(user);
                } else {
                    // Server says not authenticated — clear stale local data
                    localStorage.removeItem('cp_user');
                    localStorage.removeItem('cp_token');
                    user = null;
                    applyNavAuthUi(null);
                }
            } catch (e) {
                console.error('Auth verification failed:', e);
            }
        }

        // Nothing else to do — sidebar links and org name are applied via applyNavAuthUi.
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

    // Render workspace list items into #workspaceList. Preserves active state
    // by matching against the current active data-workspace-id if present.
    function _renderWorkspaceList(workspaces) {
        const list = document.getElementById('workspaceList');
        if (!list || !workspaces || !workspaces.length) return;
        const prevActive = list.querySelector('.panel-list-item.active');
        const activeId = prevActive ? prevActive.getAttribute('data-workspace-id') : null;
        list.innerHTML = workspaces.map(w =>
            `<div class="panel-list-item${activeId && activeId === w.guid ? ' active' : ''}" data-workspace-id="${w.guid}">
                ${w.logoUrl
                    ? `<img src="${_escHtml(w.logoUrl)}" alt="" style="width:18px;height:18px;border-radius:3px;object-fit:cover;margin-right:8px;">`
                    : '<i class="bi bi-folder me-2"></i>'}${_escHtml(w.name)}<span class="wf-ws-status unconfigured" title="Needs setup"></span>
             </div>`
        ).join('');
        list.querySelectorAll('.panel-list-item').forEach(function (el, i) {
            el.title = workspaces[i]?.description || '';
        });
    }

    // Hydrate workspace list synchronously from localStorage cache (stale-while-revalidate).
    // Call this as early as possible on page load so names appear instantly.
    function hydrateWorkspacesFromCache() {
        try {
            const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
            if (!user || !user.organizationId) return;
            const key = 'cp_workspaces_' + user.organizationId;
            const cached = localStorage.getItem(key);
            if (!cached) return;
            const workspaces = JSON.parse(cached);
            if (Array.isArray(workspaces) && workspaces.length) {
                _renderWorkspaceList(workspaces);
            }
        } catch {}
    }

    // Load workspaces into left panel
    async function loadWorkspaces() {
        const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
        if (!user || !user.organizationId) return;
        try {
            const r = await fetch('/api/workspaces?organizationId=' + user.organizationId);
            const workspaces = await r.json();
            if (workspaces.length) {
                _renderWorkspaceList(workspaces);
                // Cache a minimal snapshot for instant hydration on the next visit
                try {
                    const slim = workspaces.map(w => ({
                        guid: w.guid,
                        name: w.name,
                        logoUrl: w.logoUrl,
                        description: w.description
                    }));
                    localStorage.setItem('cp_workspaces_' + user.organizationId, JSON.stringify(slim));
                } catch {}
            }
        } catch {}

        // Load shared reports
        try {
            const sr = await fetch('/api/reports/shared');
            if (sr.ok) {
                const shared = await sr.json();
                const section = document.getElementById('sharedWithMeSection');
                const sharedList = document.getElementById('sharedReportList');
                if (section && sharedList && shared.length > 0) {
                    section.style.display = '';
                    sharedList.innerHTML = shared.map(r =>
                        `<a class="panel-list-item" href="/report/view/${_escHtml(r.guid)}" style="text-decoration:none;color:inherit;">
                            <i class="bi bi-file-earmark-bar-graph me-2"></i>${_escHtml(r.name)}
                            ${r.workspaceName ? `<small class="text-muted ms-auto" style="font-size:0.7rem">${_escHtml(r.workspaceName)}</small>` : ''}
                        </a>`
                    ).join('');
                }
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
                    window.location.href = '/org/settings?tab=users';
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

    // Shared clipboard helper — exposed globally so other scripts (e.g. cp-about.js) can reuse it
    function copyGuidToClipboard(text, btn) {
        if (!navigator.clipboard) return;
        navigator.clipboard.writeText(text).then(function () {
            var icon = btn && btn.querySelector('i');
            if (icon) {
                icon.classList.replace('bi-clipboard', 'bi-check');
                setTimeout(function () { icon.classList.replace('bi-check', 'bi-clipboard'); }, 1500);
            }
        });
    }
    window.copyGuidToClipboard = copyGuidToClipboard;

    // Load and display the organization GUID in the nav dropdown
    async function loadOrgGuid() {
        const user = JSON.parse(localStorage.getItem('cp_user') || 'null');
        if (!user || !user.organizationId) return;
        try {
            const r = await fetch('/api/org/about', { credentials: 'same-origin' });
            if (!r.ok) return;
            const data = await r.json();
            const guidVal = data.organizationGuid ? String(data.organizationGuid) : null;
            if (!guidVal) return;
            const navRow = document.getElementById('navOrgGuidRow');
            const navCode = document.getElementById('navOrgGuid');
            const navCopy = document.getElementById('navOrgGuidCopy');
            if (navRow && navCode) {
                navCode.textContent = guidVal;
                navRow.classList.remove('d-none');
            }
            if (navCopy) {
                navCopy.addEventListener('click', function () {
                    copyGuidToClipboard(guidVal, navCopy);
                });
            }
        } catch { /* non-fatal */ }
    }

    // Expose helpers globally for use by other scripts
    window.checkTokenBudget = checkTokenBudget;
    window.disableAiInsightsFeatures = disableAiInsightsFeatures;
    window.loadWorkspace = loadWorkspace;
    window.showAccessDenied = showAccessDenied;

    document.addEventListener('DOMContentLoaded', function() {
        // Paint cached state first (synchronously) to avoid flicker on names + sidebar links
        hydrateWorkspacesFromCache();
        updateNavAuth();
        loadPlan();
        loadWorkspaces();
        loadOrgGuid();
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
