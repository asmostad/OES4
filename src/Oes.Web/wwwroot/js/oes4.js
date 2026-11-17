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

    function read(k) { try { return localStorage.getItem(k); } catch (e) { return null; } }
    function store(k, v) { try { localStorage.setItem(k, v); } catch (e) { /* storage blocked */ } }
})();
