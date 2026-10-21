/*
 * MotorDark theme enhancements.
 * Progressive enhancement only: the storefront works fully without this file.
 * - Sticky header state, scroll progress and measured header height
 * - Scroll reveal and staggered entrances (IntersectionObserver)
 * - Hero light pass and photo parallax (desktop only)
 * - Add-to-cart feedback hooked into Smartstore's EventBroker
 * - Phones: sticky add-to-cart (product) and checkout (cart) bars
 */
(function () {
    'use strict';

    var doc = document.documentElement;
    var mq = function (q) { return window.matchMedia ? window.matchMedia(q) : { matches: false }; };
    var reduceMotion = mq('(prefers-reduced-motion: reduce)').matches;
    // Parallax only where the CSS applies it (lg+) and on devices that hover (no touch scrolling jank)
    var desktopMq = mq('(min-width: 992px) and (hover: hover)');
    var phoneMq = mq('(max-width: 767.98px)');

    window.MotorTheme = { version: '1.0' };
    doc.classList.add('motor-js');

    function onReady(fn) {
        if (document.readyState !== 'loading') {
            fn();
        }
        else {
            document.addEventListener('DOMContentLoaded', fn);
        }
    }

    // Header: height, scrolled state, progress
    // ---------------------------------------------

    function initHeader() {
        var header = document.getElementById('header');
        if (!header) return;

        var body = document.body;
        var ticking = false;
        // Photos that drift slower than the page (data-motor-parallax="<factor>")
        var layers = reduceMotion ? [] : Array.prototype.map.call(document.querySelectorAll('[data-motor-parallax]'), function (el) {
            return {
                el: el,
                factor: parseFloat(el.getAttribute('data-motor-parallax')) || 0.1,
                frame: el.closest('section') || el.parentElement,
                pinned: !!el.closest('[data-motor-hero]')
            };
        });

        function measure() {
            doc.style.setProperty('--motor-header-h', header.offsetHeight + 'px');
        }

        function update() {
            ticking = false;

            var y = window.scrollY || window.pageYOffset;
            var max = Math.max(1, doc.scrollHeight - window.innerHeight);

            body.classList.toggle('motor-scrolled', y > 8);
            header.style.setProperty('--motor-progress', Math.min(1, y / max).toFixed(4));

            if (!layers.length || !desktopMq.matches) return;

            var vh = window.innerHeight;
            layers.forEach(function (layer) {
                var rect = layer.frame.getBoundingClientRect();
                if (rect.bottom < 0 || rect.top > vh) return;

                // The hero starts at rest and drifts as it scrolls away; other sections
                // drift around the viewport centre.
                var offset = layer.pinned
                    ? Math.max(0, -rect.top) * layer.factor
                    : -(rect.top + rect.height / 2 - vh / 2) * layer.factor;

                layer.el.style.setProperty('--motor-parallax', offset.toFixed(1) + 'px');
            });
        }

        function requestUpdate() {
            if (!ticking) {
                ticking = true;
                window.requestAnimationFrame(update);
            }
        }

        measure();
        update();

        window.addEventListener('scroll', requestUpdate, { passive: true });
        window.addEventListener('resize', function () {
            measure();
            requestUpdate();
        }, { passive: true });

        if ('ResizeObserver' in window) {
            new ResizeObserver(measure).observe(header);
        }
    }

    // Reveal & stagger
    // ---------------------------------------------

    function initReveal() {
        var targets = Array.prototype.slice.call(document.querySelectorAll('[data-motor-reveal], [data-motor-stagger]'));
        if (!targets.length) return;

        targets.forEach(function (el) {
            if (el.hasAttribute('data-motor-stagger')) {
                Array.prototype.forEach.call(el.children, function (child, i) {
                    child.style.setProperty('--motor-i', Math.min(i, 12));
                });
            }
        });

        if (reduceMotion || !('IntersectionObserver' in window)) {
            targets.forEach(function (el) { el.classList.add('is-inview'); });
            return;
        }

        var io = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    entry.target.classList.add('is-inview');
                    io.unobserve(entry.target);
                }
            });
        }, { rootMargin: '0px 0px -8% 0px', threshold: 0.08 });

        targets.forEach(function (el) { io.observe(el); });
    }

    // Hero
    // ---------------------------------------------

    function initHero() {
        var hero = document.querySelector('[data-motor-hero]');
        if (!hero) return;

        // The hero is visible from the first paint; once the photo has loaded (or after a
        // short fallback) a single light pass crosses it (CSS, transform/opacity only).
        var started = false;
        function start() {
            if (started) return;
            started = true;
            window.requestAnimationFrame(function () {
                hero.classList.add('is-ready');
            });
        }

        var photo = hero.querySelector('.motor-hero-photo');
        if (!photo || photo.complete) {
            start();
        }
        else {
            photo.addEventListener('load', start, { once: true });
            photo.addEventListener('error', start, { once: true });
            setTimeout(start, 1500);
        }

        if ('IntersectionObserver' in window) {
            new IntersectionObserver(function (entries) {
                hero.classList.toggle('is-offscreen', !entries[0].isIntersecting);
            }).observe(hero);
        }

        // Smooth in-page jumps from hero CTAs, offset by the sticky header
        hero.addEventListener('click', function (e) {
            var link = e.target.closest('a[href^="#"]');
            if (!link) return;

            var target = document.querySelector(link.getAttribute('href'));
            if (!target) return;

            e.preventDefault();
            var offset = (parseInt(getComputedStyle(doc).getPropertyValue('--motor-header-h'), 10) || 0) + 16;
            var top = target.getBoundingClientRect().top + window.pageYOffset - offset;

            window.scrollTo({ top: top, behavior: reduceMotion ? 'auto' : 'smooth' });
            if (history.replaceState) {
                history.replaceState(null, '', link.getAttribute('href'));
            }
        });
    }

    // Add-to-cart / wishlist feedback
    // ---------------------------------------------

    function initCartFeedback() {
        if (!window.EventBroker || !window.EventBroker.subscribe) return;

        EventBroker.subscribe('ajaxcart.item.added', function (msg, cmd) {
            var src = cmd && cmd.src && cmd.src[0];
            var type = (cmd && cmd.type) || 'cart';

            if (src && src.classList && src.classList.contains('btn')) {
                src.classList.add('motor-btn-done');
                setTimeout(function () { src.classList.remove('motor-btn-done'); }, 1400);

                var card = src.closest('.art');
                if (card && !reduceMotion) {
                    card.classList.remove('motor-added');
                    void card.offsetWidth;
                    card.classList.add('motor-added');
                }
            }

            if (!reduceMotion) {
                var badge = document.querySelector('#shopbar-' + type + ' .badge-counter');
                if (badge) {
                    badge.classList.remove('motor-badge-bump');
                    void badge.offsetWidth;
                    badge.classList.add('motor-badge-bump');
                }
            }
        });
    }

    // Sticky action bar (phones)
    // ---------------------------------------------
    // Mirrors the page's own primary action (add to cart / checkout) while that button is
    // scrolled out of view. Label and amount are read from the already localized page, and a
    // tap clicks the original button, so Smartstore's own validation and AJAX flow still apply.

    function initActionBar(opts) {
        if (!opts.button() || !('IntersectionObserver' in window)) return;

        var bar = document.createElement('div');
        bar.className = 'motor-buybar motor-buybar--' + opts.kind;
        bar.innerHTML =
            '<div class="motor-buybar-info">' +
                '<span class="motor-buybar-label"></span>' +
                '<span class="motor-buybar-amount"></span>' +
            '</div>' +
            '<button type="button" class="btn btn-primary motor-buybar-btn"></button>';
        document.body.appendChild(bar);

        var label = bar.querySelector('.motor-buybar-label');
        var amount = bar.querySelector('.motor-buybar-amount');
        var btn = bar.querySelector('.motor-buybar-btn');
        var observed = null;
        var visible = false;

        function text(el) {
            return el ? el.textContent.replace(/\s+/g, ' ').trim() : '';
        }

        function sync() {
            var source = opts.button();
            btn.textContent = text(source && (source.querySelector('span') || source));
            btn.disabled = !source || source.disabled;
            label.textContent = opts.label();
            amount.textContent = opts.amount();
        }

        function setVisible(show) {
            show = show && phoneMq.matches;
            if (show === visible) return;
            visible = show;
            if (show) sync();
            bar.classList.toggle('is-visible', show);
            document.body.classList.toggle('motor-buybar-shown', show);
        }

        var io = new IntersectionObserver(function (entries) {
            // Show the bar while the original button is out of view
            setVisible(!entries[entries.length - 1].isIntersecting);
        });

        function observe() {
            var source = opts.button();
            if (source === observed) return;
            if (observed) io.unobserve(observed);
            observed = source;
            if (source) {
                io.observe(source);
            }
            else {
                setVisible(false);
            }
        }

        btn.addEventListener('click', function () {
            // A required choice is still open (e.g. size): take the shopper there instead of
            // answering with an error message about a field they cannot see.
            var pending = opts.pendingChoice && opts.pendingChoice();
            if (pending) {
                pending.scrollIntoView({ block: 'center', behavior: reduceMotion ? 'auto' : 'smooth' });
                var control = pending.querySelector('input:not([type=hidden]), select, textarea');
                if (control) control.focus({ preventScroll: true });
                pending.classList.remove('motor-attention');
                void pending.offsetWidth;
                pending.classList.add('motor-attention');
                setTimeout(function () { pending.classList.remove('motor-attention'); }, 2000);
                return;
            }

            var source = opts.button();
            if (source && !source.disabled) {
                source.click();
            }
        });

        observe();

        // Smartstore re-renders the offer/summary via AJAX (variant change, quantity update):
        // rebind to the new button and refresh the amount.
        if ('MutationObserver' in window && opts.root()) {
            var pending = false;
            new MutationObserver(function () {
                if (pending) return;
                pending = true;
                window.requestAnimationFrame(function () {
                    pending = false;
                    observe();
                    if (visible) sync();
                });
            }).observe(opts.root(), { childList: true, subtree: true });
        }

        if (phoneMq.addEventListener) {
            phoneMq.addEventListener('change', function () {
                if (!phoneMq.matches) setVisible(false);
                else if (observed) { io.unobserve(observed); io.observe(observed); }
            });
        }
    }

    function initActionBars() {
        var form = document.getElementById('pd-form');
        if (form) {
            initActionBar({
                kind: 'product',
                root: function () { return form; },
                button: function () { return form.querySelector('.pd-offer-actions .btn-add-to-cart'); },
                label: function () {
                    var name = document.querySelector('.pd-info-mobile .pd-name') || document.querySelector('.pd-name');
                    return name ? name.textContent.replace(/\s+/g, ' ').trim() : '';
                },
                amount: function () {
                    var price = form.querySelector('.pd-offer-price .pd-finalprice-amount');
                    return price ? price.textContent.replace(/\s+/g, ' ').trim() : '';
                },
                pendingChoice: function () {
                    // First required variant/attribute group without a value
                    var groups = form.querySelectorAll('.pd-variants .form-group');
                    for (var i = 0; i < groups.length; i++) {
                        var g = groups[i];
                        if (!g.querySelector('.required')) continue;

                        var options = g.querySelectorAll('input[type=radio], input[type=checkbox]');
                        if (options.length) {
                            if (!Array.prototype.some.call(options, function (o) { return o.checked; })) return g;
                            continue;
                        }

                        var field = g.querySelector('select, input:not([type=hidden]), textarea');
                        if (field && !field.value) return g;
                    }
                    return null;
                }
            });
            return;
        }

        var checkout = document.getElementById('checkout');
        if (checkout && checkout.closest('.shopping-cart-page')) {
            initActionBar({
                kind: 'cart',
                root: function () { return document.getElementById('content-center') || document.body; },
                button: function () { return document.getElementById('checkout'); },
                label: function () {
                    var l = document.querySelector('.cart-summary-total .cart-summary-label');
                    return l ? l.textContent.replace(/\s+/g, ' ').trim() : '';
                },
                amount: function () {
                    var v = document.querySelector('.cart-summary-total .cart-summary-value');
                    return v ? v.textContent.replace(/\s+/g, ' ').trim() : '';
                }
            });
        }
    }

    // Vehicle compatibility summary (MotorShop.Fitment)
    // ---------------------------------------------
    // The module renders compatibility as the last product tab, which phones show as a collapsed
    // accordion at the bottom. Surface its status under the product title; a tap opens the tab.
    // All text comes from the module's own (localized) tab content.

    function initFitmentSummary() {
        var pane = document.querySelector('.tab-pane[data-tab-name="pd-motor-fitment"]');
        if (!pane || !pane.id) return;

        var alert = pane.querySelector('.alert');
        var tabLink = document.getElementById(pane.id + '-tab');
        var caption = tabLink && tabLink.querySelector('.tab-caption');
        var count = pane.querySelectorAll('tbody tr').length;
        var state = !alert ? 'info' : (alert.classList.contains('alert-success') ? 'ok' : 'warn');
        var label = alert
            ? alert.textContent.replace(/\s+/g, ' ').trim()
            : (caption ? caption.textContent.trim() : '') + (count ? ' (' + count + ')' : '');

        if (!label) return;

        function open(e) {
            e.preventDefault();
            var toggler = pane.querySelector('.nav-toggler');
            var collapsible = toggler && toggler.offsetParent !== null;

            if (collapsible) {
                // Phones: accordion
                if (toggler.classList.contains('collapsed')) toggler.click();
            }
            else if (tabLink && window.jQuery && jQuery.fn.tab) {
                jQuery(tabLink).tab('show');
            }
            else if (tabLink) {
                tabLink.click();
            }

            setTimeout(function () {
                pane.scrollIntoView({ block: 'start', behavior: reduceMotion ? 'auto' : 'smooth' });
            }, 50);
        }

        ['.pd-info-mobile', '.pd-info-container .pd-info'].forEach(function (selector) {
            var host = document.querySelector(selector);
            if (!host) return;

            var link = document.createElement('a');
            link.href = '#' + pane.id;
            link.className = 'motor-fit-status motor-fit-status--' + state;
            link.innerHTML = '<i class="fa fa-motorcycle" aria-hidden="true"></i><span></span>';
            link.querySelector('span').textContent = label;
            link.addEventListener('click', open);
            host.appendChild(link);
        });
    }

    onReady(function () {
        initHeader();
        initReveal();
        initHero();
        initCartFeedback();
        initActionBars();
        initFitmentSummary();
    });
})();
