/*
 * MotorDark product gallery
 * - Main photo: previous/next buttons, "n / total" counter and an "inspect" button that opens the viewer.
 * - Full-screen viewer (PhotoSwipe 4, opened by smartstore.gallery.js): dark UI, zoom in/out buttons,
 *   mouse-wheel zoom around the cursor, + / - / 0 keys, thumbnail strip, Turkish error text and
 *   no close-on-scroll. Pinch, double-tap, swipe and drag-to-pan come from PhotoSwipe itself.
 * The original photos are shown as they are; zoom stops at 2x of their real pixel size.
 */
(function ($, window, document) {
    'use strict';

    var MAX_ZOOM = 2;          // relative to the photo's real pixel size
    var ZOOM_STEP = 1.6;
    var reducedMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    function el(tag, className, attrs, html) {
        var node = document.createElement(tag);
        if (className) node.className = className;
        if (attrs) Object.keys(attrs).forEach(function (k) { node.setAttribute(k, attrs[k]); });
        if (html) node.innerHTML = html;
        return node;
    }

    // ---------------------------------------------------------------
    // Main photo controls
    // ---------------------------------------------------------------

    function enhanceGallery(root) {
        if (!root) return;

        var cell = root.querySelector('.gal-cell');
        var $gal = $(root).find('.gal').first();
        var count = parseInt($(root).closest('[data-motor-gallery-count]').attr('data-motor-gallery-count'), 10) || 0;

        if (!cell || !$gal.length || count < 1 || cell.querySelector('.motor-gal-ui')) {
            return;
        }

        if (!$gal.hasClass('slick-initialized')) {
            // smartGallery initializes slick right after us; try again then.
            $gal.one('init', function () { setTimeout(function () { enhanceGallery(root); }, 0); });
            return;
        }

        var ui = el('div', 'motor-gal-ui');
        var prev, next, counter;

        if (count > 1) {
            prev = el('button', 'motor-gal-btn motor-gal-prev', { type: 'button', 'aria-label': 'Önceki fotoğraf' },
                '<i class="fa fa-chevron-left" aria-hidden="true"></i>');
            next = el('button', 'motor-gal-btn motor-gal-next', { type: 'button', 'aria-label': 'Sonraki fotoğraf' },
                '<i class="fa fa-chevron-right" aria-hidden="true"></i>');
            counter = el('span', 'motor-gal-counter', { 'aria-live': 'polite' });
            ui.appendChild(prev);
            ui.appendChild(next);
            ui.appendChild(counter);

            prev.addEventListener('click', function () { $gal.slick('slickPrev'); });
            next.addEventListener('click', function () { $gal.slick('slickNext'); });
        }

        var hasPhoto = !!root.querySelector('.gal .gal-item-viewport:not(.no-picture)');
        if (hasPhoto) {
            var expand = el('button', 'motor-gal-btn motor-gal-expand', { type: 'button', 'aria-label': 'Fotoğrafı tam ekranda incele' },
                '<i class="fa fa-magnifying-glass-plus" aria-hidden="true"></i><span>İncele</span>');
            ui.appendChild(expand);

            expand.addEventListener('click', function () {
                var link = root.querySelector('.gal .slick-current .gal-item-viewport') || root.querySelector('.gal .gal-item-viewport');
                if (link) {
                    // After a swipe slick swallows the next click on the slider; this one is intended.
                    var slick = $gal.slick('getSlick');
                    if (slick) slick.shouldClick = true;

                    lastTrigger = expand;
                    $(link).trigger('click');
                }
            });
        }

        cell.appendChild(ui);

        function update(index) {
            if (!counter) return;
            counter.textContent = (index + 1) + ' / ' + count;
            prev.disabled = index <= 0;
            next.disabled = index >= count - 1;
        }

        var gallery = $(root).data('smartGallery');
        update(gallery ? gallery.currentIndex : 0);

        $gal.on('beforeChange.motor', function (e, slick, current, nextIndex) { update(nextIndex); });

        // Show the zoom-in cursor only where the click actually opens the viewer.
        $gal.find('.gal-item-viewport').not('.no-picture').addClass('motor-zoomable');
    }

    function enhanceAll() {
        enhanceGallery(document.getElementById('pd-gallery'));
    }

    // Phones: clamp the short description above the photos to three lines (CSS) with a toggle.
    function clampDescription() {
        var desc = document.querySelector('.pd-info-mobile .pd-description');
        if (!desc || desc.dataset.motorClamp) return;
        desc.dataset.motorClamp = '1';
        desc.classList.add('motor-clamped');

        if (desc.scrollHeight <= desc.clientHeight + 2) {
            // Short enough (or not a phone layout): nothing to toggle.
            desc.classList.remove('motor-clamped');
            return;
        }

        if (!desc.id) desc.id = 'motor-pd-desc';
        var toggle = el('button', 'motor-more-toggle', { type: 'button', 'aria-expanded': 'false', 'aria-controls': desc.id }, 'Devamını oku');
        toggle.addEventListener('click', function () {
            var expanded = desc.classList.toggle('motor-clamped') === false;
            toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
            toggle.textContent = expanded ? 'Daha az göster' : 'Devamını oku';
        });
        desc.insertAdjacentElement('afterend', toggle);
    }

    $(function () {
        enhanceAll();
        clampDescription();

        // The gallery is re-rendered when a variant with its own photos is selected.
        var container = document.getElementById('pd-gallery-container');
        if (container && window.MutationObserver) {
            new MutationObserver(function () { setTimeout(enhanceAll, 0); })
                .observe(container, { childList: true });
        }
    });

    // ---------------------------------------------------------------
    // Full-screen viewer
    // ---------------------------------------------------------------

    var lastTrigger = null;

    $(document).on('init.pswp', '#pswp', function (e, pswp, gallery) {
        var template = this;
        var opts = pswp.options;
        var zoomIn = template.querySelector('.motor-pswp-zoom-in');
        var zoomOut = template.querySelector('.motor-pswp-zoom-out');
        var strip = template.querySelector('.motor-pswp-thumbs');
        var trigger = lastTrigger || document.querySelector('#pd-gallery .gal .slick-current .gal-item-viewport');
        lastTrigger = null;

        opts.maxSpreadZoom = MAX_ZOOM;
        opts.closeOnScroll = false;
        opts.clickToCloseNonZoomable = false;
        opts.errorMsg = '<div class="pswp__error-msg">Fotoğraf yüklenemedi. <a href="%url%" target="_blank" rel="noopener">Fotoğrafı ayrı açın</a>.</div>';
        opts.getDoubleTapZoom = function (isMouseClick, item) {
            return Math.min(MAX_ZOOM, Math.max(1, item.initialZoomLevel * 2.5));
        };

        var isClickable = opts.isClickableElement;
        opts.isClickableElement = function (target) {
            return isClickable(target) || !!(target.closest && target.closest('.motor-pswp-ctl'));
        };

        function minZoom() { return pswp.currItem ? pswp.currItem.initialZoomLevel : 1; }
        function isZoomable() { return !!(pswp.currItem && pswp.currItem.w); }
        function center() { return { x: pswp.viewportSize.x / 2, y: pswp.viewportSize.y / 2 }; }

        function setZoom(level, point, duration) {
            if (!isZoomable()) return;
            level = Math.max(minZoom(), Math.min(MAX_ZOOM, level));
            if (Math.abs(level - pswp.getZoomLevel()) < 0.001) return;

            pswp.zoomTo(level, point || center(), reducedMotion ? 0 : duration, false, function (now) {
                if (now === 1) updateState();
            });

            if (!duration || reducedMotion) updateState();
        }

        function updateState() {
            var z = pswp.getZoomLevel(), min = minZoom();
            var zoomed = z > min + 0.001;

            pswp.mouseZoomedIn = zoomed;
            template.classList.toggle('pswp--zoomed-in', zoomed);
            if (zoomIn) zoomIn.disabled = !isZoomable() || z >= MAX_ZOOM - 0.001;
            if (zoomOut) zoomOut.disabled = !zoomed;
        }

        function onWheel(ev) {
            if (!isZoomable() || ev.ctrlKey && !ev.deltaY) return;
            ev.preventDefault();
            ev.stopPropagation();

            var delta = ev.deltaMode === 1 ? ev.deltaY * 18 : ev.deltaY;
            setZoom(pswp.getZoomLevel() * Math.exp(-delta * 0.0025), { x: ev.clientX, y: ev.clientY }, 0);
        }

        function onKey(ev) {
            if (ev.target && /input|textarea|select/i.test(ev.target.tagName)) return;
            var z = pswp.getZoomLevel();

            if (ev.key === '+' || ev.key === '=') setZoom(z * ZOOM_STEP, null, 250);
            else if (ev.key === '-' || ev.key === '_') setZoom(z / ZOOM_STEP, null, 250);
            else if (ev.key === '0') setZoom(minZoom(), null, 250);
            else return;

            ev.preventDefault();
        }

        function onZoomIn(ev) { ev.stopPropagation(); setZoom(pswp.getZoomLevel() * ZOOM_STEP, null, 250); }
        function onZoomOut(ev) { ev.stopPropagation(); setZoom(pswp.getZoomLevel() / ZOOM_STEP, null, 250); }
        function stop(ev) { ev.stopPropagation(); }

        // Thumbnail strip, built from the page's thumbnails (small files) in viewer order.
        var thumbs = [];
        if (strip) {
            strip.innerHTML = '';
            var navImgs = gallery && gallery.nav ? gallery.nav.find('.gal-item img').toArray() : [];

            if (pswp.items.length > 1) {
                pswp.items.forEach(function (item, i) {
                    var btn = el('button', 'motor-pswp-thumb', { type: 'button', 'aria-label': 'Fotoğraf ' + (i + 1) + ' / ' + pswp.items.length });
                    var src = navImgs[i] ? (navImgs[i].currentSrc || navImgs[i].getAttribute('src')) : item.msrc;
                    if (src) {
                        btn.appendChild(el('img', null, { src: src, alt: '', loading: 'lazy', draggable: 'false' }));
                    }
                    btn.addEventListener('click', function (ev) { ev.stopPropagation(); pswp.goTo(i); });
                    strip.appendChild(btn);
                    thumbs.push(btn);
                });
            }

            strip.hidden = thumbs.length === 0;
        }

        function markThumb() {
            var idx = pswp.getCurrentIndex();
            thumbs.forEach(function (t, i) {
                if (i === idx) {
                    t.setAttribute('aria-current', 'true');
                    t.scrollIntoView({ block: 'nearest', inline: 'center', behavior: reducedMotion ? 'auto' : 'smooth' });
                }
                else {
                    t.removeAttribute('aria-current');
                }
            });
        }

        template.addEventListener('wheel', onWheel, { passive: false, capture: true });
        document.addEventListener('keydown', onKey, true);
        if (zoomIn) zoomIn.addEventListener('click', onZoomIn);
        if (zoomOut) zoomOut.addEventListener('click', onZoomOut);
        $(template).find('.motor-pswp-ctl').on('pswpTap.motor', stop);

        pswp.listen('afterChange', function () { updateState(); markThumb(); });
        pswp.listen('initialZoomInEnd', updateState);
        pswp.listen('zoomGestureEnded', updateState);
        pswp.listen('imageLoadComplete', updateState);

        pswp.listen('destroy', function () {
            template.removeEventListener('wheel', onWheel, { capture: true });
            document.removeEventListener('keydown', onKey, true);
            if (zoomIn) zoomIn.removeEventListener('click', onZoomIn);
            if (zoomOut) zoomOut.removeEventListener('click', onZoomOut);
            $(template).find('.motor-pswp-ctl').off('.motor');
            template.classList.remove('pswp--zoomed-in');
            if (strip) strip.innerHTML = '';

            // Give focus back to where the viewer was opened from.
            if (trigger && document.body.contains(trigger) && trigger.focus) {
                setTimeout(function () { trigger.focus({ preventScroll: true }); }, 0);
            }
        });

        updateState();
        markThumb();
    });

})(jQuery, window, document);
