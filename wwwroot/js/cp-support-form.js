// Wires up the public Support page form (Views/Home/Support.cshtml) and any
// in-app "Contact Support" form (Settings) that uses the same field IDs.
(function () {
    'use strict';

    function showAlert(el, type, message) {
        if (!el) return;
        el.className = 'alert alert-' + type;
        el.textContent = message;
        el.classList.remove('d-none');
    }

    function getJwt() {
        try { return localStorage.getItem('cp_token') || ''; } catch (e) { return ''; }
    }

    function bindForm(form) {
        if (!form || form.dataset.cpSupportBound === '1') return;
        form.dataset.cpSupportBound = '1';

        form.addEventListener('submit', function (e) {
            e.preventDefault();
            var alertEl = document.getElementById('supportAlert')
                || form.querySelector('.support-alert');
            var btn = form.querySelector('button[type="submit"]');
            var orig = btn ? btn.innerHTML : '';

            var payload = {
                name: (document.getElementById('supportName') || {}).value || '',
                email: (document.getElementById('supportEmail') || {}).value || '',
                category: (document.getElementById('supportCategory') || {}).value || 'Question',
                priority: (document.getElementById('supportPriority') || {}).value || 'Normal',
                subject: (document.getElementById('supportSubject') || {}).value || '',
                message: (document.getElementById('supportMessage') || {}).value || ''
            };

            if (!payload.email || !payload.subject || !payload.message) {
                showAlert(alertEl, 'warning', 'Please fill in email, subject and message.');
                return;
            }

            if (btn) {
                btn.disabled = true;
                btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Submitting…';
            }
            if (alertEl) alertEl.classList.add('d-none');

            var headers = { 'Content-Type': 'application/json' };
            var token = getJwt();
            if (token) headers['Authorization'] = 'Bearer ' + token;

            fetch('/api/support/ticket', {
                method: 'POST',
                headers: headers,
                body: JSON.stringify(payload)
            }).then(function (r) {
                return r.json().then(function (j) { return { ok: r.ok, body: j }; });
            }).then(function (res) {
                if (!res.ok) {
                    showAlert(alertEl, 'danger', (res.body && res.body.error) || 'Could not submit ticket.');
                    return;
                }
                form.reset();
                if (window.cpSuccessModal) {
                    window.cpSuccessModal({
                        title: 'Ticket Submitted',
                        message: 'Thanks! Our support team will reply by email within your plan\'s SLA.',
                        details: [
                            ['Ticket Number', res.body.ticketNumber],
                            ['Sent To', 'support@AIInsights365.net'],
                            ['Priority', payload.priority]
                        ],
                        okLabel: 'Done'
                    });
                } else {
                    showAlert(alertEl, 'success', 'Ticket ' + res.body.ticketNumber + ' submitted. Check your inbox for confirmation.');
                }
            }).catch(function () {
                showAlert(alertEl, 'danger', 'Network error. Please try again.');
            }).finally(function () {
                if (btn) { btn.disabled = false; btn.innerHTML = orig; }
            });
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        bindForm(document.getElementById('supportForm'));
        bindForm(document.getElementById('settingsSupportForm'));
    });
})();
