// Shared HTML escape utility (loaded first, used by all modules)
function escapeHtml(str) {
    const div = document.createElement('div');
    div.appendChild(document.createTextNode(String(str ?? '')));
    return div.innerHTML;
}

// Chart type library manager - handles the left panel
class ChartLibrary {
    constructor() {
        this.charts = [];
        this.filteredCharts = [];
        this.searchTerm = '';
    }

    init(chartGroups) {
        this.charts = chartGroups;
        this.filteredCharts = chartGroups;
        this.render();
        this.bindSearch();
    }

    bindSearch() {
        const searchInput = document.getElementById('chart-search');
        if (searchInput) {
            searchInput.addEventListener('input', (e) => {
                this.searchTerm = e.target.value.toLowerCase();
                this.filter();
            });
        }
    }

    filter() {
        if (!this.searchTerm) {
            this.filteredCharts = this.charts;
        } else {
            this.filteredCharts = this.charts.map(group => ({
                ...group,
                charts: group.charts.filter(c =>
                    c.name.toLowerCase().includes(this.searchTerm) ||
                    c.description.toLowerCase().includes(this.searchTerm)
                )
            })).filter(g => g.charts.length > 0);
        }
        this.render();
    }

    render() {
        const container = document.getElementById('chart-library-container');
        if (!container) return;

        container.innerHTML = '';
        this.filteredCharts.forEach((group, idx) => {
            if (!group.charts || group.charts.length === 0) return;
            const groupEl = document.createElement('div');
            groupEl.className = 'library-group mb-2';
            groupEl.innerHTML = `
                <div class="library-group-header" data-bs-toggle="collapse" data-bs-target="#group-${idx}">
                    <span class="group-name">${group.group}</span>
                    <span class="badge bg-secondary">${group.charts.length}</span>
                    <i class="bi bi-chevron-down ms-auto"></i>
                </div>
                <div class="collapse show" id="group-${idx}">
                    <div class="library-items">
                        ${group.charts.map(c => `
                            <div class="library-item"
                                 data-chart-type="${c.id}"
                                 data-chart-name="${c.name}"
                                 data-chartjs-type="${c.chartJsType}"
                                 draggable="true"
                                 title="${c.description}">
                                <i class="bi ${c.icon}"></i>
                                <span>${c.name}</span>
                            </div>
                        `).join('')}
                    </div>
                </div>
            `;
            container.appendChild(groupEl);
        });

        this.bindDragEvents();
        this.bindClickEvents();
    }

    bindDragEvents() {
        document.querySelectorAll('.library-item').forEach(item => {
            item.addEventListener('dragstart', (e) => {
                e.dataTransfer.setData('chartType', item.dataset.chartType);
                e.dataTransfer.setData('chartName', item.dataset.chartName);
                e.dataTransfer.setData('chartJsType', item.dataset.chartjsType);
                item.classList.add('dragging');
                const drop = document.getElementById('chart-canvas-drop');
                if (drop) drop.classList.add('drop-active');
            });
            item.addEventListener('dragend', () => {
                item.classList.remove('dragging');
                const drop = document.getElementById('chart-canvas-drop');
                if (drop) drop.classList.remove('drop-active');
            });
        });
    }

    bindClickEvents() {
        document.querySelectorAll('.library-item').forEach(item => {
            item.addEventListener('dblclick', () => {
                if (window.canvasManager) {
                    window.canvasManager.addChart({
                        chartType: item.dataset.chartType,
                        title: item.dataset.chartName,
                    });
                }
            });
        });
    }
}

window.chartLibrary = new ChartLibrary();
