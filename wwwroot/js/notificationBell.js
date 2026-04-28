// Notification bell dropdown — polls /api/notifications/unread-count every 30s,
// renders dropdown list on click, supports mark-read, mark-all-read, dismiss.
// Hooks into the navbar partial (_NotificationBell.cshtml).
(function () {
    'use strict';

    const wrap = document.getElementById('navNotifyWrap');
    const btn = document.getElementById('navNotifyBtn');
    const badge = document.getElementById('navNotifyBadge');
    const dropdown = document.getElementById('navNotifyDropdown');
    const list = document.getElementById('navNotifyList');
    const markAllBtn = document.getElementById('navNotifyMarkAll');
    if (!wrap || !btn || !dropdown || !list) return;

    // Only show for signed-in users
    let currentUser = null;
    try { currentUser = JSON.parse(localStorage.getItem('cp_user') || 'null'); } catch { }
    if (!currentUser || !currentUser.id) return;

    wrap.style.display = 'inline-flex';

    const POLL_MS = 30000;
    let pollTimer = null;
    let lastCount = 0;
    let loaded = false;

    function escapeHtml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function iconFor(type) {
        switch ((type || '').toLowerCase()) {
            case 'trial': return 'bi-clock-history';
            case 'emailverify': return 'bi-envelope-exclamation';
            case 'warning': return 'bi-exclamation-triangle';
            case 'success': return 'bi-check-circle';
            case 'system': return 'bi-gear';
            case 'announcement': return 'bi-megaphone';
            default: return 'bi-info-circle';
        }
    }

    function timeAgo(iso) {
        if (!iso) return '';
        const d = new Date(iso);
        const s = Math.floor((Date.now() - d.getTime()) / 1000);
        if (s < 60) return 'just now';
        const m = Math.floor(s / 60);
        if (m < 60) return m + 'm ago';
        const h = Math.floor(m / 60);
        if (h < 24) return h + 'h ago';
        const dd = Math.floor(h / 24);
        if (dd < 7) return dd + 'd ago';
        return d.toLocaleDateString();
    }

    function updateBadge(count) {
        lastCount = count;
        if (count > 0) {
            badge.textContent = count > 99 ? '99+' : String(count);
            badge.hidden = false;
            badge.classList.add('show');
        } else {
            badge.hidden = true;
            badge.classList.remove('show');
        }
    }

    async function fetchCount() {
        try {
            const r = await fetch('/api/notifications/unread-count', { credentials: 'same-origin' });
            if (!r.ok) return;
            const d = await r.json();
            updateBadge(d.count || 0);
        } catch { }
    }

    async function fetchList() {
        list.innerHTML = '<div class="nav-notify-loading"><div class="nav-notify-spinner"></div></div>';
        try {
            const r = await fetch('/api/notifications?take=30', { credentials: 'same-origin' });
            if (!r.ok) { list.innerHTML = '<div class="nav-notify-empty"><p>Unable to load</p></div>'; return; }
            const items = await r.json();
            renderList(items);
        } catch {
            list.innerHTML = '<div class="nav-notify-empty"><p>Unable to load</p></div>';
        }
    }

    function renderList(items) {
        if (!items || items.length === 0) {
            list.innerHTML = '<div class="nav-notify-empty"><i class="bi bi-bell-slash"></i><p>No notifications</p></div>';
            // Keep badge in sync — no items means no unread.
            updateBadge(0);
            return;
        }
        // Re-sync the badge with the ACTUAL number of unread items the server
        // returned in the list. Without this, a stale unread-count poll could
        // show "1" while the dropdown clearly displays 2 unread items.
        var unreadInList = items.filter(function (n) { return !n.readAt; }).length;
        updateBadge(unreadInList);
        const html = items.map(n => {
            const unread = !n.readAt;
            const sev = (n.severity || 'normal').toLowerCase();
            const linkAttr = n.link ? `data-link="${escapeHtml(n.link)}"` : '';
            return `
                <div class="nav-notify-item ${unread ? 'unread' : ''} sev-${sev}" data-id="${n.id}" ${linkAttr}>
                    <div class="nav-notify-item-icon"><i class="bi ${iconFor(n.type)}"></i></div>
                    <div class="nav-notify-item-body">
                        <div class="nav-notify-item-title">${escapeHtml(n.title)}</div>
                        <div class="nav-notify-item-text">${escapeHtml(n.body)}</div>
                        <div class="nav-notify-item-meta">${escapeHtml(timeAgo(n.createdAt))}</div>
                    </div>
                    <button type="button" class="nav-notify-item-dismiss" title="Dismiss" aria-label="Dismiss">
                        <i class="bi bi-x"></i>
                    </button>
                </div>`;
        }).join('');
        list.innerHTML = html;
    }

    async function markRead(id) {
        try { await fetch(`/api/notifications/${id}/read`, { method: 'POST', credentials: 'same-origin' }); } catch { }
    }

    async function dismiss(id) {
        try { await fetch(`/api/notifications/${id}/dismiss`, { method: 'POST', credentials: 'same-origin' }); } catch { }
    }

    async function markAll() {
        try {
            await fetch('/api/notifications/read-all', { method: 'POST', credentials: 'same-origin' });
            updateBadge(0);
            list.querySelectorAll('.nav-notify-item.unread').forEach(el => el.classList.remove('unread'));
        } catch { }
    }

    // ── Events ───────────────────────────────────────────────────────────
    function closeDropdown() {
        dropdown.classList.remove('open');
        dropdown.hidden = true;
        btn.setAttribute('aria-expanded', 'false');
    }
    function positionDropdown() {
        const r = btn.getBoundingClientRect();
        dropdown.style.top = (r.bottom + 8) + 'px';
        dropdown.style.right = Math.max(8, window.innerWidth - r.right) + 'px';
        dropdown.style.left = 'auto';
    }
    function openDropdown() {
        dropdown.hidden = false;
        positionDropdown();
        dropdown.classList.add('open');
        btn.setAttribute('aria-expanded', 'true');
        fetchList();
    }
    window.addEventListener('resize', () => { if (dropdown.classList.contains('open')) positionDropdown(); });
    window.addEventListener('scroll', () => { if (dropdown.classList.contains('open')) positionDropdown(); }, true);

    btn.addEventListener('click', (e) => {
        e.stopPropagation();
        if (dropdown.classList.contains('open')) closeDropdown();
        else openDropdown();
    });

    document.addEventListener('click', (e) => {
        if (!wrap.contains(e.target)) closeDropdown();
    });

    list.addEventListener('click', async (e) => {
        const dismissBtn = e.target.closest('.nav-notify-item-dismiss');
        const item = e.target.closest('.nav-notify-item');
        if (!item) return;
        const id = Number(item.dataset.id);

        if (dismissBtn) {
            e.stopPropagation();
            item.style.opacity = '0.4';
            await dismiss(id);
            item.remove();
            fetchCount();
            if (!list.querySelector('.nav-notify-item')) {
                list.innerHTML = '<div class="nav-notify-empty"><i class="bi bi-bell-slash"></i><p>No notifications</p></div>';
            }
            return;
        }

        // Click on item body: mark read + follow link if present
        if (item.classList.contains('unread')) {
            item.classList.remove('unread');
            markRead(id);
            if (lastCount > 0) updateBadge(lastCount - 1);
        }
        const link = item.dataset.link;
        if (link) window.location.href = link;
    });

    markAllBtn?.addEventListener('click', (e) => {
        e.stopPropagation();
        markAll();
    });

    document.getElementById('navNotifyCloseAll')?.addEventListener('click', (e) => {
        e.stopPropagation();
        closeDropdown();
    });

    // Initial + polling
    fetchCount();
    pollTimer = setInterval(fetchCount, POLL_MS);
    window.addEventListener('beforeunload', () => { if (pollTimer) clearInterval(pollTimer); });
})();
