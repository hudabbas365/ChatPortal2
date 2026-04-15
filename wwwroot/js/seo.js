// AIInsights - SEO Management JS
const seoManager = {
    modal: null,
    previewModal: null,

    init() {
        this.modal = new bootstrap.Modal(document.getElementById('seoModal'));
        this.previewModal = new bootstrap.Modal(document.getElementById('previewModal'));
    },

    async loadEntries() {
        try {
            const r = await fetch('/api/seo');
            const entries = await r.json();
            this.renderTable(entries);
        } catch { this.showAlert('Failed to load SEO entries.', 'danger'); }
    },

    renderTable(entries) {
        const tbody = document.getElementById('seoTableBody');
        if (!tbody) return;
        if (!entries.length) {
            tbody.innerHTML = '<tr><td colspan="8" class="text-center text-muted py-4">No SEO entries. Click "Seed Defaults" to add default entries.</td></tr>';
            return;
        }
        tbody.innerHTML = entries.map(e => {
            const score = this.calculateSeoScore(e);
            const scoreClass = score >= 70 ? 'text-success' : score >= 40 ? 'text-warning' : 'text-danger';
            return `<tr>
                <td><code>${e.pageUrl}</code></td>
                <td class="text-truncate" style="max-width:150px" title="${e.title}">${e.title || '<em class="text-muted">—</em>'}</td>
                <td class="text-truncate" style="max-width:200px" title="${e.metaDescription}">${e.metaDescription || '<em class="text-muted">—</em>'}</td>
                <td>${e.sitemapPriority}</td>
                <td>${e.sitemapChangeFreq}</td>
                <td>
                    <div class="form-check form-switch mb-0">
                        <input class="form-check-input" type="checkbox" ${e.includeInSitemap ? 'checked' : ''}
                               onchange="seoManager.toggleSitemap(${e.id}, this.checked)" />
                    </div>
                </td>
                <td><span class="${scoreClass} fw-bold">${score}/100</span></td>
                <td>
                    <button class="btn btn-xs btn-outline-primary me-1" onclick="seoManager.openEditModal(${e.id})">
                        <i class="bi bi-pencil"></i>
                    </button>
                    <button class="btn btn-xs btn-outline-danger" onclick="seoManager.deleteEntry(${e.id})">
                        <i class="bi bi-trash"></i>
                    </button>
                </td>
            </tr>`;
        }).join('');
    },

    openAddModal() {
        document.getElementById('seoModalTitle').textContent = 'Add SEO Entry';
        document.getElementById('seoId').value = '0';
        ['seoPageUrl','seoTitle','seoKeywords','seoMetaDesc','seoOgTitle','seoOgImage','seoOgDesc','seoCanonical','seoStructuredData'].forEach(id => {
            const el = document.getElementById(id);
            if (el) el.value = '';
        });
        document.getElementById('seoRobots').value = 'index, follow';
        document.getElementById('seoPriority').value = '0.5';
        document.getElementById('seoPriorityVal').textContent = '0.5';
        document.getElementById('seoChangeFreq').value = 'weekly';
        document.getElementById('seoIncludeSitemap').checked = true;
        this.updateScore();
        if (!this.modal) this.init();
        this.modal.show();
    },

    async openEditModal(id) {
        try {
            const r = await fetch(`/api/seo/${id}`);
            const e = await r.json();
            document.getElementById('seoModalTitle').textContent = 'Edit SEO Entry';
            document.getElementById('seoId').value = e.id;
            document.getElementById('seoPageUrl').value = e.pageUrl || '';
            document.getElementById('seoTitle').value = e.title || '';
            document.getElementById('seoKeywords').value = e.metaKeywords || '';
            document.getElementById('seoMetaDesc').value = e.metaDescription || '';
            document.getElementById('seoOgTitle').value = e.ogTitle || '';
            document.getElementById('seoOgImage').value = e.ogImage || '';
            document.getElementById('seoOgDesc').value = e.ogDescription || '';
            document.getElementById('seoCanonical').value = e.canonicalUrl || '';
            document.getElementById('seoRobots').value = e.robotsDirective || 'index, follow';
            document.getElementById('seoPriority').value = e.sitemapPriority || 0.5;
            document.getElementById('seoPriorityVal').textContent = e.sitemapPriority || 0.5;
            document.getElementById('seoChangeFreq').value = e.sitemapChangeFreq || 'weekly';
            document.getElementById('seoIncludeSitemap').checked = e.includeInSitemap;
            document.getElementById('seoStructuredData').value = e.structuredData || '';
            this.updateScore();
            if (!this.modal) this.init();
            this.modal.show();
        } catch { this.showAlert('Failed to load entry.', 'danger'); }
    },

    async saveEntry() {
        const id = parseInt(document.getElementById('seoId').value);
        const entry = {
            id,
            pageUrl: document.getElementById('seoPageUrl').value,
            title: document.getElementById('seoTitle').value,
            metaKeywords: document.getElementById('seoKeywords').value,
            metaDescription: document.getElementById('seoMetaDesc').value,
            ogTitle: document.getElementById('seoOgTitle').value,
            ogImage: document.getElementById('seoOgImage').value,
            ogDescription: document.getElementById('seoOgDesc').value,
            canonicalUrl: document.getElementById('seoCanonical').value,
            robotsDirective: document.getElementById('seoRobots').value,
            sitemapPriority: parseFloat(document.getElementById('seoPriority').value),
            sitemapChangeFreq: document.getElementById('seoChangeFreq').value,
            includeInSitemap: document.getElementById('seoIncludeSitemap').checked,
            structuredData: document.getElementById('seoStructuredData').value
        };
        try {
            const method = id === 0 ? 'POST' : 'PUT';
            const url = id === 0 ? '/api/seo' : `/api/seo/${id}`;
            const r = await fetch(url, { method, headers: {'Content-Type':'application/json'}, body: JSON.stringify(entry) });
            if (!r.ok) throw new Error('Save failed');
            this.modal.hide();
            this.loadEntries();
            this.showAlert('SEO entry saved successfully.', 'success');
        } catch { this.showAlert('Failed to save entry.', 'danger'); }
    },

    async deleteEntry(id) {
        if (!confirm('Delete this SEO entry?')) return;
        try {
            await fetch(`/api/seo/${id}`, { method: 'DELETE' });
            this.loadEntries();
        } catch { this.showAlert('Failed to delete entry.', 'danger'); }
    },

    async toggleSitemap(id, include) {
        try {
            const r = await fetch(`/api/seo/${id}`);
            const e = await r.json();
            e.includeInSitemap = include;
            await fetch(`/api/seo/${id}`, { method: 'PUT', headers: {'Content-Type':'application/json'}, body: JSON.stringify(e) });
        } catch {}
    },

    async previewSitemap() {
        try {
            const r = await fetch('/api/seo/preview-sitemap');
            const xml = await r.text();
            document.getElementById('previewModalTitle').textContent = 'Sitemap Preview';
            document.getElementById('previewContent').textContent = xml;
            if (!this.previewModal) this.init();
            this.previewModal.show();
        } catch { this.showAlert('Failed to load sitemap preview.', 'danger'); }
    },

    async previewRobots() {
        try {
            const r = await fetch('/robots.txt');
            const txt = await r.text();
            document.getElementById('previewModalTitle').textContent = 'robots.txt Preview';
            document.getElementById('previewContent').textContent = txt;
            if (!this.previewModal) this.init();
            this.previewModal.show();
        } catch { this.showAlert('Failed to load robots.txt.', 'danger'); }
    },

    async seedDefaults() {
        try {
            await fetch('/api/seo/seed', { method: 'POST' });
            this.loadEntries();
            this.showAlert('Default SEO entries seeded.', 'success');
        } catch { this.showAlert('Failed to seed defaults.', 'danger'); }
    },

    calculateSeoScore(entry) {
        let score = 0;
        const t = entry.title || '';
        const d = entry.metaDescription || '';
        if (t.length >= 10 && t.length <= 60) score += 20;
        else if (t.length > 0) score += 10;
        if (d.length >= 50 && d.length <= 160) score += 20;
        else if (d.length > 0) score += 10;
        if (entry.metaKeywords) score += 10;
        if (entry.ogTitle) score += 10;
        if (entry.ogDescription) score += 10;
        if (entry.ogImage) score += 10;
        if (entry.canonicalUrl) score += 10;
        if (entry.structuredData) score += 10;
        return Math.min(100, score);
    },

    updateScore() {
        const entry = {
            title: document.getElementById('seoTitle')?.value || '',
            metaDescription: document.getElementById('seoMetaDesc')?.value || '',
            metaKeywords: document.getElementById('seoKeywords')?.value || '',
            ogTitle: document.getElementById('seoOgTitle')?.value || '',
            ogDescription: document.getElementById('seoOgDesc')?.value || '',
            ogImage: document.getElementById('seoOgImage')?.value || '',
            canonicalUrl: document.getElementById('seoCanonical')?.value || '',
            structuredData: document.getElementById('seoStructuredData')?.value || ''
        };
        const score = this.calculateSeoScore(entry);
        const fill = document.getElementById('seoScoreFill');
        const text = document.getElementById('seoScoreText');
        if (fill) { fill.style.width = score + '%'; fill.style.background = score >= 70 ? '#48BB78' : score >= 40 ? '#ECC94B' : '#FC8181'; }
        if (text) text.textContent = score + '/100';
    },

    showAlert(msg, type) {
        const el = document.getElementById('seoAlert');
        if (!el) return;
        el.className = `alert alert-${type}`;
        el.textContent = msg;
        setTimeout(() => el.className = 'alert d-none', 4000);
    }
};
