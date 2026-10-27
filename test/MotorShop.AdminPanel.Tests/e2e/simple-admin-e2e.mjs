// End-to-end check of the simple admin (/motor-panel) against a RUNNING store, in a real headless browser.
// Never point it at production: it creates, publishes, edits and finally deletes disposable test products.
//
//   node simple-admin-e2e.mjs --base http://127.0.0.1:5111 --credentials <file with e-mail and password lines>
//        [--photo <real jpg>] [--out <screenshot dir>] [--browser <path to msedge/chrome>] [--customer <credentials file of a non-admin>]
//
// Covers: login, dashboard, product list, "Yeni Ürün Ekle" (name, searchable category, quick category, Turkish price,
// stock, photos), draft not visible to customers, publish, "Sitede Gör", price change (form and inline) reaching the
// storefront, category change, main photo change, concurrent edit, validation and security checks, console errors.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import zlib from 'node:zlib';

const args = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, all) => (a.startsWith('--') ? [...acc, [a.slice(2), all[i + 1]]] : acc), []));
const base = (args.base || 'http://127.0.0.1:5111').replace(/\/$/, '');
const [email, password] = fs.readFileSync(args.credentials, 'utf8').split(/\r?\n/);
const outDir = args.out || path.join(os.tmpdir(), 'motor-panel-e2e');
const browserPath = args.browser || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
fs.mkdirSync(outDir, { recursive: true });

const stamp = Date.now().toString(36).toUpperCase();
const results = [];
const problems = [];
const check = (name, ok, detail = '') => {
    results.push({ name, ok, detail });
    console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  — ' + detail : ''}`);
};

// ---------- Test files ----------

function png(width, height, rgb) {
    const crc = (buf) => {
        let c, crc = 0xffffffff;
        for (const b of buf) {
            c = (crc ^ b) & 0xff;
            for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
            crc = (crc >>> 8) ^ c;
        }
        return (crc ^ 0xffffffff) >>> 0;
    };
    const chunk = (type, data) => {
        const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
        const td = Buffer.concat([Buffer.from(type), data]);
        const c = Buffer.alloc(4); c.writeUInt32BE(crc(td));
        return Buffer.concat([len, td, c]);
    };
    const ihdr = Buffer.alloc(13);
    ihdr.writeUInt32BE(width, 0); ihdr.writeUInt32BE(height, 4); ihdr[8] = 8; ihdr[9] = 2;
    const row = Buffer.alloc(1 + width * 3);
    for (let x = 0; x < width; x++) { row[1 + x * 3] = rgb[0]; row[2 + x * 3] = rgb[1]; row[3 + x * 3] = rgb[2]; }
    const raw = Buffer.concat(Array.from({ length: height }, () => row));
    return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}

const files = {
    orange: path.join(outDir, `qa-orange-${stamp}.png`),
    blue: path.join(outDir, `qa-blue-${stamp}.png`),
    fake: path.join(outDir, `qa-fake-${stamp}.jpg`)
};
fs.writeFileSync(files.orange, png(640, 480, [255, 90, 31]));
fs.writeFileSync(files.blue, png(640, 480, [47, 95, 208]));
fs.writeFileSync(files.fake, '%PDF-1.7 not a photo');
const photo = args.photo && fs.existsSync(args.photo) ? args.photo : files.blue;

// ---------- Browser over CDP ----------

const port = 9300 + Math.floor(Math.random() * 500);
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'mp-e2e-'));
const browser = spawn(browserPath, [`--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, '--headless=new', '--window-size=1440,1000', '--no-first-run', '--disable-gpu', '--disable-extensions', 'about:blank'], { stdio: 'ignore' });

async function waitJson(url, tries = 50) {
    for (let i = 0; i < tries; i++) {
        try { return await (await fetch(url)).json(); } catch { await new Promise(r => setTimeout(r, 200)); }
    }
    throw new Error('Browser did not start');
}

const targets = await waitJson(`http://127.0.0.1:${port}/json/list`);
const ws = new WebSocket(targets.find(t => t.type === 'page').webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener('open', r, { once: true }));
let seq = 0;
const pending = new Map();
const listeners = [];
ws.addEventListener('message', e => {
    const msg = JSON.parse(e.data);
    if (msg.id && pending.has(msg.id)) {
        const { resolve, reject } = pending.get(msg.id);
        pending.delete(msg.id);
        msg.error ? reject(new Error(msg.error.message)) : resolve(msg.result);
    } else if (msg.method) {
        listeners.forEach(l => l(msg));
    }
});
const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++seq;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 45000);
    pending.set(id, { resolve: v => { clearTimeout(timer); resolve(v); }, reject: e => { clearTimeout(timer); reject(e); } });
    ws.send(JSON.stringify({ id, method, params }));
});

await send('Page.enable');
await send('Runtime.enable');
await send('Network.enable');
await send('DOM.enable');
// Smartstore's bot detection refuses the HeadlessEdge user agent.
await send('Network.setUserAgentOverride', { userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36 Edg/141.0.0.0' });
await send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false });

const urls = new Map();
listeners.push(msg => {
    if (msg.method === 'Runtime.exceptionThrown') {
        const d = msg.params.exceptionDetails;
        problems.push(`JS exception: ${d.text} ${d.exception?.description?.split('\n')[0] ?? JSON.stringify(d.exception?.value ?? d.exception?.preview ?? '')} at ${d.url ?? ''}:${d.lineNumber}`);
    }
    if (msg.method === 'Runtime.consoleAPICalled' && msg.params.type === 'error') problems.push('console.error: ' + msg.params.args.map(a => a.value ?? a.description).join(' '));
    if (msg.method === 'Network.requestWillBeSent') urls.set(msg.params.requestId, msg.params.request.url);
    if (msg.method === 'Page.javascriptDialogOpening') {
        // An unexpected dialog (e.g. "leave page?") would block the page; record it and accept.
        problems.push(`dialog (${msg.params.type}) on ${msg.params.url}: ${msg.params.message}`);
        send('Page.handleJavaScriptDialog', { accept: true }).catch(() => { });
    }
    if (msg.method === 'Network.loadingFailed' && !msg.params.canceled) problems.push('network failed: ' + urls.get(msg.params.requestId) + ' ' + msg.params.errorText);
    if (msg.method === 'Network.responseReceived' && msg.params.response.status >= 500) problems.push(`HTTP ${msg.params.response.status}: ${msg.params.response.url}`);
});

const sleep = ms => new Promise(r => setTimeout(r, ms));
async function evaluate(expression) {
    const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
}
async function waitFor(expression, timeout = 15000) {
    const end = Date.now() + timeout;
    while (Date.now() < end) {
        try { if (await evaluate(expression)) return true; } catch { /* page loading */ }
        await sleep(150);
    }
    throw new Error('Timeout: ' + expression);
}
// Waits for the DOM, not for every third-party resource of the storefront (analytics may never finish offline).
async function go(url) {
    let listener;
    const ready = new Promise(r => {
        listener = m => { if (m.method === 'Page.domContentEventFired') r(); };
        listeners.push(listener);
    });
    await send('Page.navigate', { url: url.startsWith('http') ? url : base + url });
    await Promise.race([ready, sleep(20000)]);
    listeners.splice(listeners.indexOf(listener), 1);
    await sleep(500);
}
async function shot(name) {
    // Full-page capture: a sticky footer would otherwise sit in the middle of the image.
    await evaluate(`(() => { const s = document.createElement('style'); s.id = '__shot'; s.textContent = '.mp-simple__foot{position:static!important}'; document.head.append(s); return true; })()`).catch(() => { });
    const { data } = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
    const file = path.join(outDir, `${name}.png`);
    fs.writeFileSync(file, Buffer.from(data, 'base64'));
    await evaluate(`document.getElementById('__shot')?.remove(), true`).catch(() => { });
    return file;
}
async function type(selector, text) {
    await evaluate(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); el.focus(); el.select?.(); return true; })()`);
    await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'a', code: 'KeyA', modifiers: 2 });
    await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'a', code: 'KeyA', modifiers: 2 });
    await send('Input.insertText', { text });
}
async function setFiles(selector, paths) {
    const { root } = await send('DOM.getDocument');
    const { nodeId } = await send('DOM.querySelector', { nodeId: root.nodeId, selector });
    await send('DOM.setFileInputFiles', { nodeId, files: paths });
}
// Submits the simple editor and waits until the save answered: a new toast on success, or an error.
async function saveForm() {
    // Toasts disappear after a few seconds, so count the ones added from now on.
    await evaluate(`(() => {
        window.__okToasts = 0;
        window.__toastObserver?.disconnect();
        window.__toastObserver = new MutationObserver(list => list.forEach(r => r.addedNodes.forEach(n => { if (!n.classList?.contains('mp-toast--error')) window.__okToasts++; })));
        window.__toastObserver.observe(document.querySelector('.mp-toasts'), { childList: true });
        return true;
    })()`);
    await click('form[data-simple-form] button[type=submit]');
    await waitFor(`window.__okToasts > 0 || !document.querySelector('[data-form-error]').hidden`, 30000);
    return evaluate(`document.querySelector('[data-form-error]').hidden`);
}
const click = selector => evaluate(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); if (!el) throw new Error('missing ${selector.replace(/'/g, '')}'); el.click(); return true; })()`);
const text = selector => evaluate(`document.querySelector(${JSON.stringify(selector)})?.textContent.trim() ?? null`);

// Anonymous storefront requests (no cookies): what a customer sees.
const customerHeaders = { 'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/141.0 Safari/537.36', 'Sec-Fetch-Mode': 'navigate', 'Sec-Fetch-Dest': 'document', 'Sec-Fetch-Site': 'none', 'Accept': 'text/html' };
const anonymous = async (p, init = {}) => fetch(base + p, { redirect: 'manual', ...init, headers: { ...customerHeaders, ...(init.headers || {}) } });

// Panel request with the browser's session (antiforgery token from the page).
const panelPost = (url, body, withToken = true) => evaluate(`(async () => {
    const fd = new FormData();
    for (const [k, v] of ${JSON.stringify(Object.entries(body))}) fd.append(k, v);
    const headers = { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' };
    if (${withToken}) headers['X-XSRF-Token'] = document.querySelector('meta[name="mp-xsrf"]').content;
    const r = await fetch(${JSON.stringify(url)}, { method: 'POST', body: fd, headers, credentials: 'same-origin' });
    let data = null; try { data = await r.json(); } catch {}
    return { status: r.status, data };
})()`);

const created = [];
let exitCode = 0;

try {
    // ---------- Security before login ----------
    const anonPanel = await anonymous('/motor-panel');
    check('Anonymous /motor-panel redirects to login', anonPanel.status === 302 && /login/i.test(anonPanel.headers.get('location') || ''), String(anonPanel.status));
    const anonPost = await anonymous('/motor-panel/product/create', { method: 'POST', headers: { 'X-Requested-With': 'XMLHttpRequest' }, body: new URLSearchParams({ Name: 'x' }) });
    check('Anonymous product create is refused', [302, 400, 401, 403].includes(anonPost.status), String(anonPost.status));

    // ---------- Login ----------
    await go('/login?returnUrl=%2Fmotor-panel');
    await evaluate(`(() => { const f = document.querySelector('[name=UsernameOrEmail]').form; f.UsernameOrEmail.value = ${JSON.stringify(email)}; f.Password.value = ${JSON.stringify(password)}; f.requestSubmit(); return true; })()`);
    await waitFor(`location.pathname.startsWith('/motor-panel') && !!document.querySelector('.mp-cards')`);
    check('Admin login opens the dashboard', true);

    // The storefront "Yönetim" link (top bar, wide screens) opens the simple panel, not the classic admin.
    await go('/');
    const adminLink = await evaluate(`document.querySelector('.menubar-group--special a.menubar-link')?.getAttribute('href')`);
    check('Storefront "Yönetim" link opens /motor-panel', /^\/motor-panel\/?$/.test(adminLink || ''), adminLink);
    await go('/motor-panel');

    // ---------- Dashboard ----------
    const tabs = await evaluate(`[...document.querySelectorAll('.mp-tabs__list .mp-tabs__link')].map(x => x.textContent.trim())`);
    check('Main navigation: Genel Bakış, Ürünler, Kategoriler, Siparişler, Kargolar', JSON.stringify(tabs) === JSON.stringify(['Genel Bakış', 'Ürünler', 'Kategoriler', 'Siparişler', 'Kargolar']), tabs.join(' | '));
    const more = await evaluate(`[...document.querySelectorAll('.mp-more .mp-menu__item')].map(x => x.textContent.trim().replace(/\\s+/g, ' '))`);
    check('Rare pages under "Diğer İşlemler"', more.length >= 6, more.join(' | '));
    const cards = await evaluate(`document.querySelectorAll('.mp-cards .mp-card').length`);
    check('Dashboard cards are intact', cards === 4, String(cards));
    await shot('01-dashboard');

    // ---------- Product list ----------
    await go('/motor-panel/products');
    const headers = await evaluate(`[...document.querySelectorAll('.mp-table--products thead th')].map(x => x.textContent.trim()).filter(Boolean)`);
    check('List columns Ürün | Fiyat | Stok | Kategori | Durum | İşlemler', headers.join('|').toLocaleUpperCase('tr') === 'ÜRÜN|FİYAT|STOK|KATEGORİ|DURUM|İŞLEMLER', headers.join(' | '));
    await shot('02-product-list');

    // ---------- New product: validation first ----------
    await go('/motor-panel/product/new');
    await click('button[data-publish="true"]');
    await waitFor(`!!document.querySelector('[data-error-for="Name"]').textContent`);
    const nameError = await text('[data-error-for="Name"]');
    const categoryError = await text('[data-error-for="CategoryId"]');
    check('Empty form shows Turkish errors', /Ürün adını yazmalısın/.test(nameError) && /kategori seç/i.test(categoryError), `${nameError} / ${categoryError}`);

    // Wrong file type is refused in the browser...
    await setFiles('[data-photo-input]', [files.fake]);
    await sleep(200);
    const typeError = await text('[data-error-for="Photos"]');
    check('Wrong photo type is refused in the form', /fotoğraf değil/i.test(typeError), typeError);

    // ---------- New product: fill in ----------
    const name = `QA Test Kaskı ${stamp}`;
    await type('input[name=Name]', name);
    const categories = await evaluate(`[...document.querySelectorAll('.mp-combo__option[data-id]')].map(x => ({ id: x.dataset.id, path: x.dataset.path }))`);
    const leaf = categories.filter(c => c.path.includes('›'));
    const [firstCategory, secondCategory] = leaf.length >= 2 ? leaf : categories;
    await type('#mp-category-input', firstCategory.path.split('›').pop().trim().slice(0, 4));
    await waitFor(`!document.querySelector('#mp-category-list').hidden`);
    await shot('04-category-selection');
    await evaluate(`document.querySelector('.mp-combo__option[data-id="${firstCategory.id}"]').click(), true`);
    check('Category picked from the searchable list', (await evaluate(`document.querySelector('[data-combo-value]').value`)) === firstCategory.id, firstCategory.path);

    // Quick category: a new one, then the same name again returns the existing one.
    const quickName = `QA Kategori ${stamp}`;
    const quick1 = await panelPost('/motor-panel/categories/quick', { name: quickName, parentId: '0' });
    const quick2 = await panelPost('/motor-panel/categories/quick', { name: quickName.toLocaleUpperCase('tr'), parentId: '0' });
    check('Quick category is created once (no duplicate)', quick1.data?.created === true && quick2.data?.created === false && quick2.data?.category?.id === quick1.data?.category?.id, quick2.data?.message);

    await type('input[name=Price]', '1.250,50');
    await type('input[name=Stock]', '4');
    await type('textarea[name=ShortDescription]', 'QA testi için geçici ürün. Silinecek.');
    await setFiles('[data-photo-input]', [files.orange, photo]);
    await waitFor(`document.querySelectorAll('.mp-photo').length === 2`);
    check('Two photo thumbnails, first marked as main', (await evaluate(`document.querySelector('.mp-photo.is-main .mp-photo__main') != null`)));
    await shot('03-new-product-form');

    // ---------- Save as draft ----------
    await click('button[data-publish="false"]');
    await waitFor(`!document.querySelector('[data-create-done]').hidden`, 30000);
    const editHref = await evaluate(`document.querySelector('[data-done-edit]').getAttribute('href')`);
    const productId = Number((editHref.match(/(\d+)\/?$/) || [])[1]);
    const siteHref = await evaluate(`document.querySelector('[data-done-view]').getAttribute('href')`);
    created.push(productId);
    check('Draft saved with confirmation and "Sitede Gör"', productId > 0 && !!siteHref, `#${productId} ${siteHref}`);
    await shot('05-draft-saved');

    // Double submission of the same form must not create a twin.
    const submissionId = await evaluate(`document.querySelector('[name=SubmissionId]').value`);
    const twin = await panelPost('/motor-panel/product/create', { SubmissionId: submissionId, Name: name, CategoryId: firstCategory.id, Price: '1.250,50', Stock: '4', Publish: 'false' });
    check('Same form sent twice returns the same product', twin.status === 200 && twin.data?.productId === productId, `${twin.status} #${twin.data?.productId}`);

    const draftForCustomer = await anonymous(siteHref);
    check('Draft is not visible to customers (404)', draftForCustomer.status === 404, String(draftForCustomer.status));

    await go(siteHref);
    const notice = await text('.mp-draft-notice');
    check('Admin preview shows the draft notice', /taslak/i.test(notice || ''), notice);
    await shot('06-storefront-draft-preview');

    // ---------- Simple editor: publish ----------
    await go(editHref);
    await shot('07-edit-form');
    await evaluate(`document.querySelector('input[name=Published][value=true]').click(), true`);
    const published = await saveForm();
    const toast = await evaluate(`[...document.querySelectorAll('.mp-toast')].map(t => t.textContent).join(' ')`);
    check('Publish from the editor: "Değişiklikler kaydedildi."', published && /Değişiklikler kaydedildi/.test(toast), toast);

    let page = await anonymous(siteHref);
    let html = await page.text();
    check('Published product is visible to customers', page.status === 200 && html.includes(name), String(page.status));
    check('Storefront shows the price 1.250,50', html.includes('1.250,50'));

    // ---------- Change price in the editor ----------
    await type('input[name=Price]', '1.399,90');
    check('Price change saved in the editor', await saveForm(), await text('[data-form-error]'));
    html = await (await anonymous(siteHref)).text();
    check('New price 1.399,90 reaches the storefront', html.includes('1.399,90') && !html.includes('1.250,50'));
    check('Draft note is hidden once published', await evaluate(`document.querySelector('[data-draft-note]').hidden`));
    await go(siteHref);
    check('Published page has no draft notice', (await evaluate(`document.querySelector('.mp-draft-notice') == null`)));
    await shot('07b-storefront-published');

    // ---------- Change category ----------
    await go(editHref);
    await type('#mp-category-input', secondCategory.path.split('›').pop().trim());
    await waitFor(`!document.querySelector('#mp-category-list').hidden`);
    await evaluate(`document.querySelector('.mp-combo__option[data-id="${secondCategory.id}"]').click(), true`);
    check('Category change saved in the editor', await saveForm());
    await go(`/motor-panel/products?categoryId=${secondCategory.id}&q=${encodeURIComponent(stamp)}`);
    const inNew = await evaluate(`document.querySelectorAll('tr[data-id="${productId}"]').length`);
    await go(`/motor-panel/products?categoryId=${firstCategory.id}&q=${encodeURIComponent(stamp)}`);
    const inOld = await evaluate(`document.querySelectorAll('tr[data-id="${productId}"]').length`);
    check('Category change: listed under the new category only', inNew === 1 && inOld === 0, `${secondCategory.path}`);
    page = await anonymous(siteHref);
    html = await page.text();
    const breadcrumbOk = html.includes(secondCategory.path.split('›').pop().trim().replace(/&/g, '&amp;'));
    check('Storefront product page names the new category', breadcrumbOk, secondCategory.path);

    // ---------- Inline price and stock from the list ----------
    await go(`/motor-panel/products?q=${encodeURIComponent(stamp)}`);
    await shot('08-list-with-test-product');
    await evaluate(`document.querySelector('tr[data-id="${productId}"] [data-edit=price]').click(), true`);
    await type(`tr[data-id="${productId}"] .mp-cell-input`, '1.450,00');
    await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Enter', code: 'Enter', windowsVirtualKeyCode: 13 });
    await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Enter', code: 'Enter', windowsVirtualKeyCode: 13 });
    await waitFor(`document.querySelector('tr[data-id="${productId}"] [data-edit=price]').dataset.value === '1450'`);
    html = await (await anonymous(siteHref)).text();
    check('Inline list price 1.450,00 reaches the storefront without reload', html.includes('1.450,00'));

    // Inline conflict: the value the user saw is stale.
    const conflict = await panelPost(`/motor-panel/product/${productId}/field`, { field: 'stock', value: '9', expected: '123' });
    check('Inline conflict answers 409 with the current value', conflict.status === 409 && conflict.data?.value === '4', `${conflict.status} current=${conflict.data?.value}`);

    // ---------- Photos: make the second photo the main one ----------
    await go(editHref);
    const before = await evaluate(`[...document.querySelectorAll('.mp-photo img')].map(i => i.src)`);
    await evaluate(`document.querySelector('.mp-photo:nth-child(2) [data-action=main]').click(), true`);
    await waitFor(`document.querySelector('.mp-photo.is-main img')?.src === ${JSON.stringify(before[1])}`);
    check('Second photo became the main photo', true);
    // The new main photo is the first gallery image on the storefront.
    const mainFileId = (before[1].match(/\/(\d+)\//) || [])[1];
    html = await (await anonymous(siteHref)).text();
    check('Storefront uses the new main photo', !!mainFileId && new RegExp(`/media/0*${mainFileId}/`).test(html), `file ${mainFileId}`);

    // ---------- Concurrent edit in the simple editor ----------
    await go(editHref);
    await panelPost(`/motor-panel/product/${productId}/field`, { field: 'stock', value: '6', expected: '4' });
    await type('input[name=Name]', name + ' (eski sekme)');
    await click('form[data-simple-form] button[type=submit]');
    await waitFor(`!document.querySelector('[data-form-error]').hidden`);
    const conflictText = await text('[data-form-error]');
    check('Concurrent edit is reported, nothing overwritten', /başka biri tarafından değiştirildi/.test(conflictText), conflictText);
    await shot('09-edit-conflict');
    // Unsaved changes stay on the page; "Sayfayı yenile" drops them on purpose and loads the current data.
    await click('[data-form-error] .mp-reload');
    await waitFor(`document.readyState === 'complete' && document.querySelector('[data-form-error]')?.hidden === true`);
    check('"Sayfayı yenile" loads the current values', (await evaluate(`document.querySelector('input[name=Stock]').value`)) === '6');

    // ---------- Hidden price ("Fiyat için arayın"), as the supplier import leaves unpriced products ----------
    // Turn the test product into one: draft, price 0, price hidden.
    await go(editHref);
    await evaluate(`document.querySelector('input[name=Published][value=false]').click(), true`);
    check('Test product back to draft', await saveForm());
    await panelPost(`/motor-panel/product/${productId}/field`, { field: 'price', value: '0', expected: '1450' });
    const hide = await panelPost(`/motor-panel/product/${productId}/field`, { field: 'callForPrice', value: 'true', expected: 'false' });
    check('Price can be hidden (call for price)', hide.status === 200 && hide.data?.value === 'true', `${hide.status}`);
    await go(editHref);
    check('Editor shows "Fiyat sitede görünsün" unticked for a hidden price', (await evaluate(`!document.querySelector('[data-show-price]').checked && !document.querySelector('[data-price-hidden-note]').hidden`)));
    await type('input[name=Price]', '2.750');
    await evaluate(`document.querySelector('input[name=Price]').dispatchEvent(new Event('input', { bubbles: true })), true`);
    check('Typing a price ticks "Fiyat sitede görünsün" with an explanation', (await evaluate(`document.querySelector('[data-show-price]').checked && !document.querySelector('[data-price-auto-note]').hidden`)));
    await shot('12-hidden-price-ticked');
    await evaluate(`document.querySelector('input[name=Published][value=true]').click(), true`);
    check('Save with visible price and publish', await saveForm());
    html = await (await anonymous(siteHref)).text();
    check('Customer sees the new price 2.750,00 and a cart button', html.includes('2.750,00') && /add-to-cart/i.test(html) && !/fiyat için arayın/i.test(html));

    // Inline: hidden, unpriced product gets a price in the list.
    await panelPost(`/motor-panel/product/${productId}/field`, { field: 'published', value: 'false', expected: 'true' });
    await panelPost(`/motor-panel/product/${productId}/field`, { field: 'price', value: '0', expected: '2750' });
    await panelPost(`/motor-panel/product/${productId}/field`, { field: 'callForPrice', value: 'true', expected: 'false' });
    await go(`/motor-panel/products?q=${encodeURIComponent(stamp)}`);
    check('List shows the "Fiyat gizli" switch', (await evaluate(`document.querySelector('tr[data-id="${productId}"] [data-toggle="callForPrice"]')?.dataset.value === 'true'`)));
    await evaluate(`document.querySelector('tr[data-id="${productId}"] [data-edit=price]').click(), true`);
    await type(`tr[data-id="${productId}"] .mp-cell-input`, '3.100');
    await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Enter', code: 'Enter', windowsVirtualKeyCode: 13 });
    await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Enter', code: 'Enter', windowsVirtualKeyCode: 13 });
    await waitFor(`document.querySelector('tr[data-id="${productId}"] [data-edit=price]').dataset.value === '3100'`);
    check('Inline price on a hidden, unpriced product shows the price', (await evaluate(`document.querySelector('tr[data-id="${productId}"] [data-toggle="callForPrice"]').dataset.value === 'false'`)));
    // Publish again from the list toggle.
    await evaluate(`document.querySelector('tr[data-id="${productId}"] [data-toggle="published"]').click(), true`);
    await waitFor(`document.querySelector('tr[data-id="${productId}"] [data-toggle="published"]').dataset.value === 'true'`);
    html = await (await anonymous(siteHref)).text();
    check('List toggle publishes; customer sees 3.100,00', html.includes('3.100,00') && !/fiyat için arayın/i.test(html));
    // Hide the price from the list switch; the customer sees no price.
    await evaluate(`document.querySelector('tr[data-id="${productId}"] [data-toggle="callForPrice"]').click(), true`);
    await waitFor(`document.querySelector('tr[data-id="${productId}"] [data-toggle="callForPrice"]').dataset.value === 'true'`);
    html = await (await anonymous(siteHref)).text();
    check('List switch hides the price again', !html.includes('3.100,00'));
    await evaluate(`document.querySelector('tr[data-id="${productId}"] [data-toggle="callForPrice"]').click(), true`);
    await waitFor(`document.querySelector('tr[data-id="${productId}"] [data-toggle="callForPrice"]').dataset.value === 'false'`);

    // ---------- Search: several words must all match ----------
    await go(`/motor-panel/products?q=${encodeURIComponent('kaskı ' + stamp)}`);
    check('Two-word search finds the product', (await evaluate(`document.querySelectorAll('tr[data-id="${productId}"]').length`)) === 1);
    await go(`/motor-panel/products?q=${encodeURIComponent('kaskı yokboyle' + stamp)}`);
    check('Two-word search needs every word', (await evaluate(`document.querySelectorAll('tr[data-id]').length`)) === 0);

    // ---------- Leaving with unsaved changes ----------
    await go(editHref);
    await type('input[name=Name]', name + ' (kaydedilmedi)');
    await evaluate(`document.querySelector('input[name=Name]').dispatchEvent(new Event('input', { bubbles: true })), true`);
    await click('[data-cancel]');
    await waitFor(`document.getElementById('mp-confirm').open`);
    check('Leaving asks "Sayfadan çıkılsın mı?"', /Sayfadan çıkılsın/.test(await text('#mp-confirm .mp-dialog__title')));
    await click('#mp-confirm .mp-dialog__ok');
    await waitFor(`location.pathname.startsWith('/motor-panel/products')`);
    check('"Sayfadan çık" leaves the page without saving', true);

    // ---------- List filters ----------
    await go(`/motor-panel/products?q=${encodeURIComponent(stamp)}&status=Unpublished`);
    check('Status filter "Taslak" hides the published product', (await evaluate(`document.querySelectorAll('tr[data-id="${productId}"]').length`)) === 0);
    await go(`/motor-panel/products?q=${encodeURIComponent(stamp)}&status=Published&stock=InStock`);
    check('Status + advanced stock filter find it', (await evaluate(`document.querySelectorAll('tr[data-id="${productId}"]').length`)) === 1);
    check('Active advanced filters open their area', (await evaluate(`document.querySelector('.mp-filters-more').open`)));
    await go(`/motor-panel/products?q=${encodeURIComponent((await evaluate(`document.querySelector('tr[data-id="${productId}"] .mp-mono').textContent.trim()`)))}`);
    check('Search by SKU finds the product', (await evaluate(`document.querySelectorAll('tr[data-id="${productId}"]').length`)) === 1);

    // ---------- Bulk action ----------
    await go(`/motor-panel/products?q=${encodeURIComponent(stamp)}`);
    await evaluate(`(() => { const b = document.querySelector('tr[data-id="${productId}"] [data-select]'); b.click(); return true; })()`);
    await waitFor(`!document.getElementById('mp-bulk').hidden`);
    await evaluate(`(() => { const s = document.querySelector('#mp-bulk select[name=action]'); s.value = 'Unpublish'; s.dispatchEvent(new Event('change', { bubbles: true })); document.querySelector('#mp-bulk button[type=submit]').click(); return true; })()`);
    await waitFor(`document.getElementById('mp-confirm').open`);
    await click('#mp-confirm .mp-dialog__ok');
    await waitFor(`document.querySelector('tr[data-id="${productId}"] [data-toggle="published"]')?.dataset.value === 'false'`);
    check('Bulk "Yayından kaldır" on the selection', true);

    // ---------- Duplicate and delete from the "⋯" menu ----------
    await evaluate(`document.querySelector('tr[data-id="${productId}"] .mp-row-more summary').click(), true`);
    check('Row menu opens', (await evaluate(`document.querySelector('tr[data-id="${productId}"] .mp-row-more').open`)));
    await evaluate(`document.querySelector('tr[data-id="${productId}"] [data-duplicate]').click(), true`);
    await waitFor(`document.getElementById('mp-confirm').open`);
    await click('#mp-confirm .mp-dialog__ok');
    await waitFor(`document.querySelectorAll('tr[data-id]').length === 2`);
    const copyId = await evaluate(`[...document.querySelectorAll('tr[data-id]')].map(r => Number(r.dataset.id)).find(id => id !== ${productId})`);
    created.push(copyId);
    check('Copy is created unpublished', (await evaluate(`document.querySelector('tr[data-id="${copyId}"] [data-toggle="published"]').dataset.value === 'false'`)));
    await evaluate(`document.querySelector('tr[data-id="${copyId}"] .mp-row-more summary').click(), true`);
    await evaluate(`document.querySelector('tr[data-id="${copyId}"] [data-delete]').click(), true`);
    await waitFor(`document.getElementById('mp-confirm').open`);
    await click('#mp-confirm .mp-dialog__ok');
    await waitFor(`document.querySelectorAll('tr[data-id="${copyId}"]').length === 0`);
    check('Delete from the row menu', true);

    // ---------- Categories page ----------
    const catName = `QA Sayfa Kategori ${stamp}`;
    await go('/motor-panel/categories');
    await evaluate(`[...document.querySelectorAll('[data-open-editor]')].find(b => /editor\\/?$/.test(b.dataset.openEditor)).click(), true`);
    await waitFor(`document.getElementById('mp-drawer').open && !!document.querySelector('#mp-drawer input[name=Name]')`);
    await type('#mp-drawer input[name=Name]', catName);
    await shot('13-category-editor');
    await click('#mp-drawer button[type=submit]');
    await waitFor(`!document.getElementById('mp-drawer').open && [...document.querySelectorAll('tr[data-name]')].some(r => r.dataset.name === ${JSON.stringify(catName)})`, 20000);
    const catId = await evaluate(`Number([...document.querySelectorAll('tr[data-name]')].find(r => r.dataset.name === ${JSON.stringify(catName)}).dataset.id)`);
    created.push('category:' + catId);
    check('Category created on the categories page', catId > 0, `#${catId}`);
    await sleep(800);
    // Same name again under the same parent is refused.
    await evaluate(`[...document.querySelectorAll('[data-open-editor]')].find(b => /editor\\/?$/.test(b.dataset.openEditor)).click(), true`);
    await waitFor(`document.getElementById('mp-drawer').open && !!document.querySelector('#mp-drawer input[name=Name]')`);
    await type('#mp-drawer input[name=Name]', catName.toLocaleUpperCase('tr'));
    await click('#mp-drawer button[type=submit]');
    await waitFor(`!!document.querySelector('#mp-drawer [data-error-for="Name"]')?.textContent`);
    check('Duplicate category name is refused', /zaten var/.test(await text('#mp-drawer [data-error-for="Name"]')));
    await click('#mp-drawer [data-close]');
    await sleep(300);
    if (await evaluate(`document.getElementById('mp-confirm').open`)) await click('#mp-confirm .mp-dialog__ok');
    // Rename.
    await go('/motor-panel/categories');
    await evaluate(`document.querySelector('tr[data-id="${catId}"] [data-open-editor]').click(), true`);
    await waitFor(`document.getElementById('mp-drawer').open && !!document.querySelector('#mp-drawer input[name=Name]')`);
    await type('#mp-drawer input[name=Name]', catName + ' (yeni ad)');
    await click('#mp-drawer button[type=submit]');
    await waitFor(`!document.getElementById('mp-drawer').open && [...document.querySelectorAll('tr[data-name]')].some(r => r.dataset.name === ${JSON.stringify(catName + ' (yeni ad)')})`, 20000);
    check('Category renamed', true);
    await sleep(800);
    // Move down/up keeps working.
    const moved = await evaluate(`(async () => { const b = document.querySelector('tr[data-id="${catId}"] [data-move][data-up="true"]'); if (!b || b.disabled) return 'skip'; const fd = new FormData(); fd.append('up', 'true'); const r = await fetch(b.dataset.move, { method: 'POST', body: fd, headers: { 'X-XSRF-Token': document.querySelector('meta[name="mp-xsrf"]').content, 'X-Requested-With': 'XMLHttpRequest' } }); return (await r.json()).success; })()`);
    check('Category move works', moved === true || moved === 'skip', String(moved));
    // The new category is offered in the product form at once.
    await go('/motor-panel/product/new');
    check('New category is offered in the product form', (await evaluate(`!!document.querySelector('.mp-combo__option[data-id="${catId}"]')`)));
    // Delete through its dialog.
    await go('/motor-panel/categories');
    await evaluate(`document.querySelector('tr[data-id="${catId}"] .mp-icon-btn--danger').click(), true`);
    await waitFor(`document.getElementById('mp-drawer').open && !!document.querySelector('#mp-drawer button.mp-btn--danger')`);
    await click('#mp-drawer button.mp-btn--danger');
    await waitFor(`!document.getElementById('mp-drawer').open && !document.querySelector('tr[data-id="${catId}"]')`, 20000);
    check('Category deleted', true);
    created.splice(created.indexOf('category:' + catId), 1);

    // ---------- "Tüm fiyatları gizle" (store-wide price lock) ----------
    await panelPost(`/motor-panel/product/${productId}/field`, { field: 'published', value: 'true', expected: 'false' });
    await go('/motor-panel');
    const lockBefore = await evaluate(`document.querySelector('#mp-price-lock .mp-switch').getAttribute('aria-checked')`);
    if (lockBefore === 'false') {
        await click('#mp-price-lock .mp-switch');
        await waitFor(`document.getElementById('mp-confirm').open`);
        await click('#mp-confirm .mp-dialog__ok');
        await waitFor(`document.querySelector('#mp-price-lock .mp-switch').getAttribute('aria-checked') === 'true'`);
        html = await (await anonymous(siteHref)).text();
        check('Price lock hides prices and cart on the storefront', !html.includes('3.100,00') && !/add-to-cart/i.test(html));
        await click('#mp-price-lock .mp-switch');
        await waitFor(`document.getElementById('mp-confirm').open`);
        await click('#mp-confirm .mp-dialog__ok');
        await waitFor(`document.querySelector('#mp-price-lock .mp-switch').getAttribute('aria-checked') === 'false'`);
        html = await (await anonymous(siteHref)).text();
        check('Price lock off: price is back', html.includes('3.100,00'));
    } else {
        check('Price lock was already on; not touched', true);
    }

    // ---------- Every other panel page renders ----------
    for (const [label, url] of [['Siparişler', '/motor-panel/orders'], ['Kargolar', '/motor-panel/shipments'], ['Müşteriler', '/motor-panel/customers'],
        ['Raporlar', '/motor-panel/reports'], ['Kuponlar', '/motor-panel/coupons'], ['Sayfalar', '/motor-panel/content'],
        ['Faturalar', '/motor-panel/invoices'], ['Ayarlar', '/motor-panel/settings'], ['Klasik ürün editörü', `/admin/product/edit/${productId}`]]) {
        await go(url);
        const ok = await evaluate(`location.pathname.startsWith(${JSON.stringify(url.split('?')[0])}) && !/Bir hata oluştu|An error occurred/i.test(document.body.innerText)`);
        check(`${label} page opens`, ok);
    }
    // Advanced editor (drawer) opens from the simple editor and saves.
    await go(editHref);
    await click('[data-reload-on-save]');
    await waitFor(`document.getElementById('mp-drawer').open && !!document.querySelector('#mp-drawer form[data-editor]')`);
    await click('#mp-drawer button[type=submit]');
    await waitFor(`document.readyState === 'complete' && !document.getElementById('mp-drawer').open`, 20000);
    check('Advanced editor saves and the simple editor reloads', true);

    // ---------- Server-side validation and security ----------
    const bypass = await panelPost('/motor-panel/product/create', { Name: 'QA bypass ' + stamp, CategoryId: firstCategory.id, Price: '0', Publish: 'true' });
    check('Publishing with price 0 is refused by the server (client checks bypassed)', bypass.status === 422 && /0'dan büyük/.test(bypass.data?.errors?.Price || ''), `${bypass.status} ${bypass.data?.errors?.Price}`);
    const forgedPhoto = await evaluate(`(async () => {
        const fd = new FormData();
        fd.append('Name', 'QA forged ${stamp}'); fd.append('CategoryId', '${firstCategory.id}'); fd.append('Price', '10'); fd.append('Publish', 'false');
        fd.append('photos', new File([new TextEncoder().encode('%PDF-1.7')], 'fatura.jpg', { type: 'image/jpeg' }));
        const r = await fetch('/motor-panel/product/create', { method: 'POST', body: fd, headers: { 'X-Requested-With': 'XMLHttpRequest', 'X-XSRF-Token': document.querySelector('meta[name="mp-xsrf"]').content } });
        return { status: r.status, data: await r.json() };
    })()`);
    check('A PDF sent as image/jpeg is refused by the server', forgedPhoto.status === 422 && /fotoğraf değil/.test(forgedPhoto.data?.errors?.Photos || ''), String(forgedPhoto.status));
    const noToken = await panelPost(`/motor-panel/product/${productId}/simple`, { Name: 'x', Price: '1', Stock: '1', Version: '0' }, false);
    check('POST without antiforgery token is refused (400)', noToken.status === 400, String(noToken.status));

    if (args.customer) {
        const [cEmail, cPassword] = fs.readFileSync(args.customer, 'utf8').split(/\r?\n/);
        const login = await fetch(base + '/login', { redirect: 'manual', headers: customerHeaders });
        const cookies = (login.headers.getSetCookie?.() || []).map(c => c.split(';')[0]).join('; ');
        const loginHtml = await login.text();
        const token = (loginHtml.match(/name="__RequestVerificationToken" type="hidden" value="([^"]+)"/) || [])[1];
        const posted = await fetch(base + '/login?returnUrl=%2Fmotor-panel', {
            method: 'POST', redirect: 'manual',
            headers: { ...customerHeaders, 'Content-Type': 'application/x-www-form-urlencoded', Cookie: cookies, 'Sec-Fetch-Site': 'same-origin', Origin: base },
            body: new URLSearchParams({ UsernameOrEmail: cEmail, Password: cPassword, CustomerLoginType: 'UsernameOrEmail', __RequestVerificationToken: token || '' })
        });
        const authCookies = [cookies, ...(posted.headers.getSetCookie?.() || []).map(c => c.split(';')[0])].join('; ');
        const panel = await fetch(base + '/motor-panel', { redirect: 'manual', headers: { ...customerHeaders, Cookie: authCookies } });
        check('Signed-in customer without admin rights cannot open the panel', [302, 401, 403].includes(panel.status) && posted.status === 302, `login ${posted.status}, panel ${panel.status} ${panel.headers.get('location') || ''}`);
    }

    // ---------- Dashboard still intact ----------
    await go('/motor-panel');
    check('Dashboard still renders cards and recent products', (await evaluate(`document.querySelectorAll('.mp-cards .mp-card').length === 4 && document.querySelectorAll('.mp-recent__item').length > 0`)));
    await shot('10-dashboard-after');

    // ---------- Phone width ----------
    await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });
    for (const [label, url] of [['dashboard', '/motor-panel'], ['list', '/motor-panel/products'], ['new', '/motor-panel/product/new'], ['edit', editHref]]) {
        await go(url);
        const overflow = await evaluate(`document.documentElement.scrollWidth - document.documentElement.clientWidth`);
        check(`No horizontal overflow at 390 px (${label})`, overflow <= 1, `${overflow}px`);
    }
    await go('/motor-panel/product/new');
    await shot('11-mobile-new-product');
    await send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false });

    if (quick1.data?.category?.id) {
        created.push('category:' + quick1.data.category.id);
    }
} catch (err) {
    exitCode = 1;
    check('Run completed', false, err.message);
    try { await shot('99-failure'); } catch { /* ignore */ }
} finally {
    // ---------- Cleanup: disposable test data ----------
    if (!args.keep) {
        for (const item of created) {
            try {
                if (String(item).startsWith('category:')) {
                    const id = String(item).split(':')[1];
                    const r = await panelPost(`/motor-panel/categories/${id}/delete`, { deleteSubCategories: 'false' });
                    console.log(`cleanup category #${id}: ${r.status}`);
                } else {
                    const r = await panelPost(`/motor-panel/product/${item}/delete`, {});
                    console.log(`cleanup product #${item}: ${r.status}`);
                }
            } catch (e) { console.log('cleanup failed', item, e.message); }
        }
    }

    const relevant = problems.filter(p => !/favicon|google|gtag|analytics|ts\.net|iterable/i.test(p));
    check('No console errors, JS exceptions or failed requests', relevant.length === 0, relevant.slice(0, 5).join(' || '));

    const failed = results.filter(r => !r.ok);
    console.log(`\n${results.length - failed.length}/${results.length} checks passed. Screenshots: ${outDir}`);
    if (failed.length) exitCode = 1;
    ws.close();
    browser.kill();
    process.exit(exitCode);
}
