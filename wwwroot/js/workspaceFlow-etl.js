// WorkspaceFlow — ETL extension
// Loaded after workspaceFlow.js. Adds the ETL-specific setup wizard onto
// window.workspaceFlow. Triggered when wsData.type === 'Etl'.
//
// IMPORTANT (design): the wizard does NOT do any transform / dashboard /
// report work during workspace creation. It just creates one or more
// datasources, then drops the user on the workspace home. All transform
// authoring happens later in the dedicated Transform Workbench page
// (/transform/{wsGuid}) — like Power Query — so users can come back, edit
// applied steps, see TOML/DAX, and re-publish to flow into the dashboard.
(function () {
    'use strict';

    if (!window.workspaceFlow) {
        console.warn('workspaceFlow-etl.js loaded before workspaceFlow.js — ETL wizard disabled.');
        return;
    }

    const ETL = {
        _renderEtlSetupWizard(panel, wsData) {
            this._etlState = { wsData, datasources: [] };
            this._setupStep = 1;
            panel.innerHTML = `
                <div class="wf-home-header">
                    <div class="wf-home-icon" style="background:linear-gradient(135deg,#7c3aed,#3b82f6);color:#fff"><i class="bi bi-diagram-3"></i></div>
                    <div class="wf-home-meta">
                        <h2 class="wf-home-title">${this._esc(wsData.name)} <span class="wf-etl-tag">ETL</span></h2>
                        <p class="wf-home-desc">Connect one or more datasources. You'll author transforms (merge, pivot, add column, DAX expressions, …) afterwards in the Transform Workbench.</p>
                    </div>
                </div>
                <div class="wf-steps" id="wfSteps">
                    <div class="wf-step active" data-step="1"><span class="wf-step-num">1</span>Datasources</div>
                    <div class="wf-step-line"></div>
                    <div class="wf-step" data-step="2"><span class="wf-step-num">2</span>Finish</div>
                </div>
                <div id="wfSetupContent"></div>
            `;
            this._updateSteps();
            this._renderEtlDatasourceList();
        },

        _renderEtlDatasourceList() {
            const content = document.getElementById('wfSetupContent');
            if (!content) return;
            const dsList = this._etlState.datasources;
            const items = dsList.length === 0
                ? `<div class="wf-etl-empty">No datasources yet. Add at least one to continue.</div>`
                : dsList.map((d, i) => `
                    <div class="wf-etl-ds-item">
                        <div class="wf-etl-ds-icon"><i class="bi bi-database"></i></div>
                        <div class="wf-etl-ds-meta">
                            <div class="wf-etl-ds-name">${this._esc(d.name)}</div>
                            <div class="wf-etl-ds-type">${this._esc(d.type)}</div>
                        </div>
                        <button class="btn btn-sm btn-outline-danger" data-etl-remove="${i}" title="Remove"><i class="bi bi-x-lg"></i></button>
                    </div>`).join('');
            content.innerHTML = `
                <div class="wf-setup-section">
                    <div class="wf-setup-section-head">
                        <div class="wf-setup-section-icon" style="background:var(--cp-primary-light);color:var(--cp-primary)"><i class="bi bi-database"></i></div>
                        <div>
                            <div class="wf-setup-section-title">Step 1 — Datasources</div>
                            <div class="wf-setup-section-desc">Add one or more datasources. You can add more later from the workspace home.</div>
                        </div>
                    </div>
                    <div class="wf-etl-ds-list">${items}</div>
                    <div class="wf-setup-actions">
                        <button class="btn btn-outline-primary btn-sm" id="wfEtlAddDsBtn"><i class="bi bi-plus-lg me-1"></i>Add Datasource</button>
                        <button class="btn cp-btn-gradient btn-sm" id="wfEtlFinishBtn" ${dsList.length === 0 ? 'disabled' : ''}>
                            <i class="bi bi-check-lg me-1"></i>Finish — Open Workspace
                        </button>
                    </div>
                    <div class="wf-etl-hint">
                        <i class="bi bi-info-circle me-1"></i>
                        Once you finish, head into <strong>Transform Workbench</strong> from the workspace home to define the pipeline (merge, pivot, append, add column, DAX expressions, …). Your changes will flow through to the dashboard.
                    </div>
                </div>
            `;
            const addBtn = document.getElementById('wfEtlAddDsBtn');
            if (addBtn) addBtn.addEventListener('click', () => this._renderEtlAddDatasource());
            const finishBtn = document.getElementById('wfEtlFinishBtn');
            if (finishBtn) finishBtn.addEventListener('click', async () => {
                this._setupStep = 2; this._updateSteps();
                this._wizardCompleted = true;
                await this._selectWorkspace(this._etlState.wsData.guid);
            });
            content.querySelectorAll('[data-etl-remove]').forEach(btn => {
                btn.addEventListener('click', () => {
                    const idx = parseInt(btn.dataset.etlRemove, 10);
                    this._etlState.datasources.splice(idx, 1);
                    this._renderEtlDatasourceList();
                });
            });
        },

        _renderEtlAddDatasource() {
            const wsData = this._etlState.wsData;
            // Reuse the standard datasource form; intercept Save via _onDsSaved.
            this._renderDatasourceSetup(wsData, { name: wsData.name });

            // Adapt headings & button labels to ETL "add to list" context.
            const title = document.querySelector('#wfSetupContent .wf-setup-section-title');
            if (title) title.textContent = 'Add Datasource';
            const desc = document.querySelector('#wfSetupContent .wf-setup-section-desc');
            if (desc) desc.textContent = 'Connect a source. You can add more after this one.';
            const saveBtn = document.getElementById('wfDsSaveBtn');
            if (saveBtn) saveBtn.innerHTML = '<i class="bi bi-check-lg me-1"></i>Add & Continue';

            const actions = document.querySelector('#wfSetupContent .wf-setup-actions');
            if (actions) {
                const cancel = document.createElement('button');
                cancel.className = 'btn btn-outline-secondary btn-sm';
                cancel.innerHTML = '<i class="bi bi-arrow-left me-1"></i>Back to list';
                cancel.addEventListener('click', () => { this._onDsSaved = null; this._renderEtlDatasourceList(); });
                actions.insertBefore(cancel, actions.firstChild);
            }

            this._onDsSaved = (ds) => {
                if (ds) this._etlState.datasources.push(ds);
                this._renderEtlDatasourceList();
            };
        }
    };

    Object.assign(window.workspaceFlow, ETL);
})();
