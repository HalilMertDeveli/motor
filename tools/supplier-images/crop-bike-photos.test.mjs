// node --test tools/supplier-images/crop-bike-photos.test.mjs
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { cropPlan, detectWholeBike, isKitProduct, loadExceptions } from './crop-bike-photos.mjs';

// White RGB canvas with filled rectangles and circles.
function canvas(w, h) {
    const data = Buffer.alloc(w * h * 3, 255);
    const set = (x, y, v) => { if (x >= 0 && y >= 0 && x < w && y < h) data.fill(v, (y * w + x) * 3, (y * w + x) * 3 + 3); };
    return {
        w, h, data,
        rect(x0, y0, x1, y1, v) { for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) set(x, y, v); return this; },
        circle(cx, cy, r, v) { for (let y = cy - r; y <= cy + r; y++) for (let x = cx - r; x <= cx + r; x++) if ((x - cx) ** 2 + (y - cy) ** 2 <= r * r) set(x, y, v); return this; }
    };
}

// Side view: grey body over two black tyres, facing right.
const bike = () => canvas(400, 300).rect(40, 120, 360, 220, 150).circle(80, 250, 40, 20).circle(320, 250, 40, 20);

test('a whole bike on a white background is detected', () => {
    const d = detectWholeBike(bike());
    assert.equal(d.reason, 'whole bike');
    assert.deepEqual(d.box, { x: 40, y: 120, w: 321, h: 171 });
});

test('photos that are not a whole bike are kept', () => {
    // top case alone: compact object
    assert.equal(detectWholeBike(canvas(400, 300).rect(120, 80, 280, 220, 30)).reason, 'narrow');
    // part photo: wide but flat, one dark run per row
    assert.equal(detectWholeBike(canvas(400, 300).rect(40, 120, 360, 200, 30)).reason, 'no two wheels');
    // close-up running off the image
    assert.equal(detectWholeBike(bike().rect(0, 120, 60, 220, 150)).reason, 'touches the border');
    // scene instead of a studio background
    assert.equal(detectWholeBike(bike().rect(0, 0, 10, 10, 90)).reason, 'background not white');
    // tall object
    assert.equal(detectWholeBike(canvas(400, 400).rect(40, 40, 360, 360, 150).circle(80, 330, 25, 20).circle(320, 330, 25, 20)).reason, 'not wide');
});

test('the crop keeps the rear 55 % of the bike at full height on a padded square', () => {
    const box = { x: 40, y: 120, w: 321, h: 171 };
    const plan = cropPlan(box);
    assert.deepEqual(plan.crop, { x: 40, y: 120, w: 176, h: 171 });
    assert.equal(plan.side, 186);
    assert.deepEqual(plan.offset, { x: 5, y: 7 });
    // a bike facing left keeps its right side
    assert.deepEqual(cropPlan(box, true).crop, { x: 185, y: 120, w: 176, h: 171 });
});

test('only SHAD mounting kits are cropped, never tank bags', () => {
    const shad = name => ({ brand: 'SHAD', name });
    assert.ok(isKitProduct(shad('SHAD Arka Çanta Bağlantı Demiri - Honda Forza 300 (2013/17)')));
    assert.ok(isKitProduct(shad('SHAD 3P System Yan Çanta Bağlantısı - Yamaha Tracer 9')));
    assert.ok(isKitProduct(shad('Shad Kymco AK 550 Sissybar Sırt Dayama')));
    assert.ok(!isKitProduct(shad('Shad H010CS Honda Depo Üstü Click System Bağlantı Demiri')));
    assert.ok(!isKitProduct(shad('Shad SH47 Titanyum Kapaklı Arka Çanta')));
    assert.ok(!isKitProduct(shad('Shad SH26 / SH29 / SH33 / SH34 Arka Çanta Sırt Dayama')));
    assert.ok(!isKitProduct({ brand: 'Prosev', name: 'Prosev Q7 İnterkom Kask Bağlantı Aparatı' }));
});

test('the exception list parses and does not list a picture twice', () => {
    const e = loadExceptions(new URL('./bike-photo-exceptions.json', import.meta.url));
    assert.equal(e.keep.size, 7);
    assert.equal(e.mirror.size, 1);
    assert.ok([...e.mirror].every(n => !e.keep.has(n)));
});
