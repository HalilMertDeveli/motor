#!/usr/bin/env node
// Applies the verified photo corrections in fixes.json to a Smartstore tenant (SQLite): removes wrong or duplicate
// product-photo links, puts a complete photo first, and trims the white space around tiny spare part drawings.
// Genuine pixels only: nothing is generated, trimming only cuts empty background.
//
// STOP THE STORE FIRST (Smartstore caches products and pictures), then:
//   node tools/image-audit/apply-fixes.mjs --tenant=src/Smartstore.Web/App_Data/Tenants/Default --backup=E:/motor-backups/x [--dry-run=1]
//
// Idempotent: links already removed and drawings already trimmed are skipped. --backup receives a copy of the database
// and of every replaced original. Products are matched by id and SKU, media by id and file name.

import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { backup, DatabaseSync } from 'node:sqlite';
import { fileURLToPath } from 'node:url';
import { readRgb } from '../supplier-images/crop-bike-photos.mjs';
import { analyzePixels, readHeader, TINY_SUBJECT } from './image-lib.mjs';
import { storagePath } from './storage-path.mjs';

export const TRIM_FILL = 0.8;  // the drawing uses this share of the new square canvas

// Pure plan: which statements to run for the current database rows. Throws on anything that does not match.
// db = { products: [{Id, Sku}], files: [{Id, Name}], maps: [{Id, ProductId, MediaFileId, DisplayOrder}] }
export function planFixes(fixes, db) {
    const product = id => db.products.find(p => p.Id === id);
    const file = id => db.files.find(f => f.Id === id);
    const errors = [], plan = { remove: [], order: [], trim: [], skipped: [] };
    const check = (cond, msg) => { if (!cond) errors.push(msg); return cond; };
    const mapsOf = pid => db.maps.filter(m => m.ProductId === pid).sort((a, b) => a.DisplayOrder - b.DisplayOrder || a.Id - b.Id);

    for (const r of fixes.removeMappings || []) {
        if (!check(product(r.productId)?.Sku === r.sku, `product ${r.productId} is not ${r.sku}`)) continue;
        if (!check(file(r.mediaId)?.Name === r.file, `media ${r.mediaId} is not ${r.file}`)) continue;
        const map = db.maps.find(m => m.ProductId === r.productId && m.MediaFileId === r.mediaId);
        if (!map) { plan.skipped.push(`link ${r.sku} / ${r.file} already removed`); continue; }
        plan.remove.push({ mapId: map.Id, productId: r.productId, mediaId: r.mediaId });
    }
    const removed = new Set(plan.remove.map(x => x.mapId));
    for (const pid of new Set(plan.remove.map(x => x.productId))) {
        check(mapsOf(pid).some(m => !removed.has(m.Id)), `product ${pid} would lose its last photo`);
    }

    for (const r of fixes.reorder || []) {
        if (!check(product(r.productId)?.Sku === r.sku, `product ${r.productId} is not ${r.sku}`)) continue;
        const current = mapsOf(r.productId).filter(m => !removed.has(m.Id));
        const listed = r.mediaIds.filter(id => current.some(m => m.MediaFileId === id));
        check(listed.length > 0 && listed[0] === r.mediaIds[0], `product ${r.sku}: new main photo ${r.mediaIds[0]} is not linked`);
        const rest = current.filter(m => !listed.includes(m.MediaFileId)).map(m => m.MediaFileId);
        const wanted = [...listed, ...rest];
        wanted.forEach((mediaId, i) => {
            const m = current.find(x => x.MediaFileId === mediaId);
            if (m.DisplayOrder !== i + 1) plan.order.push({ mapId: m.Id, displayOrder: i + 1 });
        });
    }

    for (const t of fixes.trimWhitespace || []) {
        if (!check(file(t.mediaId)?.Name === t.file, `media ${t.mediaId} is not ${t.file}`)) continue;
        plan.trim.push({ mediaId: t.mediaId });
    }
    if (errors.length) throw new Error('fixes.json does not match this database:\n- ' + errors.join('\n- '));
    return plan;
}

// Square canvas around the subject box with the drawing at `fill`; null when it is already large enough.
export function trimBox(p, w, h, fill = TRIM_FILL) {
    if (!p.studio || p.empty || p.boxFill >= TINY_SUBJECT) return null;
    const side = Math.min(Math.max(w, h), Math.ceil(Math.max(p.box.w, p.box.h) / fill));
    const cx = p.box.x + p.box.w / 2, cy = p.box.y + p.box.h / 2;
    const x = Math.round(cx - side / 2), y = Math.round(cy - side / 2);
    return { side, crop: { x: Math.max(0, x), y: Math.max(0, y), w: Math.min(w, x + side) - Math.max(0, x), h: Math.min(h, y + side) - Math.max(0, y) }, pad: { x: Math.max(0, -x), y: Math.max(0, -y) } };
}

function trimFile(src, target) {
    const rgb = readRgb(src);
    const box = trimBox(analyzePixels(rgb), rgb.w, rgb.h);
    if (!box) return null;
    const { side, crop, pad } = box;
    execFileSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y', '-i', src, '-vf',
        `crop=${crop.w}:${crop.h}:${crop.x}:${crop.y},pad=${side}:${side}:${pad.x}:${pad.y}:white,format=yuvj444p`, '-q:v', '2', '-frames:v', '1', target]);
    return side;
}

function main(args) {
    const tenant = args.tenant || 'src/Smartstore.Web/App_Data/Tenants/Default';
    const dbFile = path.join(tenant, args.db || 'motordarkux.db');
    const media = path.join(tenant, 'Media');
    const dry = args['dry-run'] === '1';
    const fixes = JSON.parse(fs.readFileSync(args.fixes || fileURLToPath(new URL('fixes.json', import.meta.url)), 'utf8'));
    if (!dry && !args.backup) { console.error('--backup=<dir> is required (or --dry-run=1)'); process.exit(2); }

    const db = new DatabaseSync(dbFile);
    const rows = {
        products: db.prepare('SELECT Id, Sku FROM Product').all(),
        files: db.prepare('SELECT Id, Name, Extension FROM MediaFile').all(),
        maps: db.prepare('SELECT Id, ProductId, MediaFileId, DisplayOrder FROM Product_MediaFile_Mapping').all()
    };
    const plan = planFixes(fixes, rows);
    console.log(`remove ${plan.remove.length} links, reorder ${plan.order.length} links, check ${plan.trim.length} drawings`);
    plan.skipped.forEach(s => console.log('skip:', s));
    if (dry) return;

    fs.mkdirSync(path.join(args.backup, 'media'), { recursive: true });
    return backup(db, path.join(args.backup, path.basename(dbFile))).then(() => {
        const touched = new Set([...plan.remove.map(x => x.productId), ...(fixes.reorder || []).map(r => r.productId)]);
        db.exec('BEGIN');
        try {
            for (const r of plan.remove) {
                db.prepare("DELETE FROM MediaTrack WHERE EntityName = 'ProductMediaFile' AND EntityId = ?").run(r.mapId);
                db.prepare('DELETE FROM Product_MediaFile_Mapping WHERE Id = ?').run(r.mapId);
            }
            for (const o of plan.order) db.prepare('UPDATE Product_MediaFile_Mapping SET DisplayOrder = ? WHERE Id = ?').run(o.displayOrder, o.mapId);
            // The cached main picture must follow the first photo, or cards keep showing the old one
            for (const pid of touched) {
                db.prepare(`UPDATE Product SET MainPictureId = (SELECT MediaFileId FROM Product_MediaFile_Mapping WHERE ProductId = ? ORDER BY DisplayOrder, Id LIMIT 1) WHERE Id = ?`).run(pid, pid);
            }

            let trimmed = 0;
            for (const t of plan.trim) {
                const f = rows.files.find(x => x.Id === t.mediaId);
                const file = storagePath(media, f);
                const tmp = file + '.trim.jpg';
                const side = trimFile(file, tmp);
                if (!side) { console.log(`skip: ${f.Name} already fills the frame`); continue; }
                fs.copyFileSync(file, path.join(args.backup, 'media', `${f.Id}-${f.Name}`));
                fs.renameSync(tmp, file);
                const bytes = fs.statSync(file).size, hdr = readHeader(fs.readFileSync(file));
                db.prepare(`UPDATE MediaFile SET Width = ?, Height = ?, PixelSize = ?, Size = ?, Version = Version + 1, UpdatedOnUtc = ? WHERE Id = ?`)
                    .run(hdr.width, hdr.height, hdr.width * hdr.height, bytes, new Date().toISOString().replace('T', ' ').replace('Z', ''), f.Id);
                // Generated sizes of the old picture
                const tdir = path.join(media, 'Thumbs', String(Math.floor(f.Id / 1000)).padStart(4, '0'));
                const prefix = `${String(f.Id).padStart(7, '0')}-`;
                if (fs.existsSync(tdir)) for (const th of fs.readdirSync(tdir).filter(n => n.startsWith(prefix))) fs.rmSync(path.join(tdir, th));
                trimmed++;
            }
            db.exec('COMMIT');
            console.log(`done: ${plan.remove.length} links removed, ${plan.order.length} reordered, ${trimmed} drawings trimmed. Backup: ${args.backup}`);
        } catch (e) {
            db.exec('ROLLBACK');
            throw e;
        }
    });
}

if (process.argv[1]?.endsWith('apply-fixes.mjs')) {
    Promise.resolve(main(Object.fromEntries(process.argv.slice(2).map(a => a.replace(/^--/, '').split(/=(.*)/s).slice(0, 2)))))
        .catch(e => { console.error(e.message); process.exitCode = 1; });
}
