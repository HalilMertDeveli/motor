// node --test tools/image-audit
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { test } from 'node:test';
import { planFixes, trimBox } from './apply-fixes.mjs';

const fixes = JSON.parse(fs.readFileSync(new URL('fixes.json', import.meta.url), 'utf8'));

// A database as fixes.json expects it: every product, file and link the corrections name
function matchingDb() {
    const products = new Map(), files = new Map(), maps = [];
    let mapId = 1;
    const link = (productId, sku, mediaId, file, order) => {
        products.set(productId, { Id: productId, Sku: sku });
        if (file) files.set(mediaId, { Id: mediaId, Name: file });
        else if (!files.has(mediaId)) files.set(mediaId, { Id: mediaId, Name: `m${mediaId}.jpg` });
        maps.push({ Id: mapId++, ProductId: productId, MediaFileId: mediaId, DisplayOrder: order });
    };
    for (const r of fixes.removeMappings) link(r.productId, r.sku, r.mediaId, r.file, 5);
    for (const r of fixes.reorder) r.mediaIds.slice().reverse().forEach((id, i) => {
        if (!maps.some(m => m.ProductId === r.productId && m.MediaFileId === id)) link(r.productId, r.sku, id, null, i + 1);
    });
    // every product keeps a photo of its own
    for (const p of products.values()) link(p.Id, p.Sku, 90000 + p.Id, null, 0);
    for (const t of fixes.trimWhitespace) files.set(t.mediaId, { Id: t.mediaId, Name: t.file });
    return { products: [...products.values()], files: [...files.values()], maps };
}

test('fixes.json: every decision has a reason and no media is both removed and kept as main photo', () => {
    for (const list of [fixes.removeMappings, fixes.reorder, fixes.trimWhitespace, fixes.reviewed]) {
        for (const x of list) assert.ok(x.reason?.length > 10, JSON.stringify(x));
    }
    const removed = new Set(fixes.removeMappings.map(r => `${r.productId}/${r.mediaId}`));
    for (const r of fixes.reorder) assert.ok(!removed.has(`${r.productId}/${r.mediaIds[0]}`), r.sku);
    for (const r of fixes.reviewed) assert.match(r.verdict, /^(OK|NEEDS_SOURCE)$/);
});

test('plan removes the listed links, reorders and trims', () => {
    const db = matchingDb();
    const plan = planFixes(fixes, db);
    assert.equal(plan.remove.length, fixes.removeMappings.length);
    assert.equal(plan.trim.length, fixes.trimWhitespace.length);
    // Y017CS: the tank ring part photo becomes the first photo
    const y017 = db.maps.find(m => m.ProductId === 257 && m.MediaFileId === 752);
    assert.deepEqual(plan.order.find(o => o.mapId === y017.Id), { mapId: y017.Id, displayOrder: 1 });
});

test('plan is idempotent: removed links are skipped', () => {
    const db = matchingDb();
    const first = planFixes(fixes, db);
    const removed = new Set(first.remove.map(r => r.mapId));
    db.maps = db.maps.filter(m => !removed.has(m.Id));
    const second = planFixes(fixes, db);
    assert.equal(second.remove.length, 0);
    assert.equal(second.skipped.length, fixes.removeMappings.length);
});

test('plan refuses a database with other products under the same ids', () => {
    const db = matchingDb();
    db.products.find(p => p.Id === 217).Sku = 'SOMETHING-ELSE';
    assert.throws(() => planFixes(fixes, db), /product 217 is not TR-48-BLACK-016900/);
});

test('plan refuses to leave a product without photos', () => {
    const db = matchingDb();
    db.maps = db.maps.filter(m => !(m.ProductId === 92 && m.MediaFileId === 90092));
    assert.throws(() => planFixes(fixes, db), /would lose its last photo/);
});

test('trim box: a tiny drawing gets a square canvas at 80 %, a large product is left alone', () => {
    const tiny = trimBox({ studio: true, empty: false, boxFill: 0.25, box: { x: 300, y: 350, w: 200, h: 100 } }, 800, 800);
    assert.equal(tiny.side, 250);
    assert.deepEqual(tiny.crop, { x: 275, y: 275, w: 250, h: 250 });
    assert.equal(trimBox({ studio: true, empty: false, boxFill: 0.9, box: { x: 0, y: 0, w: 720, h: 720 } }, 800, 800), null);
    assert.equal(trimBox({ studio: false, empty: false, boxFill: 0.2, box: { x: 0, y: 0, w: 10, h: 10 } }, 800, 800), null);
});
