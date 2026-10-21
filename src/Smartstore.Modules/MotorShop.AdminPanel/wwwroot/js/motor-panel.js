// MOTOR premium admin panel. Progressive enhancement only: every filter, sort and page link
// also works as a normal GET request without JavaScript.
(function () {
    'use strict';

    const form = document.querySelector('.mp-toolbar');
    const grid = document.getElementById('mp-grid');
    if (!form || !grid) return;

    const gridUrl = form.dataset.gridUrl;
    const pageUrl = form.getAttribute('action');
    let timer = 0;
    let controller = null;

    function queryFromForm() {
        const params = new URLSearchParams();
        for (const [key, value] of new FormData(form)) {
            // Keep deep links short: skip empty values and defaults.
            if (value === '' || value === 'All' || (key === 'categoryId' && value === '0')) continue;
            params.append(key, value);
        }
        return params;
    }

    function syncForm(params) {
        for (const el of form.elements) {
            if (!el.name) continue;
            const value = params.get(el.name);
            if (el.tagName === 'SELECT') {
                el.value = value ?? (el.name === 'categoryId' ? '0' : 'All');
            } else {
                el.value = value ?? '';
            }
        }
    }

    async function load(params, push) {
        controller?.abort();
        controller = new AbortController();
        grid.setAttribute('aria-busy', 'true');

        try {
            const qs = params.toString();
            const response = await fetch(gridUrl + (qs ? '?' + qs : ''), {
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                credentials: 'same-origin',
                signal: controller.signal
            });

            if (response.status === 401 || response.redirected) {
                // Session expired: let the server handle the sign-in redirect.
                window.location.href = pageUrl + (qs ? '?' + qs : '');
                return;
            }
            if (!response.ok) throw new Error(response.status);

            grid.innerHTML = await response.text();

            if (push) {
                history.pushState({ mp: qs }, '', pageUrl + (qs ? '?' + qs : '') + '#mp-products');
            }
        } catch (err) {
            if (err.name !== 'AbortError') toast(form.dataset.errorText || 'Liste yüklenemedi.', true);
        } finally {
            grid.setAttribute('aria-busy', 'false');
        }
    }

    function toast(text, isError) {
        const host = document.querySelector('.mp-toasts');
        if (!host) return;
        const el = document.createElement('div');
        el.className = 'mp-toast' + (isError ? ' mp-toast--error' : '');
        el.textContent = text;
        host.appendChild(el);
        setTimeout(() => el.remove(), 5000);
    }

    form.addEventListener('submit', e => {
        e.preventDefault();
        load(queryFromForm(), true);
    });

    form.addEventListener('change', e => {
        if (e.target.name === 'q') return;
        load(queryFromForm(), true);
    });

    form.addEventListener('input', e => {
        if (e.target.name !== 'q') return;
        clearTimeout(timer);
        timer = setTimeout(() => load(queryFromForm(), true), 350);
    });

    // Sort headers, pagination and "reset" links inside the grid.
    grid.addEventListener('click', e => {
        const link = e.target.closest('a.mp-sort, a.mp-pager__link, .mp-empty a');
        if (!link || e.ctrlKey || e.metaKey || e.shiftKey || e.button !== 0) return;
        e.preventDefault();
        const url = new URL(link.href, window.location.href);
        url.searchParams.delete('area');
        syncForm(url.searchParams);
        load(url.searchParams, true);
        grid.closest('section')?.scrollIntoView({ block: 'start' });
    });

    window.addEventListener('popstate', () => {
        const params = new URLSearchParams(window.location.search);
        syncForm(params);
        load(params, false);
    });
})();
