/*
 * MotorDark theme enhancements.
 * Progressive enhancement only: the storefront works fully without this file.
 * - Sticky header state, scroll progress and measured header height
 * - Scroll reveal and staggered entrances (IntersectionObserver)
 * - Hero entrance and photo parallax
 * - Add-to-cart feedback hooked into Smartstore's EventBroker
 */
(function () {
    'use strict';

    var doc = document.documentElement;
    var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

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

        // Start the entrance once the photo is decoded (or after a short fallback),
        // so the "develop from black" transition never plays over an empty frame.
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

    onReady(function () {
        initHeader();
        initReveal();
        initHero();
        initCartFeedback();
    });
})();
