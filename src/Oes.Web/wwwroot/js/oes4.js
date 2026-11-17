// OES4 core script. Ported pieces of OES3's oes3.js; grows module by module.
// No inline scripts anywhere (the CSP forbids them), so page behaviour hooks in via data- attributes.
(function () {
    'use strict';

    const body = document.body;

    // ---------------- Sidebar toggle (OES3 behaviour) ----------------
    const toggle = document.getElementById('sidebarToggle');
    if (toggle) {
        toggle.addEventListener('click', function () {
            if (window.innerWidth >= 992) {
                body.classList.toggle('sidebar-collapsed');
                store('oes_sidebar', body.classList.contains('sidebar-collapsed') ? 'collapsed' : 'open');
            } else {
                body.classList.toggle('sidebar-open');
                syncBackdrop();
            }
        });
        if (window.innerWidth >= 992 && read('oes_sidebar') === 'collapsed') body.classList.add('sidebar-collapsed');
    }

    function syncBackdrop() {
        let backdrop = document.querySelector('.sidebar-backdrop');
        if (body.classList.contains('sidebar-open')) {
            if (!backdrop) {
                backdrop = document.createElement('div');
                backdrop.className = 'sidebar-backdrop';
                backdrop.addEventListener('click', function () {
                    body.classList.remove('sidebar-open');
                    backdrop.style.display = 'none';
                });
                body.appendChild(backdrop);
            }
            backdrop.style.display = 'block';
        } else if (backdrop) {
            backdrop.style.display = 'none';
        }
    }

    // ---------------- Collapsible admin groups (remember open ones) ----------------
    const NAV_KEY = 'oes_nav_open';
    let open = [];
    try { open = JSON.parse(read(NAV_KEY) || '[]'); } catch (e) { open = []; }
    document.querySelectorAll('.menu-group').forEach(function (g) {
        const key = g.getAttribute('data-group');
        const btn = g.querySelector('.menu-group-toggle');
        if (!g.classList.contains('has-active') && open.indexOf(key) > -1) {
            g.classList.add('open');
            btn.setAttribute('aria-expanded', 'true');
        }
        btn.addEventListener('click', function () {
            const isOpen = g.classList.toggle('open');
            btn.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
            open = open.filter(function (k) { return k !== key; });
            if (isOpen) open.push(key);
            store(NAV_KEY, JSON.stringify(open));
        });
    });

    // ---------------- data-confirm on submit buttons and links ----------------
    document.addEventListener('click', function (e) {
        const el = e.target.closest('[data-confirm]');
        if (el && !window.confirm(el.getAttribute('data-confirm'))) e.preventDefault();
    });

    // ---------------- data-autosubmit on selects ----------------
    document.querySelectorAll('select[data-autosubmit]').forEach(function (s) {
        s.addEventListener('change', function () { s.form.submit(); });
    });

    // ---------------- fetch helper with anti-forgery header ----------------
    const csrf = document.querySelector('meta[name="csrf-token"]');
    window.OES = window.OES || {};
    window.OES.api = async function (url, options) {
        const opts = Object.assign({ credentials: 'same-origin', headers: {} }, options || {});
        opts.headers = Object.assign({ 'Accept': 'application/json' }, opts.headers);
        if (opts.body && typeof opts.body !== 'string' && !(opts.body instanceof FormData)) {
            opts.body = JSON.stringify(opts.body);
            opts.headers['Content-Type'] = 'application/json';
        }
        if (csrf && opts.method && opts.method.toUpperCase() !== 'GET') opts.headers['X-CSRF-TOKEN'] = csrf.content;
        const res = await fetch(url, opts);
        if (!res.ok) throw Object.assign(new Error('Request failed: ' + res.status), { status: res.status, response: res });
        return res.status === 204 ? null : res.json();
    };

    // ---------------- helpers ----------------
    function el(tag, cls, text) {
        const e = document.createElement(tag);
        if (cls) e.className = cls;
        if (text != null) e.textContent = text; // never innerHTML with data
        return e;
    }
    function debounce(fn, ms) {
        let t;
        return function () { const a = arguments; clearTimeout(t); t = setTimeout(function () { fn.apply(null, a); }, ms); };
    }
    async function getJson(url) {
        const res = await fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } });
        if (!res.ok) throw new Error(res.status);
        return res.json();
    }

    // ---------------- clickable table rows ----------------
    document.addEventListener('click', function (e) {
        const row = e.target.closest('tr[data-href]');
        if (!row || e.target.closest('a, button, input, select, form')) return;
        window.location.href = row.getAttribute('data-href');
    });

    // ---------------- show/hide sections from a checkbox ----------------
    document.querySelectorAll('[data-toggle-target]').forEach(function (cb) {
        const target = document.querySelector(cb.getAttribute('data-toggle-target'));
        if (!target) return;
        cb.addEventListener('change', function () { target.classList.toggle('d-none', !cb.checked); });
    });
    document.querySelectorAll('input[data-autosubmit-check]').forEach(function (cb) {
        cb.addEventListener('change', function () { cb.form.submit(); });
    });

    // ---------------- typeahead pickers ----------------
    // <div class="typeahead" data-typeahead="/api/v1/lookup/x"> with a hidden [data-typeahead-value],
    // a text [data-typeahead-input] and a .typeahead-menu. Optional data-typeahead-clears hides a
    // "create new" section while an existing record is picked.
    document.querySelectorAll('[data-typeahead]').forEach(function (box) {
        const url = box.getAttribute('data-typeahead');
        const hidden = box.querySelector('[data-typeahead-value]');
        const input = box.querySelector('[data-typeahead-input]');
        const menu = box.querySelector('.typeahead-menu');
        const alt = box.getAttribute('data-typeahead-clears') ? document.querySelector(box.getAttribute('data-typeahead-clears')) : null;
        let active = -1;

        function setAlt() { if (alt) alt.classList.toggle('d-none', !!hidden.value); }
        function close() { menu.classList.remove('show'); active = -1; }
        function choose(item) {
            hidden.value = item.id;
            input.value = item.label;
            input.classList.add('is-picked');
            close();
            setAlt();
        }

        const search = debounce(async function () {
            const q = input.value.trim();
            if (q.length < 2) { close(); return; }
            let items = [];
            try { items = await getJson(url + (url.indexOf('?') > -1 ? '&' : '?') + 'q=' + encodeURIComponent(q)); } catch (e) { return; }
            menu.replaceChildren();
            if (items.length === 0) {
                menu.appendChild(el('span', 'dropdown-item-text text-muted small', 'No matches'));
            }
            items.forEach(function (item) {
                const a = el('button', 'dropdown-item');
                a.type = 'button';
                a.appendChild(el('div', 'fw-medium', item.label));
                if (item.detail) a.appendChild(el('div', 'small text-muted', item.detail));
                a.addEventListener('mousedown', function (ev) { ev.preventDefault(); choose(item); });
                menu.appendChild(a);
            });
            menu.classList.add('show');
        }, 200);

        input.addEventListener('input', function () {
            hidden.value = '';
            input.classList.remove('is-picked');
            setAlt();
            search();
        });
        input.addEventListener('keydown', function (e) {
            const items = menu.querySelectorAll('.dropdown-item');
            if (!menu.classList.contains('show') || items.length === 0) return;
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                e.preventDefault();
                active = (active + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
                items.forEach(function (it, i) { it.classList.toggle('active', i === active); });
            } else if (e.key === 'Enter' && active > -1) {
                e.preventDefault();
                items[active].dispatchEvent(new MouseEvent('mousedown'));
            } else if (e.key === 'Escape') { close(); }
        });
        input.addEventListener('blur', function () { setTimeout(close, 150); });
        if (hidden.value) input.classList.add('is-picked');
        setAlt();
    });

    // ---------------- global search ----------------
    const gs = document.getElementById('globalSearch');
    const gsMenu = document.getElementById('globalSearchMenu');
    if (gs && gsMenu) {
        const icons = { contact: 'bi-person', organization: 'bi-building', property: 'bi-house-door', opportunity: 'bi-funnel' };
        const labels = { contact: 'Contacts', organization: 'Organizations', property: 'Properties', opportunity: 'Opportunities' };
        const run = debounce(async function () {
            const q = gs.value.trim();
            if (q.length < 2) { gsMenu.classList.remove('show'); return; }
            let hits = [];
            try { hits = await getJson('/api/v1/search?q=' + encodeURIComponent(q)); } catch (e) { return; }
            gsMenu.replaceChildren();
            if (hits.length === 0) gsMenu.appendChild(el('span', 'dropdown-item-text text-muted small', 'Nothing found'));
            let lastKind = null;
            hits.forEach(function (h) {
                if (h.kind !== lastKind) { gsMenu.appendChild(el('h6', 'dropdown-header', labels[h.kind] || h.kind)); lastKind = h.kind; }
                const a = el('a', 'dropdown-item d-flex gap-2 align-items-start');
                a.href = h.url;
                a.appendChild(el('i', 'bi ' + (icons[h.kind] || 'bi-dot') + ' text-level-' + h.kind + ' mt-1'));
                const body = el('div');
                body.appendChild(el('div', 'fw-medium', h.title));
                if (h.subtitle) body.appendChild(el('div', 'small text-muted', h.subtitle));
                a.appendChild(body);
                gsMenu.appendChild(a);
            });
            gsMenu.classList.add('show');
        }, 200);
        gs.addEventListener('input', run);
        gs.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') gsMenu.classList.remove('show');
            if (e.key === 'Enter') { const first = gsMenu.querySelector('a.dropdown-item'); if (first) window.location.href = first.href; }
        });
        gs.addEventListener('blur', function () { setTimeout(function () { gsMenu.classList.remove('show'); }, 150); });
        document.addEventListener('keydown', function (e) {
            if (e.key === '/' && !e.target.closest('input, textarea, select, [contenteditable]')) { e.preventDefault(); gs.focus(); }
        });
    }

    // ---------------- duplicate address check while typing ----------------
    document.querySelectorAll('[data-address-check]').forEach(function (box) {
        const url = box.getAttribute('data-address-check');
        const out = box.querySelector('.address-matches');
        if (!out) return;
        const part = function (suffix) {
            const i = box.querySelector('[data-address-part][name$="' + suffix + '"]');
            return i ? i.value.trim() : '';
        };
        const check = debounce(async function () {
            const line1 = part('Line1'), zip = part('PostalCode'), city = part('City');
            if (line1.length < 4 || (zip.length < 5 && city.length < 2)) { out.classList.add('d-none'); return; }
            const qs = new URLSearchParams({ Line1: line1, Line2: part('Line2'), City: city, State: part('State'), PostalCode: zip });
            let matches = [];
            try { matches = await getJson(url + '?' + qs.toString()); } catch (e) { return; }
            out.replaceChildren();
            if (matches.length === 0) { out.classList.add('d-none'); return; }
            const exact = matches.filter(function (m) { return m.exact; });
            out.appendChild(el('div', 'fw-medium mb-1', exact.length ? 'This address already exists:' : 'Similar addresses you already have:'));
            matches.forEach(function (m) {
                const row = el('div');
                const a = el('a', null, m.address);
                a.href = '/Properties/View/' + m.id;
                a.target = '_blank';
                row.appendChild(el('i', 'bi ' + (m.exact ? 'bi-exclamation-circle' : 'bi-house') + ' me-1'));
                row.appendChild(a);
                out.appendChild(row);
            });
            if (exact.length) out.appendChild(el('div', 'small mt-1', 'Saving will reuse the existing property instead of creating a duplicate.'));
            out.classList.remove('d-none');
        }, 400);
        box.querySelectorAll('[data-address-part]').forEach(function (i) { i.addEventListener('input', check); });
    });

    // ---------------- pipeline board drag and drop ----------------
    const board = document.querySelector('[data-board]');
    if (board) {
        let lostReasons = [];
        try { lostReasons = JSON.parse(board.getAttribute('data-lost-reasons') || '[]'); } catch (e) { lostReasons = []; }
        let dragged = null;
        board.addEventListener('dragstart', function (e) {
            const card = e.target.closest('.pipeline-card');
            if (!card) return;
            dragged = card;
            card.classList.add('dragging');
            e.dataTransfer.effectAllowed = 'move';
            e.dataTransfer.setData('text/plain', card.getAttribute('data-opportunity-id'));
        });
        board.addEventListener('dragend', function () {
            if (dragged) dragged.classList.remove('dragging');
            board.querySelectorAll('.drop-target').forEach(function (z) { z.classList.remove('drop-target'); });
        });
        board.querySelectorAll('[data-dropzone]').forEach(function (zone) {
            zone.addEventListener('dragover', function (e) { e.preventDefault(); zone.classList.add('drop-target'); });
            zone.addEventListener('dragleave', function () { zone.classList.remove('drop-target'); });
            zone.addEventListener('drop', async function (e) {
                e.preventDefault();
                zone.classList.remove('drop-target');
                if (!dragged) return;
                const column = zone.closest('.pipeline-column');
                const from = dragged.parentElement;
                if (from === zone) return;
                const body = { stageId: column.getAttribute('data-stage-id') };
                if (column.getAttribute('data-outcome') === 'lost') {
                    const choices = lostReasons.map(function (r, i) { return (i + 1) + '. ' + r.label; }).join('\n');
                    const pick = window.prompt('Why was it lost?\n' + choices + '\n\nEnter a number:');
                    const idx = parseInt(pick, 10) - 1;
                    if (isNaN(idx) || !lostReasons[idx]) return;
                    body.lostReasonKey = lostReasons[idx].value;
                }
                const card = dragged;
                zone.prepend(card);
                updateCounts();
                try {
                    await window.OES.api('/api/v1/opportunities/' + card.getAttribute('data-opportunity-id') + '/stage', { method: 'PUT', body: body });
                } catch (err) {
                    from.prepend(card);
                    updateCounts();
                    let msg = 'Could not move that card.';
                    try { const p = await err.response.json(); if (p && p.detail) msg = p.detail; } catch (x) { /* keep default */ }
                    window.alert(msg);
                }
            });
        });
        function updateCounts() {
            board.querySelectorAll('.pipeline-column').forEach(function (c) {
                c.querySelector('.pipeline-count').textContent = c.querySelectorAll('.pipeline-card').length;
            });
        }
    }

    function read(k) { try { return localStorage.getItem(k); } catch (e) { return null; } }
    function store(k, v) { try { localStorage.setItem(k, v); } catch (e) { /* storage blocked */ } }
})();
