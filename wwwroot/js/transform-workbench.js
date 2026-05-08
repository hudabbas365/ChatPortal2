(function(){
  'use strict';

  const PAGE_SIZE = 1000;

  function esc(v){ return String(v ?? '').replace(/[&<>"']/g, s => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[s])); }
  function token(){ const m = document.cookie.match(/(?:^|; )XSRF-TOKEN=([^;]+)/); return m ? decodeURIComponent(m[1]) : ''; }

  function buildToml(steps){
    const lines = [
      '[transform]',
      'name = "AI Insights 365 ETL"',
      'enabled = true',
      'dax_like_expressions = true',
      ''
    ];
    (steps || []).forEach(s => {
      lines.push('[[rules]]');
      lines.push(`type = "${s.type}"`);
      lines.push(`enabled = ${s.enabled !== false ? 'true':'false'}`);
      if (s.params){
        Object.keys(s.params).forEach(k => {
          const val = s.params[k];
          if (Array.isArray(val)) lines.push(`${k} = [${val.map(x=>`"${String(x).replace(/\\/g,'\\\\').replace(/"/g,'\\"')}"`).join(', ')}]`);
          else if (typeof val === 'boolean') lines.push(`${k} = ${val ? 'true':'false'}`);
          else if (val != null && val !== '') lines.push(`${k} = "${String(val).replace(/\\/g,'\\\\').replace(/"/g,'\\"')}"`);
        });
      }
      lines.push('');
    });
    return lines.join('\n');
  }

  async function fetchJson(url, opts){
    const r = await fetch(url, opts);
    const j = await r.json().catch(()=>({}));
    if(!r.ok) throw new Error(j.error || 'Request failed');
    return j;
  }

  const api = {
    async open(ds){
      let modal = document.getElementById('transformWorkbenchModal');
      if (modal) modal.remove();
      modal = document.createElement('div');
      modal.id = 'transformWorkbenchModal';
      modal.className = 'modal fade twb-modal';
      modal.tabIndex = -1;
      modal.innerHTML = `
        <div class="modal-dialog modal-xl modal-dialog-centered">
          <div class="modal-content">
            <div class="modal-header">
              <h5 class="modal-title"><i class="bi bi-magic me-2"></i>AI Insights 365 ETL Workbench</h5>
              <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
            </div>
            <div class="modal-body">
              <div class="twb-shell">
                <div class="twb-left">
                  <div class="fw-semibold mb-2">Datasources</div>
                  <div id="twbDatasourceList"></div>
                  <hr/>
                  <div class="fw-semibold mb-2">AI Assistant</div>
                  <div class="twb-ai">
                    <div id="twbAiHint" class="mb-2">Select or add a step to get AI guidance.</div>
                    <button class="btn btn-sm btn-outline-primary w-100" id="twbAiSuggestBtn"><i class="bi bi-stars me-1"></i>Suggest next step</button>
                  </div>
                </div>
                <div class="twb-center">
                  <div class="twb-ribbon" id="twbRibbon"></div>
                  <div class="fw-semibold mb-1">Preview</div>
                  <div class="twb-preview-wrap">
                    <table class="twb-preview" id="twbPreviewTable"></table>
                  </div>
                  <div class="twb-pager">
                    <div>
                      <button class="btn btn-sm btn-outline-secondary" id="twbPrev">Prev</button>
                      <button class="btn btn-sm btn-outline-secondary" id="twbNext">Next</button>
                      <span id="twbPageLabel" class="small ms-2">Page 1</span>
                    </div>
                    <div>
                      <button class="btn btn-sm btn-outline-primary" id="twbPreviewBtn">Preview</button>
                      <button class="btn btn-sm btn-outline-success" id="twbValidateBtn">Validate</button>
                    </div>
                  </div>
                  <hr/>
                  <div class="d-flex justify-content-between align-items-center mb-1">
                    <div class="fw-semibold">TOML Editor</div>
                    <div>
                      <button class="btn btn-sm btn-outline-secondary" id="twbSaveDraftBtn">Save Draft</button>
                      <button class="btn btn-sm btn-primary" id="twbPublishBtn">Publish</button>
                    </div>
                  </div>
                  <textarea class="form-control twb-editor" id="twbToml"></textarea>
                </div>
                <div class="twb-right">
                  <div class="fw-semibold mb-2">Applied Steps</div>
                  <ul class="twb-steps" id="twbSteps"></ul>
                </div>
              </div>
              <div class="twb-statusbar" id="twbStatusbar">
                <span>Render: <strong id="twbStatusRender">-</strong></span>
                <span>Rows: <strong id="twbStatusRows">0</strong></span>
                <span>Validation: <strong id="twbStatusValidation">-</strong></span>
                <span>Preview: <strong id="twbStatusPreview">-</strong></span>
                <span>Save/Publish: <strong id="twbStatusSave">-</strong></span>
              </div>
            </div>
          </div>
        </div>`;
      document.body.appendChild(modal);

      const state = {
        ds,
        dsGuid: ds.guid,
        page: 1,
        pageSize: PAGE_SIZE,
        selectedDatasourceGuids: [ds.guid],
        steps: [],
        toml: ds.transformToml || '',
        draftGuid: null,
        preview: null,
        handlers: []
      };

      const bs = new bootstrap.Modal(modal);
      modal.addEventListener('hidden.bs.modal', ()=> modal.remove());
      wire(modal, state);
      await loadWorkbenchState(modal, state);
      bs.show();
    }
  };

  function buildRibbonGroups(handlers){
    const groups = {};
    (handlers || []).forEach(h => {
      if (!groups[h.group]) groups[h.group] = [];
      groups[h.group].push(h.type);
    });
    return groups;
  }

  function renderRibbon(modal, state){
    const root = modal.querySelector('#twbRibbon');
    const ribbonGroups = buildRibbonGroups(state.handlers);
    const buttons = [];
    Object.keys(ribbonGroups).forEach(group => {
      ribbonGroups[group].forEach(fn => buttons.push(`<button class="btn btn-sm btn-outline-secondary" data-add-step="${esc(fn)}" title="${esc(group)}">${esc(fn)}</button>`));
    });
    root.innerHTML = buttons.join('');
    root.querySelectorAll('[data-add-step]').forEach(btn => {
      btn.addEventListener('click', ()=> {
        state.steps.push({ id: crypto.randomUUID(), type: btn.dataset.addStep, enabled: true, params:{} });
        state.toml = buildToml(state.steps);
        modal.querySelector('#twbToml').value = state.toml;
        renderSteps(modal, state);
      });
    });
  }

  function renderDatasourceList(modal, state, datasources){
    const root = modal.querySelector('#twbDatasourceList');
    root.innerHTML = (datasources || []).map(d =>
      `<label class="twb-ds-item"><input type="checkbox" value="${esc(d.guid)}" ${state.selectedDatasourceGuids.includes(d.guid) ? 'checked':''}/> <span>${esc(d.name)}</span> <small class="text-muted">${esc(d.type)}</small></label>`
    ).join('');
    root.querySelectorAll('input[type="checkbox"]').forEach(cb => {
      cb.addEventListener('change', ()=> {
        const selected = Array.from(root.querySelectorAll('input:checked')).map(x => x.value);
        state.selectedDatasourceGuids = selected.length ? selected : [state.dsGuid];
      });
    });
  }

  function renderSteps(modal, state){
    const root = modal.querySelector('#twbSteps');
    root.innerHTML = state.steps.map((s, idx)=>`<li class="twb-step">
      <span>${idx+1}. ${esc(s.type)}</span>
      <span>
        <button class="btn btn-xs btn-light" data-up="${s.id}"><i class="bi bi-arrow-up"></i></button>
        <button class="btn btn-xs btn-light" data-down="${s.id}"><i class="bi bi-arrow-down"></i></button>
        <button class="btn btn-xs btn-light" data-toggle="${s.id}">${s.enabled !== false ? 'On':'Off'}</button>
        <button class="btn btn-xs btn-danger" data-remove="${s.id}"><i class="bi bi-x"></i></button>
      </span>
    </li>`).join('');

    root.querySelectorAll('[data-remove]').forEach(btn=> btn.addEventListener('click', ()=> {
      state.steps = state.steps.filter(s => s.id !== btn.dataset.remove);
      state.toml = buildToml(state.steps);
      modal.querySelector('#twbToml').value = state.toml;
      renderSteps(modal, state);
    }));
    root.querySelectorAll('[data-toggle]').forEach(btn=> btn.addEventListener('click', ()=> {
      const step = state.steps.find(s => s.id === btn.dataset.toggle);
      if (!step) return;
      step.enabled = step.enabled === false ? true : false;
      state.toml = buildToml(state.steps);
      modal.querySelector('#twbToml').value = state.toml;
      renderSteps(modal, state);
    }));
    root.querySelectorAll('[data-up]').forEach(btn=> btn.addEventListener('click', ()=> moveStep(state, btn.dataset.up, -1, modal)));
    root.querySelectorAll('[data-down]').forEach(btn=> btn.addEventListener('click', ()=> moveStep(state, btn.dataset.down, 1, modal)));
  }

  function moveStep(state, id, dir, modal){
    const idx = state.steps.findIndex(s => s.id === id);
    const nidx = idx + dir;
    if (idx < 0 || nidx < 0 || nidx >= state.steps.length) return;
    const [item] = state.steps.splice(idx,1);
    state.steps.splice(nidx,0,item);
    state.toml = buildToml(state.steps);
    modal.querySelector('#twbToml').value = state.toml;
    renderSteps(modal, state);
  }

  function renderPreview(modal, preview){
    const table = modal.querySelector('#twbPreviewTable');
    const rows = preview?.data || [];
    const cols = rows.length ? Object.keys(rows[0]) : [];
    const thead = `<thead><tr>${cols.map(c=>`<th>${esc(c)}</th>`).join('')}</tr></thead>`;
    const tbody = `<tbody>${rows.map(r=>`<tr>${cols.map(c=>`<td>${esc(r[c])}</td>`).join('')}</tr>`).join('')}</tbody>`;
    table.innerHTML = thead + tbody;
    modal.querySelector('#twbPageLabel').textContent = `Page ${preview?.page || 1} / ${preview?.totalPages || 1}`;
    modal.querySelector('#twbStatusRows').textContent = `${preview?.rowCount ?? 0}`;
    modal.querySelector('#twbStatusRender').textContent = `${preview?.renderDurationMs ?? 0} ms`;
    modal.querySelector('#twbStatusPreview').textContent = preview?.success ? 'OK' : 'Failed';
  }

  async function loadWorkbenchState(modal, state){
    // Fetch handler metadata and datasource transform state in parallel
    const [handlersData, data] = await Promise.all([
      fetchJson('/api/transforms/handlers', { credentials:'same-origin' }).catch(err => {
        console.error('[twb] Failed to load handler metadata:', err);
        return [];
      }),
      fetchJson(`/api/datasources/${encodeURIComponent(state.dsGuid)}/transform`, { credentials:'same-origin' })
    ]);
    state.handlers = Array.isArray(handlersData) ? handlersData : [];
    state.toml = data.toml || '';
    modal.querySelector('#twbToml').value = state.toml;
    renderRibbon(modal, state);
    renderDatasourceList(modal, state, data.workbench?.datasources || [state.ds]);
    const aiHint = modal.querySelector('#twbAiHint');
    aiHint.textContent = data.workbench?.aiSuggestion || 'Use ribbon actions to build ETL steps.';
  }

  async function doPreview(modal, state){
    state.toml = modal.querySelector('#twbToml').value || '';
    const body = {
      toml: state.toml,
      enabled: true,
      page: state.page,
      pageSize: state.pageSize,
      datasourceGuids: state.selectedDatasourceGuids,
      autoLoadDatasourceRows: true,
      autoLoadMaxRows: 5000
    };
    const preview = await fetchJson(`/api/datasources/${encodeURIComponent(state.dsGuid)}/transform/preview`, {
      method:'POST',
      credentials:'same-origin',
      headers:{ 'Content-Type':'application/json', 'RequestVerificationToken': token() },
      body: JSON.stringify(body)
    });
    state.preview = preview;
    renderPreview(modal, preview);
  }

  async function doValidate(modal, state){
    state.toml = modal.querySelector('#twbToml').value || '';
    const validation = await fetchJson(`/api/datasources/${encodeURIComponent(state.dsGuid)}/transform/validate`, {
      method:'POST',
      credentials:'same-origin',
      headers:{ 'Content-Type':'application/json', 'RequestVerificationToken': token() },
      body: JSON.stringify({ toml: state.toml })
    });
    modal.querySelector('#twbStatusValidation').textContent = validation.success ? 'Valid' : 'Invalid';
    modal.querySelector('#twbAiHint').textContent = validation.aiSuggestion || (validation.success ? 'Validation passed.' : 'Fix syntax and validate again.');
  }

  async function doSaveDraft(modal, state){
    state.toml = modal.querySelector('#twbToml').value || '';
    const res = await fetchJson(`/api/datasources/${encodeURIComponent(state.dsGuid)}/transform/draft`, {
      method:'POST',
      credentials:'same-origin',
      headers:{ 'Content-Type':'application/json', 'RequestVerificationToken': token() },
      body: JSON.stringify({ draftGuid: state.draftGuid, toml: state.toml, stepsJson: JSON.stringify(state.steps), datasourceGuids: state.selectedDatasourceGuids, name: 'AI Insights 365 ETL Draft' })
    });
    state.draftGuid = res.draftGuid;
    modal.querySelector('#twbStatusSave').textContent = 'Draft saved';
  }

  async function doPublish(modal, state){
    state.toml = modal.querySelector('#twbToml').value || '';
    await fetchJson(`/api/datasources/${encodeURIComponent(state.dsGuid)}/transform/publish`, {
      method:'POST', credentials:'same-origin', headers:{ 'Content-Type':'application/json', 'RequestVerificationToken': token() }, body: JSON.stringify({ toml: state.toml, draftGuid: state.draftGuid })
    });
    modal.querySelector('#twbStatusSave').textContent = 'Published';
  }

  function wire(modal, state){
    modal.querySelector('#twbToml').addEventListener('input', e => { state.toml = e.target.value; });
    modal.querySelector('#twbPreviewBtn').addEventListener('click', ()=> doPreview(modal, state).catch(err => alert(err.message)));
    modal.querySelector('#twbValidateBtn').addEventListener('click', ()=> doValidate(modal, state).catch(err => alert(err.message)));
    modal.querySelector('#twbSaveDraftBtn').addEventListener('click', ()=> doSaveDraft(modal, state).catch(err => alert(err.message)));
    modal.querySelector('#twbPublishBtn').addEventListener('click', ()=> doPublish(modal, state).catch(err => alert(err.message)));
    modal.querySelector('#twbAiSuggestBtn').addEventListener('click', ()=> doValidate(modal, state).catch(err => alert(err.message)));
    modal.querySelector('#twbPrev').addEventListener('click', ()=> { state.page = Math.max(1, state.page - 1); doPreview(modal,state).catch(()=>{}); });
    modal.querySelector('#twbNext').addEventListener('click', ()=> { state.page += 1; doPreview(modal,state).catch(()=>{}); });
  }

  window.transformWorkbench = api;
})();
