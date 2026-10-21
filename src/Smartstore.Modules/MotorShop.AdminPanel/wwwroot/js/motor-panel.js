// MOTOR premium admin panel. Progressive enhancement: lists, filters and links work as plain GET requests;
// writes go through antiforgery-protected POSTs and are validated again on the server.
(function () {
    'use strict';

    const strings = JSON.parse(document.getElementById('mp-strings')?.textContent || '{}');
    const token = document.querySelector('meta[name="mp-xsrf"]')?.content || '';
    const trNumber = new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    const fmt = (text, ...args) => String(text || '').replace(/\{(\d+)\}/g, (_, i) => args[i] ?? '');

    // ---------- Requests ----------

    async function post(url, body) {
        const headers = { 'X-XSRF-Token': token, 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' };
        let payload = body;
        if (!(body instanceof FormData)) {
            headers['Content-Type'] = 'application/json';
            payload = JSON.stringify(body || {});
        }

        let response;
        try {
            response = await fetch(url, { method: 'POST', body: payload, headers, credentials: 'same-origin' });
        } catch {
            return { ok: false, status: 0, data: { error: strings.error } };
        }

        if (response.status === 401) {
            window.location.reload();
            return { ok: false, status: 401, data: {} };
        }

        let data = {};
        try { data = await response.json(); } catch { /* not JSON */ }

        if (response.status === 403 && !data.error) {
            data.error = data.message || strings.error;
        }

        return { ok: response.ok, status: response.status, data };
    }

    async function getHtml(url) {
        const response = await fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' }, credentials: 'same-origin' });
        if (response.status === 401) {
            window.location.reload();
            throw new Error('401');
        }
        if (!response.ok) throw new Error(String(response.status));
        return response.text();
    }

    // ---------- Toasts ----------

    function toast(text, isError) {
        const host = document.querySelector('.mp-toasts');
        if (!host || !text) return;
        const el = document.createElement('div');
        el.className = 'mp-toast' + (isError ? ' mp-toast--error' : '');
        el.textContent = text;
        host.appendChild(el);
        setTimeout(() => el.remove(), isError ? 8000 : 4000);
    }

    // ---------- Confirm dialog ----------

    function confirmDialog({ title, text, okLabel, danger = false, html = null }) {
        const dialog = document.getElementById('mp-confirm');
        if (!dialog?.showModal) return Promise.resolve(window.confirm(text || title));

        dialog.querySelector('.mp-dialog__title').textContent = title || strings.confirm;
        const body = dialog.querySelector('.mp-dialog__text');
        body.textContent = text || '';
        if (html) body.appendChild(html);

        const ok = dialog.querySelector('.mp-dialog__ok');
        ok.textContent = okLabel || strings.confirm;
        ok.classList.toggle('mp-btn--danger', danger);
        ok.classList.toggle('mp-btn--accent', !danger);
        ok.hidden = okLabel === false;

        dialog.showModal();
        (danger ? dialog.querySelector('button[value="cancel"]') : ok).focus();

        // Resolved from the buttons and Escape directly: the dialog's close event is not reliable while
        // another modal (the drawer) is open.
        return new Promise(resolve => {
            const finish = result => {
                dialog.removeEventListener('click', onClick);
                dialog.removeEventListener('cancel', onCancel);
                if (dialog.open) dialog.close();
                resolve(result);
            };
            const onClick = e => {
                const button = e.target.closest('button[value]');
                if (button) finish(button.value === 'ok');
            };
            const onCancel = e => {
                e.preventDefault();
                finish(false);
            };
            dialog.addEventListener('click', onClick);
            dialog.addEventListener('cancel', onCancel);
        });
    }

    // ---------- Drawer (editors) ----------

    const drawer = document.getElementById('mp-drawer');
    let drawerDirty = false;
    let drawerOnSaved = null;
    let drawerOpener = null;

    async function openDrawer(url, onSaved) {
        if (!drawer?.showModal) {
            window.location.href = url;
            return;
        }

        drawerOpener = document.activeElement;
        drawerOnSaved = onSaved;
        const content = drawer.querySelector('.mp-drawer__content');

        try {
            content.innerHTML = await getHtml(url);
        } catch {
            toast(strings.loadError, true);
            return;
        }

        drawerDirty = false;
        if (!drawer.open) drawer.showModal();
        content.querySelector('input:not([type=hidden]):not([disabled]), select:not([disabled]), textarea:not([disabled])')?.focus();
    }

    async function closeDrawer(force) {
        if (!drawer?.open) return;
        if (drawerDirty && !force) {
            const leave = await confirmDialog({ title: strings.unsaved, okLabel: strings.close, danger: true });
            if (!leave) return;
        }
        drawerDirty = false;
        drawer.close();
        drawer.querySelector('.mp-drawer__content').innerHTML = '';
        drawerOpener?.focus?.();
    }

    if (drawer) {
        drawer.addEventListener('cancel', e => {
            e.preventDefault();
            closeDrawer(false);
        });

        drawer.addEventListener('click', e => {
            if (e.target === drawer || e.target.closest('[data-close]')) {
                e.preventDefault();
                closeDrawer(false);
            }
        });

        drawer.addEventListener('input', e => {
            if (e.target.closest('form[data-editor]')) drawerDirty = true;

            const filter = e.target.closest('[data-filter-list]');
            if (filter) {
                const term = filter.value.trim().toLocaleLowerCase('tr-TR');
                drawer.querySelectorAll(filter.dataset.filterList + ' [data-filter-text]').forEach(item => {
                    item.hidden = term !== '' && !item.dataset.filterText.toLocaleLowerCase('tr-TR').includes(term);
                });
            }
        });

        drawer.addEventListener('submit', async e => {
            const form = e.target.closest('form[data-editor]');
            if (!form) return;
            e.preventDefault();

            const submit = form.querySelector('button[type=submit]');
            submit?.setAttribute('disabled', '');
            form.querySelectorAll('[data-error-for]').forEach(x => (x.textContent = ''));
            form.querySelectorAll('[aria-invalid]').forEach(x => x.removeAttribute('aria-invalid'));
            const formError = form.querySelector('[data-form-error]');
            if (formError) formError.hidden = true;

            const result = await post(form.action, new FormData(form));
            submit?.removeAttribute('disabled');

            if (result.ok && result.data.success !== false) {
                drawerDirty = false;
                toast(result.data.message || strings.saved);
                await closeDrawer(true);
                if (form.hasAttribute('data-reload')) {
                    window.location.reload();
                } else {
                    drawerOnSaved?.(result.data);
                }
                return;
            }

            const errors = result.data.errors || { '': result.data.error || strings.error };
            let firstField = null;
            for (const [key, message] of Object.entries(errors)) {
                const slot = key ? form.querySelector(`[data-error-for="${CSS.escape(key)}"]`) : null;
                const input = key ? form.querySelector(`[name="${CSS.escape(key)}"]`) : null;
                if (slot) {
                    slot.textContent = message;
                    input?.setAttribute('aria-invalid', 'true');
                    firstField ??= input;
                } else if (formError) {
                    formError.textContent = message;
                    formError.hidden = false;
                }
            }
            (firstField || formError)?.scrollIntoView?.({ block: 'center' });
            firstField?.focus();
        });
    }

    window.addEventListener('beforeunload', e => {
        if (drawerDirty) {
            e.preventDefault();
            e.returnValue = '';
        }
    });

    // Anything with data-open-editor opens its URL in the drawer.
    document.addEventListener('click', e => {
        const opener = e.target.closest('[data-open-editor]');
        if (!opener || drawer?.contains(opener)) return;
        e.preventDefault();
        openDrawer(opener.dataset.openEditor, () => refreshGrid?.());
    });

    // ---------- Price lock switch ----------

    const lockCard = document.getElementById('mp-price-lock');
    if (lockCard) {
        const toggle = lockCard.querySelector('.mp-switch');
        toggle?.addEventListener('click', async () => {
            const turnOn = toggle.getAttribute('aria-checked') !== 'true';
            const yes = await confirmDialog({
                title: lockCard.querySelector('.mp-card__label').textContent,
                text: turnOn ? strings.priceLockOn : strings.priceLockOff,
                danger: turnOn
            });
            if (!yes) return;

            const body = new FormData();
            body.append('hideAllPrices', turnOn ? 'true' : 'false');
            toggle.disabled = true;
            const result = await post(lockCard.dataset.url, body);
            toggle.disabled = false;

            if (!result.ok) {
                toast(result.data.error || strings.error, true);
                return;
            }

            const on = result.data.hideAllPrices;
            toggle.setAttribute('aria-checked', on ? 'true' : 'false');
            const text = toggle.querySelector('.mp-switch__text');
            text.textContent = on ? text.dataset.on : text.dataset.off;
            lockCard.classList.toggle('mp-card--alert', on);
            const icon = lockCard.querySelector('.mp-card__icon');
            icon.classList.toggle('mp-card__icon--red', on);
            icon.classList.toggle('mp-card__icon--green', !on);
            toast(result.data.message);
        });
    }

    // ---------- Categories page ----------

    document.addEventListener('click', async e => {
        const move = e.target.closest('[data-move]');
        if (!move) return;
        const body = new FormData();
        body.append('up', move.dataset.up);
        const result = await post(move.dataset.move, body);
        if (result.ok && result.data.success) {
            window.location.reload();
        } else {
            toast(result.data.error || strings.error, true);
        }
    });

    // ---------- Products: filters, grid, selection, inline edits, bulk ----------

    const form = document.querySelector('form.mp-toolbar[data-grid-url]');
    const grid = document.getElementById('mp-grid');
    let refreshGrid = null;
    if (!form || !grid) return;

    const gridUrl = form.dataset.gridUrl;
    const pageUrl = form.getAttribute('action');
    const fieldUrl = id => grid.dataset.fieldUrl.replace(/\/0\/field/, `/${id}/field`);
    let timer = 0;
    let controller = null;

    // Selection lives on the current page; "all matching" switches to the whole filter result.
    const selected = new Set();
    let allMatching = false;

    function queryFromForm() {
        const params = new URLSearchParams();
        for (const [key, value] of new FormData(form)) {
            // Keep deep links short: skip empty values and defaults.
            if (value === '' || value === 'All' || (key === 'categoryId' && value === '0') || (key === 'size' && value === '25')) continue;
            params.append(key, value);
        }
        return params;
    }

    function currentParams() {
        const params = new URLSearchParams(window.location.search);
        params.delete('area');
        return params;
    }

    function syncForm(params) {
        for (const el of form.elements) {
            if (!el.name) continue;
            const value = params.get(el.name);
            if (el.tagName === 'SELECT') {
                el.value = value ?? (el.name === 'categoryId' ? '0' : el.name === 'size' ? '25' : 'All');
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
                window.location.href = pageUrl + (qs ? '?' + qs : '');
                return;
            }
            if (!response.ok) throw new Error(response.status);

            grid.innerHTML = await response.text();
            clearSelection();

            if (push) {
                history.pushState({ mp: qs }, '', pageUrl + (qs ? '?' + qs : '') + '#mp-products');
            }
        } catch (err) {
            if (err.name !== 'AbortError') toast(form.dataset.errorText || strings.loadError, true);
        } finally {
            grid.setAttribute('aria-busy', 'false');
        }
    }

    refreshGrid = () => load(currentParams(), false);

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

    window.addEventListener('popstate', () => {
        const params = currentParams();
        syncForm(params);
        load(params, false);
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

    // --- Selection and bulk toolbar ---

    const bulk = document.getElementById('mp-bulk');

    function totalCount() {
        return Number(grid.querySelector('.mp-grid__meta')?.dataset.total || 0);
    }

    function updateBulk() {
        if (!bulk) return;
        const count = allMatching ? totalCount() : selected.size;
        bulk.hidden = count === 0;
        bulk.querySelector('[data-bulk-count]').textContent = fmt(strings.selected, count.toLocaleString('tr-TR'));

        const allBtn = bulk.querySelector('[data-bulk-all]');
        const pageIds = grid.querySelectorAll('[data-select]');
        const pageFull = pageIds.length > 0 && selected.size === pageIds.length;
        allBtn.hidden = allMatching || !pageFull || totalCount() <= pageIds.length;
        allBtn.textContent = fmt(bulk.dataset.allLabel, totalCount().toLocaleString('tr-TR'));

        const pageBox = grid.querySelector('[data-select-page]');
        if (pageBox) {
            pageBox.checked = pageFull || allMatching;
            pageBox.indeterminate = !pageBox.checked && selected.size > 0;
        }
    }

    function clearSelection() {
        selected.clear();
        allMatching = false;
        grid.querySelectorAll('[data-select]').forEach(x => (x.checked = false));
        updateBulk();
    }

    grid.addEventListener('change', e => {
        if (e.target.matches('[data-select-page]')) {
            allMatching = false;
            grid.querySelectorAll('[data-select]').forEach(box => {
                box.checked = e.target.checked;
                if (box.checked) selected.add(Number(box.value)); else selected.delete(Number(box.value));
            });
            updateBulk();
        } else if (e.target.matches('[data-select]')) {
            allMatching = false;
            if (e.target.checked) selected.add(Number(e.target.value)); else selected.delete(Number(e.target.value));
            updateBulk();
        }
    });

    if (bulk) {
        const actionSelect = bulk.querySelector('select[name=action]');
        const valueField = bulk.querySelector('[data-bulk-value]');
        const categoryField = bulk.querySelector('[data-bulk-category]');

        bulk.querySelector('[data-bulk-all]').addEventListener('click', () => {
            allMatching = true;
            updateBulk();
        });
        bulk.querySelector('[data-bulk-clear]').addEventListener('click', clearSelection);

        actionSelect.addEventListener('change', () => {
            const input = actionSelect.selectedOptions[0]?.dataset.input;
            valueField.hidden = !input || input === 'category';
            categoryField.hidden = input !== 'category';
            bulk.querySelector('[data-hint-percent]').hidden = input !== 'percent';
            bulk.querySelector('[data-hint-signed]').hidden = input !== 'signed';
            const value = valueField.querySelector('input');
            value.value = '';
            value.required = !valueField.hidden;
            value.inputMode = input === 'integer' ? 'numeric' : 'decimal';
        });

        bulk.addEventListener('submit', async e => {
            e.preventDefault();
            const option = actionSelect.selectedOptions[0];
            if (!option?.value) {
                actionSelect.focus();
                return;
            }

            const count = allMatching ? totalCount() : selected.size;
            const value = bulk.querySelector('input[name=value]').value.trim();
            const categorySelect = bulk.querySelector('select[name=categoryId]');
            const detail = option.dataset.input === 'category'
                ? categorySelect.selectedOptions[0]?.textContent.trim()
                : value;

            const yes = await confirmDialog({
                title: option.textContent.trim() + (detail ? `: ${detail}` : ''),
                text: fmt(strings.bulkConfirm, count.toLocaleString('tr-TR')),
                danger: option.dataset.danger === 'true'
            });
            if (!yes) return;

            const filter = Object.fromEntries(queryFromForm());
            // Property names match the C# model exactly (the JSON input is case sensitive).
            const request = {
                Action: option.value,
                Value: value,
                CategoryId: Number(categorySelect.value || 0),
                Ids: allMatching ? [] : [...selected],
                AllMatching: allMatching,
                Filter: {
                    Q: filter.q || null,
                    CategoryId: Number(filter.categoryId || 0),
                    Stock: filter.stock || 'All',
                    Price: filter.price || 'All',
                    Status: filter.status || 'All'
                }
            };

            const submit = bulk.querySelector('button[type=submit]');
            submit.disabled = true;
            grid.setAttribute('aria-busy', 'true');
            const result = await post(bulk.dataset.url, request);
            submit.disabled = false;
            grid.setAttribute('aria-busy', 'false');

            if (!result.ok && result.data.error) {
                toast(result.data.error, true);
                return;
            }

            const r = result.data;
            toast(fmt(strings.bulkDone, r.updated, r.failures?.length || 0, r.skipped || 0), (r.failures?.length || 0) > 0);

            if (r.failures?.length) {
                const list = document.createElement('ul');
                list.className = 'mp-failures';
                r.failures.slice(0, 50).forEach(f => {
                    const li = document.createElement('li');
                    li.textContent = `${f.name} (#${f.id}): ${f.reason}`;
                    list.appendChild(li);
                });
                await confirmDialog({ title: fmt(strings.bulkFailed, r.failures.length), text: '', html: list, okLabel: false });
            }

            refreshGrid();
        });
    }

    // --- Inline edit of price, sale price and stock ---

    function displayInputValue(cell) {
        const raw = cell.dataset.value;
        if (raw === '' || raw == null) return '';
        if (cell.dataset.integer) return raw;
        return trNumber.format(Number(raw));
    }

    function startEdit(cell) {
        if (cell.querySelector('input')) return;
        const original = cell.innerHTML;
        const input = document.createElement('input');
        input.className = 'mp-cell-input';
        input.value = displayInputValue(cell);
        input.inputMode = cell.dataset.integer ? 'numeric' : 'decimal';
        input.setAttribute('aria-label', cell.getAttribute('aria-label'));
        cell.replaceChildren(input);
        cell.classList.add('is-editing');
        input.focus();
        input.select();

        let done = false;
        const finish = async save => {
            if (done) return;
            done = true;
            const value = input.value.trim();

            if (!save || value === displayInputValue(cell)) {
                cell.innerHTML = original;
                cell.classList.remove('is-editing');
                cell.focus();
                return;
            }

            input.disabled = true;
            cell.classList.add('is-saving');
            const row = cell.closest('tr');
            const body = new FormData();
            body.append('field', cell.dataset.edit);
            body.append('value', value);
            body.append('expected', cell.dataset.value);
            const result = await post(fieldUrl(row.dataset.id), body);
            cell.classList.remove('is-saving', 'is-editing');

            if (result.ok && result.data.success) {
                cell.dataset.value = result.data.value;
                cell.textContent = result.data.display;
                if (cell.dataset.edit === 'stock') {
                    const inStock = Number(result.data.value) > 0;
                    cell.classList.toggle('mp-badge--green', inStock);
                    cell.classList.toggle('mp-badge--red', !inStock);
                }
                cell.classList.remove('mp-muted', 'mp-strike');
                flash(cell, true);
                updateZeroBadge(row);
            } else {
                if (result.data.conflict) {
                    cell.dataset.value = result.data.value ?? cell.dataset.value;
                    cell.textContent = result.data.display ?? '';
                } else {
                    cell.innerHTML = original;
                }
                flash(cell, false);
                toast(result.data.error || strings.error, true);
            }
            cell.focus();
        };

        input.addEventListener('keydown', e => {
            if (e.key === 'Enter') { e.preventDefault(); finish(true); }
            if (e.key === 'Escape') { e.preventDefault(); finish(false); }
        });
        input.addEventListener('blur', () => finish(true));
    }

    function flash(el, ok) {
        el.classList.add(ok ? 'is-saved' : 'is-failed');
        setTimeout(() => el.classList.remove('is-saved', 'is-failed'), 1200);
    }

    function updateZeroBadge(row) {
        const price = Number(row.querySelector('[data-edit="price"]')?.dataset.value ?? 1);
        const cfp = row.querySelector('[data-toggle="callForPrice"]')?.dataset.value === 'true';
        const badge = row.querySelector('[data-zero-badge]');
        if (badge) badge.hidden = !(price <= 0 && !cfp);
    }

    async function toggle(button) {
        const row = button.closest('tr');
        const next = button.dataset.value !== 'true';
        const body = new FormData();
        body.append('field', button.dataset.toggle);
        body.append('value', next ? 'true' : 'false');
        body.append('expected', button.dataset.value);

        button.disabled = true;
        const result = await post(fieldUrl(row.dataset.id), body);
        button.disabled = false;

        const apply = state => {
            button.dataset.value = state ? 'true' : 'false';
            button.textContent = state ? button.dataset.labelTrue : button.dataset.labelFalse;
            button.classList.toggle(button.dataset.classTrue, state);
            button.classList.toggle(button.dataset.classFalse, !state);
        };

        if (result.ok && result.data.success) {
            apply(result.data.value === 'true');
            flash(button, true);
            updateZeroBadge(row);
        } else {
            if (result.data.conflict) apply(result.data.value === 'true');
            flash(button, false);
            toast(result.data.error || strings.error, true);
        }
    }

    grid.addEventListener('click', async e => {
        const cell = e.target.closest('button.mp-cell[data-edit]');
        if (cell) {
            startEdit(cell);
            return;
        }

        const toggleBtn = e.target.closest('button[data-toggle]');
        if (toggleBtn) {
            toggle(toggleBtn);
            return;
        }

        const dup = e.target.closest('[data-duplicate]');
        if (dup) {
            const name = dup.closest('tr').dataset.name;
            if (!await confirmDialog({ title: fmt(strings.duplicateConfirm, name) })) return;
            const result = await post(dup.dataset.duplicate, {});
            toast(result.data.message || result.data.error || strings.error, !result.ok);
            if (result.ok) refreshGrid();
            return;
        }

        const del = e.target.closest('[data-delete]');
        if (del) {
            const name = del.closest('tr').dataset.name;
            if (!await confirmDialog({ title: fmt(strings.deleteConfirm, name), danger: true })) return;
            const result = await post(del.dataset.delete, {});
            toast(result.data.message || result.data.error || strings.error, !result.ok);
            if (result.ok) refreshGrid();
        }
    });
})();
