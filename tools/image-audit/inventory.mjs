#!/usr/bin/env node
// Image inventory and defect report of a Smartstore tenant: every MediaFile, its storage file, its largest generated
// thumbnail and every product it is mapped to. Read-only: it never writes to the database or the media folders.
// Requires Node 22.5+ (node:sqlite) and ffmpeg on PATH.
//
//   node tools/image-audit/inventory.mjs --tenant=src/Smartstore.Web/App_Data/Tenants/Default
//        --out=reports/image-audit [--db=motordarkux.db] [--base=http://127.0.0.1:5107] [--jobs=8]
//
// Writes inventory.csv (one row per picture and product), defects.csv, review-queue.csv, summary.md and
// contact-sheets/<defect>.html (pictures loaded from --base, nothing is copied).

import { execFile } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { detectWholeBike, isKitProduct, loadExceptions } from '../supplier-images/crop-bike-photos.mjs';
import {
    analyzePixels, colourConflict, dHash, duplicatesInProduct, fileDefects, hamming, isTruncated, meanDiff,
    readHeader, resample, samePhoto, severity
} from './image-lib.mjs';
import { storagePath } from './storage-path.mjs';

const run = promisify(execFile);
const args = Object.fromEntries(process.argv.slice(2).map(a => a.replace(/^--/, '').split(/=(.*)/s).slice(0, 2)));
const TENANT = args.tenant || 'src/Smartstore.Web/App_Data/Tenants/Default';
const OUT = args.out || 'reports/image-audit';
const BASE = (args.base || 'http://127.0.0.1:5107').replace(/\/$/, '');
const JOBS = +(args.jobs || 8);
const ANALYSIS = 240;   // long side of the decoded copy used for pixel checks

const pad = (n, w) => String(n).padStart(w, '0');
const stem = name => name.replace(/\.[^.]+$/, '');

async function decode(file, w, h) {
    const { stdout } = await run('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-i', file, '-vf', `scale=${w}:${h}:flags=area,format=rgb24`, '-frames:v', '1', '-f', 'rawvideo', '-'],
        { encoding: 'buffer', maxBuffer: 64 * 1024 * 1024 });
    if (stdout.length !== w * h * 3) throw new Error(`decoded ${stdout.length} bytes, expected ${w * h * 3}`);
    return { w, h, data: stdout };
}

async function pool(items, n, fn) {
    let i = 0;
    await Promise.all(Array.from({ length: n }, async () => { while (i < items.length) { const k = i++; await fn(items[k], k); } }));
}

function readDb(file) {
    const db = new DatabaseSync(file, { readOnly: true });
    const q = s => db.prepare(s).all();
    const files = q('SELECT Id, Name, Extension, MimeType, MediaType, Size, Width, Height, Version, FolderId, Deleted FROM MediaFile');
    const maps = q(`SELECT x.Id AS MapId, x.ProductId, x.MediaFileId, x.DisplayOrder, p.Name AS ProductName, p.Sku, p.Published, p.Deleted,
            (SELECT m.Name FROM Product_Manufacturer_Mapping pm JOIN Manufacturer m ON m.Id = pm.ManufacturerId WHERE pm.ProductId = p.Id ORDER BY pm.DisplayOrder LIMIT 1) AS Brand
        FROM Product_MediaFile_Mapping x JOIN Product p ON p.Id = x.ProductId`);
    const products = q('SELECT Id, Name, Sku, Published, Deleted, MainPictureId FROM Product');
    const used = {
        category: q('SELECT Id, Name, MediaFileId FROM Category WHERE MediaFileId IS NOT NULL'),
        manufacturer: q('SELECT Id, Name, MediaFileId FROM Manufacturer WHERE MediaFileId IS NOT NULL'),
        attributeValue: q('SELECT Id, Name, MediaFileId FROM ProductVariantAttributeValue WHERE MediaFileId > 0'),
        combination: q("SELECT Id, ProductId, AssignedMediaFileIds FROM ProductVariantAttributeCombination WHERE AssignedMediaFileIds IS NOT NULL AND AssignedMediaFileIds <> ''")
    };
    db.close();
    return { files, maps, products, used };
}

// Human decisions from fixes.json: a reviewed picture keeps its defect codes but no longer fails the audit
const REVIEWED = new Map(JSON.parse(fs.readFileSync(new URL('fixes.json', import.meta.url), 'utf8')).reviewed.flatMap(r => r.mediaIds.map(id => [id, r.verdict])));
export const sev = d => d.verdict === 'OK' ? 'accepted' : d.verdict ? 'needs-source' : severity(d.code);

const EXCEPTIONS = loadExceptions(fileURLToPath(new URL('../supplier-images/bike-photo-exceptions.json', import.meta.url)));

async function inspect(f, media, kit) {
    const file = storagePath(media, f);
    const r = { mediaId: f.Id, name: f.Name, path: path.relative(media, file).replace(/\\/g, '/'), dbWidth: f.Width, dbHeight: f.Height, dbSize: f.Size, kit, exists: fs.existsSync(file) };
    if (!r.exists) return r;
    const buf = fs.readFileSync(file);
    r.bytes = buf.length;
    r.header = readHeader(buf);
    if (!r.header) return r;
    r.truncated = isTruncated(buf, r.header.format);
    const { width: W, height: H } = r.header;
    const s = ANALYSIS / Math.max(W, H);
    try {
        const px = await decode(file, Math.max(1, Math.round(W * s)), Math.max(1, Math.round(H * s)));
        r.pixels = analyzePixels(px);
        r.small = resample(px, 96, 96);
        r.hash = dHash(px);
        if (kit && !EXCEPTIONS.keep.has(f.Name.toLowerCase())) r.wholeBike = detectWholeBike(px).bike;
    } catch (e) {
        r.decodeError = e.message.split('\n')[0].slice(0, 160);
    }

    // Largest generated thumbnail must still show the original
    const tdir = path.join(media, 'Thumbs', pad(Math.floor(f.Id / 1000), 4));
    const prefix = `${pad(f.Id, 7)}-${stem(f.Name)}-size`;
    const thumbs = fs.existsSync(tdir) ? fs.readdirSync(tdir).filter(t => t.startsWith(prefix)) : [];
    r.thumbs = thumbs.length;
    if (thumbs.length && r.small) {
        const t = thumbs.sort((a, b) => parseInt(b.slice(prefix.length)) - parseInt(a.slice(prefix.length)))[0];
        try { r.thumbDiff = meanDiff(r.small, await decode(path.join(tdir, t), 96, 96)); r.thumbFile = t; }
        catch (e) { r.thumbDiff = 255; r.thumbFile = t + ' (unreadable)'; }
    }
    return r;
}

const csvCell = v => /[",\n;]/.test(String(v ?? '')) ? `"${String(v).replace(/"/g, '""')}"` : String(v ?? '');
const writeCsv = (file, cols, rows) => fs.writeFileSync(file, [cols.join(','), ...rows.map(r => cols.map(c => csvCell(r[c])).join(','))].join('\n') + '\n');
const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

export async function buildInventory({ tenant = TENANT, db = 'motordarkux.db', jobs = JOBS } = {}) {
    const media = path.join(tenant, 'Media');
    const data = readDb(path.join(tenant, db));
    const mapsByFile = Map.groupBy(data.maps, m => m.MediaFileId);
    const facts = new Map();
    let done = 0;
    await pool(data.files, jobs, async f => {
        const kit = (mapsByFile.get(f.Id) || []).some(m => isKitProduct({ brand: m.Brand, name: m.ProductName }));
        facts.set(f.Id, await inspect(f, media, kit));
        if (++done % 250 === 0) process.stderr.write(`${done}/${data.files.length}\n`);
    });

    // Per-file defects
    const defects = [];
    for (const f of data.files) {
        const r = facts.get(f.Id);
        r.defects = fileDefects(r);
        for (const d of r.defects) defects.push({ mediaId: f.Id, file: f.Name, ...d });
    }

    // Associations: duplicates inside a product, same photo on products of different colours, gallery order
    const byProduct = Map.groupBy(data.maps, m => m.ProductId);
    for (const [pid, maps] of byProduct) {
        const pics = maps.map(m => ({ mediaId: m.MediaFileId, hash: facts.get(m.MediaFileId)?.hash, thumb: facts.get(m.MediaFileId)?.small }));
        for (const [keep, dupe] of duplicatesInProduct(pics)) {
            defects.push({ mediaId: dupe, productId: pid, sku: maps[0].Sku, file: facts.get(dupe)?.name, code: 'duplicate-in-product', other: keep, detail: `looks the same as media ${keep}` });
        }
        const orders = maps.map(m => m.DisplayOrder);
        if (new Set(orders).size !== orders.length) defects.push({ productId: pid, sku: maps[0].Sku, code: 'gallery-order-tie', detail: `display orders ${orders.join('/')}` });
        // Product.MainPictureId is what cards and search show; it must be the first gallery photo
        const first = [...maps].sort((a, b) => a.DisplayOrder - b.DisplayOrder || a.MapId - b.MapId)[0];
        const main = data.products.find(p => p.Id === pid)?.MainPictureId;
        if (main !== first.MediaFileId) defects.push({ productId: pid, sku: maps[0].Sku, mediaId: main ?? undefined, code: 'main-picture-stale', detail: `MainPictureId ${main}, first photo ${first.MediaFileId}` });
        if (new Set(maps.map(m => m.MediaFileId)).size !== maps.length) defects.push({ productId: pid, sku: maps[0].Sku, code: 'duplicate-mapping', detail: 'same media mapped twice' });
    }
    const hashed = data.files.map(f => ({ id: f.Id, hash: facts.get(f.Id).hash })).filter(x => x.hash);
    const productsOf = id => [...new Map((mapsByFile.get(id) || []).map(m => [m.ProductId, m])).values()];
    for (let i = 0; i < hashed.length; i++) for (let j = i + 1; j < hashed.length; j++) {
        if (hamming(hashed[i].hash, hashed[j].hash) > 2) continue;
        if (!samePhoto({ hash: hashed[i].hash, thumb: facts.get(hashed[i].id).small }, { hash: hashed[j].hash, thumb: facts.get(hashed[j].id).small })) continue;
        for (const a of productsOf(hashed[i].id)) for (const b of productsOf(hashed[j].id)) {
            if (a.ProductId !== b.ProductId && colourConflict(a.ProductName, b.ProductName)) {
                defects.push({ mediaId: hashed[j].id, productId: b.ProductId, sku: b.Sku, file: facts.get(hashed[j].id).name, code: 'shared-photo-colour-conflict', other: hashed[i].id, otherProduct: a.ProductId, detail: `same photo as media ${hashed[i].id} on "${a.ProductName}" vs "${b.ProductName}"` });
            }
        }
    }
    for (const p of data.products.filter(p => p.Published && !p.Deleted && !byProduct.has(p.Id))) {
        defects.push({ productId: p.Id, sku: p.Sku, code: 'published-without-picture', detail: p.Name });
    }
    const referenced = new Set([...data.maps.map(m => m.MediaFileId), ...data.used.category.map(x => x.MediaFileId), ...data.used.manufacturer.map(x => x.MediaFileId), ...data.used.attributeValue.map(x => x.MediaFileId),
        ...data.used.combination.flatMap(c => c.AssignedMediaFileIds.split(',').map(Number))]);
    for (const id of referenced) if (!facts.has(id)) defects.push({ mediaId: id, code: 'dangling-reference', detail: 'a record points to a media file that does not exist' });

    for (const d of defects) if (REVIEWED.has(d.mediaId)) d.verdict = REVIEWED.get(d.mediaId);
    return { data, facts, defects, referenced };
}

function writeReports({ data, facts, defects, referenced }, out) {
    fs.mkdirSync(path.join(out, 'contact-sheets'), { recursive: true });
    const rows = [];
    const codesOf = id => [...new Set(defects.filter(d => d.mediaId === id).map(d => d.code))].join(' ');
    const status = id => {
        const c = defects.filter(d => d.mediaId === id);
        return c.some(d => sev(d) === 'error') ? 'DEFECT' : c.some(d => sev(d) === 'needs-source') ? 'NEEDS_SOURCE' : c.some(d => sev(d) === 'review') ? 'REVIEW' : 'OK';
    };
    for (const f of data.files) {
        const r = facts.get(f.Id);
        const maps = data.maps.filter(m => m.MediaFileId === f.Id);
        const base = {
            media_id: f.Id, file: f.Name, path: r.path, db_size: `${f.Width}x${f.Height}`, file_size: r.header ? `${r.header.width}x${r.header.height}` : '',
            aspect: r.header ? (r.header.width / r.header.height).toFixed(3) : '', bytes: r.bytes ?? '', format: r.header?.format ?? '',
            display: 'card 256/512, detail 1024, viewer original', subject_fill: r.pixels?.boxFill ?? '', studio: r.pixels?.studio ?? '',
            thumbs: r.thumbs ?? 0, dhash: r.hash ?? '', status: status(f.Id), defects: codesOf(f.Id), referenced: referenced.has(f.Id)
        };
        if (!maps.length) rows.push({ ...base, product_id: '', sku: '', product: '', published: '', order: '' });
        for (const m of maps) rows.push({ ...base, product_id: m.ProductId, sku: m.Sku, product: m.ProductName, brand: m.Brand, published: m.Published, order: m.DisplayOrder });
    }
    writeCsv(path.join(out, 'inventory.csv'), ['media_id', 'product_id', 'sku', 'brand', 'product', 'published', 'order', 'file', 'path', 'db_size', 'file_size', 'aspect', 'bytes', 'format', 'display', 'subject_fill', 'studio', 'thumbs', 'dhash', 'status', 'defects', 'referenced'], rows);
    writeCsv(path.join(out, 'defects.csv'), ['code', 'severity', 'media_id', 'product_id', 'sku', 'file', 'detail'], defects.map(d => ({ ...d, severity: sev(d), media_id: d.mediaId, product_id: d.productId })));

    // Contact sheets per defect code, pictures served by the running store
    const byCode = Map.groupBy(defects.filter(d => d.mediaId), d => d.code);
    for (const [code, list] of byCode) {
        const cells = list.map(d => {
            const f = facts.get(d.mediaId); const m = data.maps.find(x => x.MediaFileId === d.mediaId);
            const o = d.other && facts.get(d.other);
            return `<figure class="${o ? 'pair' : ''}"><img loading="lazy" src="${BASE}/media/${d.mediaId}/catalog/${encodeURIComponent(f?.name || '')}?size=256">${o ? `<img loading="lazy" src="${BASE}/media/${d.other}/catalog/${encodeURIComponent(o.name)}?size=256">` : ''}<figcaption><b>${esc(m?.Sku || '')}</b> #${d.mediaId}<br>${esc(m?.ProductName || f?.name)}<br><i>${esc(d.detail)}</i></figcaption></figure>`;
        }).join('');
        fs.writeFileSync(path.join(out, 'contact-sheets', `${code}.html`), `<!doctype html><html lang="tr"><head><meta charset="utf-8"><title>${esc(code)}</title><style>body{font:12px system-ui;margin:12px;background:#f4f4f4}figure{display:inline-block;width:200px;margin:4px;vertical-align:top;background:#fff;padding:6px}img{width:200px;height:200px;object-fit:contain;background:#fff;outline:1px solid #ddd}figure.pair{width:406px}</style></head><body><h1>${esc(code)} (${list.length})</h1>${cells}</body></html>`);
    }

    const count = c => defects.filter(d => d.code === c).length;
    const codes = [...new Set(defects.map(d => d.code))].sort();
    const files = data.files.length, ok = data.files.filter(f => status(f.Id) === 'OK').length;
    const md = [
        '# Image audit', '', `Tenant files: ${files}, product mappings: ${data.maps.length}, products with pictures: ${new Set(data.maps.map(m => m.ProductId)).size}, published products: ${data.products.filter(p => p.Published && !p.Deleted).length}.`, '',
        `| Status | Files |`, `|---|---:|`, ...['OK', 'REVIEW', 'NEEDS_SOURCE', 'DEFECT'].map(s => `| ${s} | ${data.files.filter(f => status(f.Id) === s).length} |`), '',
        '| Defect | Count | Open errors | Accepted | Needs source |', '|---|---:|---:|---:|---:|', ...codes.map(c => { const l = defects.filter(d => d.code === c); return `| ${c} | ${l.length} | ${l.filter(d => sev(d) === 'error').length} | ${l.filter(d => sev(d) === 'accepted').length} | ${l.filter(d => sev(d) === 'needs-source').length} |`; }), '',
        `Verified OK: ${ok} of ${files}. Contact sheets: contact-sheets/<defect>.html (needs the store at ${BASE}).`
    ].join('\n');
    fs.writeFileSync(path.join(out, 'summary.md'), md + '\n');
    return md;
}

if (process.argv[1]?.endsWith('inventory.mjs')) {
    const t0 = Date.now();
    const result = await buildInventory({ db: args.db || 'motordarkux.db' });
    console.log(writeReports(result, OUT));
    console.log(`done in ${((Date.now() - t0) / 1000).toFixed(0)} s`);
    process.exitCode = result.defects.some(d => sev(d) === 'error') ? 1 : 0;
}
