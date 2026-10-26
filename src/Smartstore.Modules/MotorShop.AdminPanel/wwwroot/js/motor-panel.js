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

    // Field errors go next to their input ([data-error-for]); the rest into the form's error box.
    function clearFormErrors(form) {
        form.querySelectorAll('[data-error-for]').forEach(x => (x.textContent = ''));
        form.querySelectorAll('[aria-invalid]').forEach(x => x.removeAttribute('aria-invalid'));
        const formError = form.querySelector('[data-form-error]');
        if (formError) formError.hidden = true;
    }

    function showFormErrors(form, data) {
        const errors = data.errors || { '': data.error || strings.error };
        const formError = form.querySelector('[data-form-error]');
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
            } else {
                toast(message, true);
            }
        }
        (firstField || formError)?.scrollIntoView?.({ block: 'center' });
        firstField?.focus();
    }

    // Coupon editor: show the fields that belong to the chosen discount type.
    function syncCoupon(root) {
        const form = root.querySelector('form[data-coupon]');
        if (!form) return;
        const type = form.querySelector('[data-coupon-type]').value;
        const percent = form.querySelector('[data-coupon-percent]').checked;
        form.querySelector('[data-show-percent]').hidden = !percent;
        form.querySelector('[data-show-amount]').hidden = percent;
        form.querySelector('[data-show-categories]').hidden = type !== 'AssignedToCategories';
        form.querySelector('[data-show-skus]').hidden = type !== 'AssignedToSkus';
    }

    // ---------- Drawer (editors) ----------

    const drawer = document.getElementById('mp-drawer');
    let drawerDirty = false;
    let drawerOnSaved = null;
    let drawerOpener = null;
    let drawerUrl = null;

    async function openDrawer(url, onSaved) {
        if (!drawer?.showModal) {
            window.location.href = url;
            return;
        }

        if (!drawer.open) {
            drawerOpener = document.activeElement;
        }
        drawerOnSaved = onSaved ?? drawerOnSaved;
        drawerUrl = url;
        const content = drawer.querySelector('.mp-drawer__content');

        try {
            content.innerHTML = await getHtml(url);
        } catch {
            toast(strings.loadError, true);
            return;
        }

        drawerDirty = false;
        syncCoupon(content);
        if (!drawer.open) drawer.showModal();
        // Editors focus their first field; detail views focus the title so they open at the top.
        const first = content.querySelector('form[data-editor] :is(input:not([type=hidden]), select, textarea):not([disabled])');
        if (first) {
            first.focus();
        } else {
            const title = content.querySelector('.mp-drawer__title');
            title?.setAttribute('tabindex', '-1');
            title?.focus();
        }
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

        drawer.addEventListener('change', () => syncCoupon(drawer));

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

        // Small action forms in a detail view (mark paid, ship, add note...): confirm if asked, post,
        // then reload the same view and the list behind it.
        drawer.addEventListener('submit', async e => {
            const form = e.target.closest('form[data-action]');
            if (!form) return;
            e.preventDefault();

            if (form.dataset.confirm && !await confirmDialog({ title: form.dataset.confirm, danger: form.hasAttribute('data-danger') })) {
                return;
            }

            const submit = form.querySelector('button[type=submit]');
            submit?.setAttribute('disabled', '');
            const result = await post(form.action, new FormData(form));
            submit?.removeAttribute('disabled');

            if (result.ok && result.data.success !== false) {
                toast(result.data.message || strings.saved);
                drawerDirty = false;
                await openDrawer(drawerUrl);
                drawerOnSaved?.(result.data);
            } else {
                toast(result.data.error || strings.error, true);
            }
        });

        drawer.addEventListener('submit', async e => {
            const form = e.target.closest('form[data-editor]');
            if (!form) return;
            e.preventDefault();

            const submit = form.querySelector('button[type=submit]');
            submit?.setAttribute('disabled', '');
            clearFormErrors(form);

            const result = await post(form.action, new FormData(form));
            submit?.removeAttribute('disabled');

            if (result.ok && result.data.success !== false) {
                drawerDirty = false;
                toast(result.data.message || strings.saved);
                if (form.dataset.returnUrl) {
                    await openDrawer(form.dataset.returnUrl);
                    drawerOnSaved?.(result.data);
                    return;
                }
                await closeDrawer(true);
                if (form.hasAttribute('data-reload')) {
                    window.location.reload();
                } else {
                    drawerOnSaved?.(result.data);
                }
                return;
            }

            showFormErrors(form, result.data);
        });
    }

    window.addEventListener('beforeunload', e => {
        if (drawerDirty) {
            e.preventDefault();
            e.returnValue = '';
        }
    });

    // Anything with data-open-editor opens its URL in the drawer.
    // Inside the drawer it replaces the current view (e.g. order → new shipment → back to the order).
    document.addEventListener('click', async e => {
        const opener = e.target.closest('[data-open-editor]');
        if (!opener) return;
        e.preventDefault();

        if (drawer?.contains(opener)) {
            if (drawerDirty && !await confirmDialog({ title: strings.unsaved, okLabel: strings.close, danger: true })) return;
            openDrawer(opener.dataset.openEditor);
        } else {
            // On a product page the drawer is the advanced editor: reload so the simple form shows the saved values.
            openDrawer(opener.dataset.openEditor, opener.hasAttribute('data-reload-on-save')
                ? () => { pageDirty = false; window.location.reload(); }
                : () => refreshGrid?.());
        }
    });

    // Settings cards: post, show field errors, keep the page (or reload to show a new title/color).
    document.addEventListener('submit', async e => {
        const form = e.target.closest('form[data-settings-form]');
        if (!form) return;
        e.preventDefault();

        clearFormErrors(form);
        const submit = form.querySelector('button[type=submit]');
        submit?.setAttribute('disabled', '');
        const result = await post(form.action, new FormData(form));
        submit?.removeAttribute('disabled');

        if (result.ok && result.data.success !== false) {
            toast(result.data.message || strings.saved);
            if (form.hasAttribute('data-reload')) setTimeout(() => window.location.reload(), 600);
        } else {
            showFormErrors(form, result.data);
        }
    });

    // Action forms on a page (outside the drawer): confirm, post, reload the page.
    document.addEventListener('submit', async e => {
        const form = e.target.closest('form[data-page-action]');
        if (!form) return;
        e.preventDefault();

        if (form.dataset.confirm && !await confirmDialog({ title: form.dataset.confirm })) return;

        const result = await post(form.action, new FormData(form));
        if (result.ok && result.data.success !== false) {
            toast(result.data.message || strings.saved);
            setTimeout(() => window.location.reload(), 600);
        } else {
            toast(result.data.error || strings.error, true);
        }
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

    const tpl = el => el?.content?.textContent.trim() ?? '';

    // Rough reading of a typed amount for client-side hints only; the server parses for real (AmountParser).
    function parseAmount(text) {
        let s = String(text || '').replace(/[\s₺]|TL/gi, '');
        if (s.includes(',')) s = s.replace(/\./g, '').replace(',', '.');
        else if (/^\d{1,3}(\.\d{3})+$/.test(s)) s = s.replace(/\./g, '');
        const n = Number(s);
        return Number.isFinite(n) ? n : 0;
    }

    // ---------- Menus ("Diğer İşlemler", row "⋯") ----------

    // A row menu sits in a scrolling table, so it is positioned fixed next to its button.
    function placeMenu(details) {
        const menu = details.querySelector('.mp-menu');
        if (!menu || !details.hasAttribute('data-menu-fixed')) return;
        const rect = details.querySelector('summary').getBoundingClientRect();
        menu.style.position = 'fixed';
        menu.style.top = Math.min(rect.bottom + 4, window.innerHeight - menu.offsetHeight - 8) + 'px';
        menu.style.left = Math.max(8, rect.right - menu.offsetWidth) + 'px';
    }

    function closeMenus(except) {
        document.querySelectorAll('details[data-menu][open]').forEach(d => {
            if (d !== except) d.open = false;
        });
    }

    document.addEventListener('toggle', e => {
        const details = e.target;
        if (!(details instanceof HTMLDetailsElement) || !details.hasAttribute('data-menu') || !details.open) return;
        closeMenus(details);
        placeMenu(details);
    }, true);

    document.addEventListener('click', e => {
        const inside = e.target.closest('details[data-menu]');
        closeMenus(inside);
        // A chosen menu entry closes its menu.
        if (inside && e.target.closest('.mp-menu__item')) inside.open = false;
    });

    document.addEventListener('keydown', e => {
        if (e.key !== 'Escape') return;
        const open = document.querySelector('details[data-menu][open]');
        if (open) {
            open.open = false;
            open.querySelector('summary')?.focus();
        }
    });

    window.addEventListener('scroll', () => closeMenus(null), { passive: true });

    // ---------- Category picker ----------

    const norm = s => String(s || '').toLocaleLowerCase('tr-TR').replace(/\s+/g, ' ').trim();

    function initCombo(root, onChange) {
        const input = root.querySelector('[data-combo-input]');
        const value = root.querySelector('[data-combo-value]');
        const list = root.querySelector('.mp-combo__list');
        const createOption = list.querySelector('[data-create]');
        const empty = list.querySelector('[data-combo-empty]');
        const createBox = root.querySelector('[data-combo-create]');
        const quickUrl = root.dataset.quickUrl;
        let active = null;

        const options = () => [...list.querySelectorAll('.mp-combo__option:not([data-create])')];
        const visible = () => [...list.querySelectorAll('.mp-combo__option:not([hidden])')];
        const selectedPath = () => options().find(o => o.dataset.id === value.value)?.dataset.path || '';

        function open() {
            list.hidden = false;
            input.setAttribute('aria-expanded', 'true');
        }

        function close() {
            list.hidden = true;
            input.setAttribute('aria-expanded', 'false');
            setActive(null);
        }

        function setActive(option) {
            active?.classList.remove('is-active');
            active = option;
            if (option) {
                option.classList.add('is-active');
                input.setAttribute('aria-activedescendant', option.id);
                option.scrollIntoView({ block: 'nearest' });
            } else {
                input.removeAttribute('aria-activedescendant');
            }
        }

        function filter() {
            const term = norm(input.value);
            let exact = false;
            let shown = 0;
            for (const o of options()) {
                const path = norm(o.dataset.path);
                const name = norm(path.split('›').pop());
                const match = term === '' || path.includes(term);
                o.hidden = !match;
                if (match) shown++;
                if (term !== '' && (path === term || name === term)) exact = true;
            }
            if (createOption) {
                createOption.hidden = !quickUrl || term === '' || exact;
                createOption.querySelector('[data-create-name]').textContent = `"${input.value.trim()}"`;
            }
            if (empty) empty.hidden = shown > 0 || (createOption && !createOption.hidden);
            setActive(null);
        }

        function choose(option) {
            if (option.hasAttribute('data-create')) {
                startCreate();
                return;
            }
            value.value = option.dataset.id;
            input.value = option.dataset.path;
            options().forEach(o => o.setAttribute('aria-selected', o === option ? 'true' : 'false'));
            close();
            onChange?.();
        }

        function startCreate() {
            if (!createBox) return;
            close();
            createBox.hidden = false;
            const name = createBox.querySelector('[data-create-input]');
            // "Kask › Yeni" typed as a path: preselect the parent when it exists.
            const parts = input.value.split('›').map(x => x.trim()).filter(Boolean);
            name.value = parts.pop() || '';
            const parentPath = norm(parts.join(' › '));
            const parent = options().find(o => norm(o.dataset.path) === parentPath);
            createBox.querySelector('[data-create-parent]').value = parent ? parent.dataset.id : '0';
            createBox.querySelector('[data-create-error]').textContent = '';
            name.focus();
        }

        input.addEventListener('focus', () => { filter(); open(); });
        input.addEventListener('input', () => { filter(); open(); });
        input.addEventListener('keydown', e => {
            const items = visible();
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                e.preventDefault();
                open();
                const i = items.indexOf(active);
                const next = e.key === 'ArrowDown' ? items[Math.min(items.length - 1, i + 1)] : items[Math.max(0, i - 1)];
                setActive(next || null);
            } else if (e.key === 'Enter') {
                if (!list.hidden && (active || items.length === 1)) {
                    e.preventDefault();
                    choose(active || items[0]);
                }
            } else if (e.key === 'Escape' && !list.hidden) {
                e.preventDefault();
                e.stopPropagation();
                input.value = selectedPath();
                close();
            }
        });

        input.addEventListener('blur', () => {
            // Let a click on an option land first.
            setTimeout(() => {
                if (root.contains(document.activeElement) && document.activeElement !== input) return;
                if (input.value.trim() === '') {
                    value.value = '0';
                    if (!createBox || createBox.hidden) onChange?.();
                } else if (input.value !== selectedPath()) {
                    input.value = selectedPath();
                }
                close();
            }, 150);
        });

        list.addEventListener('mousedown', e => e.preventDefault());
        list.addEventListener('click', e => {
            const option = e.target.closest('.mp-combo__option');
            if (option) choose(option);
        });

        if (!createBox) return;

        createBox.querySelector('[data-create-cancel]').addEventListener('click', () => {
            createBox.hidden = true;
            input.value = selectedPath();
            input.focus();
        });

        createBox.querySelector('[data-create-input]').addEventListener('keydown', e => {
            if (e.key === 'Enter') {
                e.preventDefault();
                createBox.querySelector('[data-create-save]').click();
            }
        });

        createBox.querySelector('[data-create-save]').addEventListener('click', async () => {
            const name = createBox.querySelector('[data-create-input]').value.trim();
            const error = createBox.querySelector('[data-create-error]');
            const button = createBox.querySelector('[data-create-save]');
            if (!name) {
                error.textContent = strings.categoryNameRequired;
                return;
            }

            const body = new FormData();
            body.append('name', name);
            body.append('parentId', createBox.querySelector('[data-create-parent]').value);
            button.disabled = true;
            const result = await post(quickUrl, body);
            button.disabled = false;

            if (!result.ok || !result.data.success) {
                error.textContent = result.data.error || strings.error;
                return;
            }

            const c = result.data.category;
            let option = options().find(o => o.dataset.id === String(c.id));
            if (!option) {
                option = document.createElement('li');
                option.className = 'mp-combo__option';
                option.setAttribute('role', 'option');
                option.id = `mp-cat-${c.id}`;
                option.dataset.id = c.id;
                option.dataset.path = c.path;
                option.textContent = c.path;
                list.insertBefore(option, createOption);
                const parentOption = document.createElement('option');
                parentOption.value = c.id;
                parentOption.textContent = c.path;
                createBox.querySelector('[data-create-parent]').appendChild(parentOption);
            }
            createBox.hidden = true;
            choose(option);
            toast(result.data.message);
            input.focus();
        });
    }

    // ---------- Photos ----------

    function initPhotos(root, getVersion, setVersion) {
        const mode = root.dataset.mode;
        const listEl = root.querySelector('[data-photo-list]');
        const input = root.querySelector('[data-photo-input]');
        const drop = root.querySelector('[data-photo-drop]');
        const errorEl = root.querySelector('[data-error-for="Photos"]');
        const max = Number(root.dataset.max || 10);
        const maxBytes = Number(root.dataset.maxBytes || 10485760);
        const types = ['image/jpeg', 'image/png', 'image/webp', 'image/gif'];
        let items = JSON.parse(root.dataset.initial || '[]');
        let busy = false;

        const setError = text => { errorEl.textContent = text || ''; };

        function button(label, action, extraClass = '') {
            const b = document.createElement('button');
            b.type = 'button';
            b.className = 'mp-photo__btn ' + extraClass;
            b.dataset.action = action;
            b.textContent = label;
            return b;
        }

        function render() {
            listEl.replaceChildren(...items.map((item, i) => {
                const li = document.createElement('li');
                li.className = 'mp-photo' + (i === 0 ? ' is-main' : '') + (item.uploading ? ' is-busy' : '');
                li.dataset.index = i;
                const img = document.createElement('img');
                img.src = item.url;
                img.alt = item.name || '';
                img.width = 120;
                img.height = 120;
                li.appendChild(img);
                if (i === 0) {
                    const badge = document.createElement('span');
                    badge.className = 'mp-photo__main';
                    badge.textContent = strings.photoMain;
                    li.appendChild(badge);
                }
                const tools = document.createElement('div');
                tools.className = 'mp-photo__tools';
                if (i > 0) tools.appendChild(button(strings.photoMakeMain, 'main'));
                if (i > 0) tools.appendChild(button('←', 'left')).setAttribute('aria-label', strings.photoLeft);
                if (i < items.length - 1) tools.appendChild(button('→', 'right')).setAttribute('aria-label', strings.photoRight);
                tools.appendChild(button(strings.photoRemove, 'remove', 'mp-photo__btn--danger'));
                li.appendChild(tools);
                return li;
            }));
            drop.hidden = items.length >= max;
            root.classList.toggle('is-busy', busy);
        }

        async function send(url, body) {
            busy = true;
            render();
            body.append('version', getVersion());
            const result = await post(url, body);
            busy = false;
            if (result.ok && result.data.success) {
                items = (result.data.photos || []).map(p => ({ id: p.fileId, url: p.thumbUrl, name: p.name }));
                setVersion(result.data.version);
                setError('');
                toast(result.data.message);
            } else {
                setError(result.data.errors?.Photos || result.data.errors?.[''] || result.data.error || strings.photoFailed);
                toast(strings.photoFailed, true);
            }
            render();
            return result.ok;
        }

        // Same content check as the server (ProductPhotoRules): a renamed PDF is not a photo.
        async function isImage(file) {
            let h;
            try {
                h = new Uint8Array(await file.slice(0, 12).arrayBuffer());
            } catch {
                return false;
            }
            const ascii = (from, to) => String.fromCharCode(...h.slice(from, to));
            return (h[0] === 0xFF && h[1] === 0xD8 && h[2] === 0xFF)
                || (h[0] === 0x89 && ascii(1, 4) === 'PNG')
                || (ascii(0, 4) === 'RIFF' && ascii(8, 12) === 'WEBP')
                || ascii(0, 6) === 'GIF87a' || ascii(0, 6) === 'GIF89a';
        }

        async function add(files) {
            setError('');
            const accepted = [];
            for (const file of files) {
                if (!types.includes(file.type) || !await isImage(file)) { setError(fmt(strings.photoType, file.name)); continue; }
                if (file.size > maxBytes) { setError(fmt(strings.photoSize, file.name, Math.round(maxBytes / 1048576))); continue; }
                if (items.length + accepted.length >= max) { setError(fmt(strings.photoMax, max)); break; }
                accepted.push(file);
            }
            if (accepted.length === 0) return;

            if (mode === 'create') {
                accepted.forEach(file => items.push({ file, url: URL.createObjectURL(file), name: file.name }));
                render();
                root.dispatchEvent(new Event('input', { bubbles: true }));
                return;
            }

            const body = new FormData();
            accepted.forEach(file => body.append('photos', file, file.name));
            await send(root.dataset.uploadUrl, body);
        }

        async function reorder(next) {
            if (mode === 'create') {
                items = next;
                render();
                root.dispatchEvent(new Event('input', { bubbles: true }));
                return;
            }
            const body = new FormData();
            next.forEach(item => body.append('fileIds', item.id));
            await send(root.dataset.orderUrl, body);
        }

        listEl.addEventListener('click', async e => {
            const b = e.target.closest('button[data-action]');
            if (!b || busy) return;
            const i = Number(b.closest('li').dataset.index);
            const next = [...items];
            switch (b.dataset.action) {
                case 'main':
                    next.unshift(...next.splice(i, 1));
                    await reorder(next);
                    break;
                case 'left':
                    [next[i - 1], next[i]] = [next[i], next[i - 1]];
                    await reorder(next);
                    break;
                case 'right':
                    [next[i + 1], next[i]] = [next[i], next[i + 1]];
                    await reorder(next);
                    break;
                case 'remove':
                    if (mode === 'create') {
                        URL.revokeObjectURL(items[i].url);
                        items.splice(i, 1);
                        render();
                        root.dispatchEvent(new Event('input', { bubbles: true }));
                    } else if (await confirmDialog({ title: strings.photoRemoveConfirm, danger: true, okLabel: strings.photoRemove })) {
                        await send(root.dataset.removeUrl.replace(/\/0\/remove/, `/${items[i].id}/remove`), new FormData());
                    }
                    break;
            }
            listEl.querySelector(`li[data-index="${Math.min(i, items.length - 1)}"] button`)?.focus();
        });

        input.addEventListener('change', () => {
            add([...input.files]);
            input.value = '';
        });

        ['dragenter', 'dragover'].forEach(type => drop.addEventListener(type, e => {
            e.preventDefault();
            drop.classList.add('is-over');
        }));
        ['dragleave', 'drop'].forEach(type => drop.addEventListener(type, () => drop.classList.remove('is-over')));
        drop.addEventListener('drop', e => {
            e.preventDefault();
            add([...(e.dataTransfer?.files || [])]);
        });

        render();
        return { files: () => items.filter(x => x.file).map(x => x.file), setError };
    }

    // ---------- Simple product forms (new product, simple editor) ----------

    let pageDirty = false;
    window.addEventListener('beforeunload', e => {
        if (pageDirty) {
            e.preventDefault();
            e.returnValue = '';
        }
    });

    document.querySelectorAll('form[data-simple-form]').forEach(form => {
        const isCreate = form.dataset.simpleForm === 'create';
        const versionInput = form.querySelector('[data-version]');
        const markDirty = () => { pageDirty = true; };

        form.querySelectorAll('[data-combo]').forEach(root => initCombo(root, markDirty));

        // "Fiyat sitede görünsün": a hidden price that only stood in for a missing one is shown as soon as a
        // price is typed (the same rule as the supplier import); the employee sees why and can untick it.
        const showPrice = form.querySelector('[data-show-price]');
        const priceInput = form.querySelector('input[name=Price]');
        const syncPriceNote = () => {
            const hiddenNote = form.querySelector('[data-price-hidden-note]');
            if (hiddenNote && showPrice) hiddenNote.hidden = showPrice.checked;
        };
        if (showPrice && priceInput) {
            showPrice.addEventListener('change', () => {
                form.querySelector('[data-price-auto-note]').hidden = true;
                syncPriceNote();
            });
            priceInput.addEventListener('input', () => {
                if (!showPrice.checked && parseAmount(priceInput.dataset.originalPrice) <= 0 && parseAmount(priceInput.value) > 0) {
                    showPrice.checked = true;
                    form.querySelector('[data-price-auto-note]').hidden = false;
                    syncPriceNote();
                }
            });
        }

        const photosRoot = form.querySelector('[data-photos]');
        const photos = photosRoot
            ? initPhotos(photosRoot, () => versionInput?.value ?? '', v => { if (versionInput && v) versionInput.value = v; })
            : null;

        form.addEventListener('input', e => {
            // A corrected field loses its old error message right away.
            const field = e.target.name || (e.target.matches?.('[data-combo-input]') ? 'CategoryId' : null);
            if (field) {
                const slot = form.querySelector(`[data-error-for="${CSS.escape(field)}"]`);
                if (slot) slot.textContent = '';
                e.target.removeAttribute('aria-invalid');
            }

            // Photo changes of an existing product are saved at once.
            if (!isCreate && e.target.closest?.('[data-photos]')) return;
            markDirty();
        });
        form.addEventListener('change', e => {
            if (e.target.matches('[data-photo-input]')) return;
            markDirty();
        });

        form.querySelector('[data-cancel]')?.addEventListener('click', async e => {
            if (!pageDirty) return;
            e.preventDefault();
            if (await confirmDialog({ title: strings.unsaved, okLabel: strings.leave, danger: true })) {
                pageDirty = false;
                window.location.href = e.currentTarget.href;
            }
        });

        let sending = false;
        form.addEventListener('submit', async e => {
            e.preventDefault();
            if (sending) return;

            const submitter = e.submitter || form.querySelector('button[type=submit]');

            // A field submitted with Enter fires its "change" only when it loses focus, which would mark the page
            // dirty again after a successful save. Let it fire now.
            if (form.contains(document.activeElement)) document.activeElement.blur();

            if (isCreate) {
                form.querySelector('[data-publish-field]').value = submitter?.dataset.publish === 'true' ? 'true' : 'false';
            }

            clearFormErrors(form);
            const body = new FormData(form);
            photos?.files().forEach(file => body.append('photos', file, file.name));

            const buttons = [...form.querySelectorAll('button[type=submit]')];
            const label = submitter?.textContent;
            sending = true;
            buttons.forEach(b => (b.disabled = true));
            if (submitter) submitter.textContent = strings.saving;

            const result = await post(form.action, body);

            sending = false;
            buttons.forEach(b => (b.disabled = false));
            if (submitter) submitter.textContent = label;

            const data = result.data || {};
            if (!result.ok || !data.success) {
                if (result.status === 413) {
                    data.errors = { Photos: strings.photoTooLargeRequest };
                }
                showFormErrors(form, { errors: data.errors && Object.keys(data.errors).length ? data.errors : { '': data.error || strings.error } });
                if (data.conflict) {
                    const box = form.querySelector('[data-form-error]');
                    const reload = document.createElement('button');
                    reload.type = 'button';
                    reload.className = 'mp-btn mp-btn--outline mp-btn--sm mp-reload';
                    reload.textContent = strings.reload;
                    reload.addEventListener('click', () => { pageDirty = false; window.location.reload(); });
                    box.append(' ', reload);
                }
                toast(data.errors?.[''] || strings.checkForm, true);
                return;
            }

            pageDirty = false;
            const productUrl = slug => (form.dataset.productUrlTemplate || tpl(document.querySelector('template[data-product-url-template]')))
                .replace('__slug__', encodeURIComponent(slug));

            if (isCreate) {
                const done = document.querySelector('[data-create-done]');
                done.querySelector('[data-done-title]').textContent = data.message;
                done.querySelector('[data-done-text]').textContent = tpl(done.querySelector(data.published ? '[data-text-published]' : '[data-text-draft]'));
                const view = done.querySelector('[data-done-view]');
                view.hidden = !data.slug;
                if (data.slug) view.href = productUrl(data.slug);
                done.querySelector('[data-done-edit]').href = tpl(done.querySelector('[data-edit-url-template]')).replace(/\/0(\/?)$/, `/${data.productId}$1`);
                form.hidden = true;
                done.hidden = false;
                done.focus();
                window.scrollTo({ top: 0 });
                return;
            }

            toast(data.message || strings.saved);
            if (versionInput) versionInput.value = data.version;
            form.querySelector('[data-title]').textContent = data.productName;
            const badge = form.querySelector('[data-state-badge]');
            badge.textContent = data.published ? badge.dataset.labelTrue : badge.dataset.labelFalse;
            badge.classList.toggle('mp-badge--green', data.published);
            badge.classList.toggle('mp-badge--gray', !data.published);
            const note = form.querySelector('[data-draft-note]');
            if (note) note.hidden = data.published;
            if (data.slug) form.querySelectorAll('[data-view-site]').forEach(a => (a.href = productUrl(data.slug)));
            if (showPrice) {
                showPrice.checked = !data.callForPrice;
                form.querySelector('[data-price-auto-note]').hidden = true;
                syncPriceNote();
            }
            if (priceInput) priceInput.dataset.originalPrice = priceInput.value;
            if (data.warning) {
                toast(data.warning, true);
                const box = form.querySelector('[data-form-error]');
                box.textContent = data.warning;
                box.hidden = false;
            }
        });
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
                el.value = value ?? el.options[0]?.value ?? '';
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
                const anchor = grid.closest('section')?.id;
                history.pushState({ mp: qs }, '', pageUrl + (qs ? '?' + qs : '') + (anchor ? '#' + anchor : ''));
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
                // Entering a price for a hidden, unpriced product shows the price (server rule).
                const cfp = row.querySelector('[data-toggle="callForPrice"]');
                if (cfp && result.data.callForPrice === false && cfp.dataset.value === 'true') {
                    cfp.dataset.value = 'false';
                    cfp.textContent = cfp.dataset.labelFalse;
                    cfp.classList.replace(cfp.dataset.classTrue, cfp.dataset.classFalse);
                    toast(strings.priceShown);
                }
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
