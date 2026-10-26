// Checks for rendered <img> elements. `measureImages` runs inside the page (Playwright `page.evaluate`) and only
// collects numbers; `classify` turns them into defects in Node so the rules can be unit tested without a browser.

// Decorative banners are art-directed crops on purpose (hero, story). Everything else must show the whole picture.
export const DECORATIVE = ['motor-hero-photo', 'motor-story-photo'];

// Tolerances: 3 % aspect difference is invisible; a picture may be enlarged by 15 % before it gets soft.
export const ASPECT_TOLERANCE = 0.03;
export const UPSCALE_TOLERANCE = 1.15;
export const CLIP_TOLERANCE_PX = 2;

// Runs in the browser. Self-contained: no closures over Node variables.
export async function measureImages() {
    const dpr = window.devicePixelRatio || 1;
    const out = [];
    for (const img of document.images) {
        const r = img.getBoundingClientRect();
        if (r.width < 40 || r.height < 40) continue;
        const cs = getComputedStyle(img);
        if (cs.visibility === 'hidden' || cs.display === 'none' || +cs.opacity === 0) continue;

        // Nearest ancestor that clips its content
        let clip = null;
        for (let el = img.parentElement; el && el !== document.body; el = el.parentElement) {
            const s = getComputedStyle(el);
            if (s.overflow !== 'visible' || s.overflowX !== 'visible' || s.overflowY !== 'visible') { clip = el.getBoundingClientRect(); break; }
        }
        // naturalWidth of a srcset candidate is divided by its density; the file's real size needs a probe
        let naturalWidth = img.naturalWidth, naturalHeight = img.naturalHeight;
        if (img.currentSrc && naturalWidth && (img.srcset || img.parentElement?.tagName === 'PICTURE')) {
            const probe = new Image();
            probe.src = img.currentSrc;
            await probe.decode().catch(() => {});
            if (probe.naturalWidth) { naturalWidth = probe.naturalWidth; naturalHeight = probe.naturalHeight; }
        }
        // offsetWidth ignores transforms (hover zoom), the bounding box does not.
        out.push({
            src: img.currentSrc || img.src || '',
            cls: [img.className, img.parentElement?.className].join(' ').trim(),
            context: img.closest('[class*="art-"], [class*="motor-"], [class*="pswp"], [class*="gal"], [class*="cart"], [class*="search"]')?.className?.toString().split(' ')[0] || '',
            alt: img.alt || '',
            complete: img.complete,
            naturalWidth,
            naturalHeight,
            boxWidth: img.offsetWidth || r.width,
            boxHeight: img.offsetHeight || r.height,
            rect: { left: r.left, top: r.top, right: r.right, bottom: r.bottom },
            clip: clip && { left: clip.left, top: clip.top, right: clip.right, bottom: clip.bottom },
            objectFit: cs.objectFit,
            opacity: +cs.opacity,
            transform: cs.transform,
            padding: ['Top', 'Right', 'Bottom', 'Left'].map(k => parseFloat(cs['padding' + k]) || 0),
            dpr
        });
    }
    return out;
}

const isDecorative = m => DECORATIVE.some(c => m.cls.includes(c));

// Size of the drawn picture inside the content box, per object-fit.
export function drawnSize(m) {
    const [pt, pr, pb, pl] = m.padding || [0, 0, 0, 0];
    const bw = m.boxWidth - pl - pr, bh = m.boxHeight - pt - pb;
    const nw = m.naturalWidth, nh = m.naturalHeight;
    if (!nw || !nh) return { width: bw, height: bh };
    const scale = { contain: Math.min(bw / nw, bh / nh), cover: Math.max(bw / nw, bh / nh), none: 1, 'scale-down': Math.min(1, bw / nw, bh / nh) }[m.objectFit];
    return scale === undefined ? { width: bw, height: bh } : { width: nw * scale, height: nh * scale };
}

// Returns a list of { type, detail } for one measured image.
export function classify(m) {
    const defects = [];
    if (m.complete && (!m.naturalWidth || !m.naturalHeight)) {
        return [{ type: 'broken', detail: 'image did not decode' }];
    }
    if (!m.naturalWidth) return defects; // still lazy, nothing to judge

    // Without the version hash a photo corrected in place stays hidden behind the 7-day browser cache
    if (/\/media\/\d+\//.test(m.src) && !/[?&]ver=/.test(m.src)) defects.push({ type: 'unversioned-url', detail: 'media URL without ?ver=' });

    const [pt, pr, pb, pl] = m.padding || [0, 0, 0, 0];
    const bw = m.boxWidth - pl - pr, bh = m.boxHeight - pt - pb;
    const natAspect = m.naturalWidth / m.naturalHeight, boxAspect = bw / bh;
    const mismatch = Math.abs(natAspect - boxAspect) / boxAspect;

    if (!isDecorative(m)) {
        if (m.objectFit === 'cover' && mismatch > ASPECT_TOLERANCE) {
            defects.push({ type: 'cropped', detail: `object-fit:cover cuts ${(100 * (1 - Math.min(natAspect, boxAspect) / Math.max(natAspect, boxAspect))).toFixed(0)}% of the picture` });
        }
        if (m.objectFit === 'fill' && mismatch > ASPECT_TOLERANCE) {
            defects.push({ type: 'distorted', detail: `stretched: picture ${natAspect.toFixed(2)}, box ${boxAspect.toFixed(2)}` });
        }
        // Box sticking out of a clipping ancestor (ignores hover transforms: offset box, not the transformed rect)
        if (m.clip && m.transform === 'none') {
            const c = m.clip, r = m.rect, t = CLIP_TOLERANCE_PX;
            if (r.left < c.left - t || r.top < c.top - t || r.right > c.right + t || r.bottom > c.bottom + t) {
                defects.push({ type: 'clipped', detail: 'image box exceeds its clipping container' });
            }
        }
    }

    // A dimmed background (e.g. the story band on phones, 45 % under a dark shade) cannot show softness
    if (isDecorative(m) && m.opacity < 0.6) return defects;
    const drawn = drawnSize(m);
    const needed = drawn.width * m.dpr;
    if (needed > m.naturalWidth * UPSCALE_TOLERANCE) {
        defects.push({ type: 'upscaled', detail: `drawn at ${Math.round(needed)} device px from a ${m.naturalWidth} px file` });
    }
    return defects;
}
