/*
 * MotorDark theme enhancements.
 * Progressive enhancement only: the storefront works fully without this file.
 * - Sticky header state, scroll progress and measured header height
 * - Scroll reveal and staggered entrances (IntersectionObserver)
 * - Hero entrance and light parallax
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
        var hero = document.querySelector('[data-motor-hero]');
        var bike = hero ? hero.querySelector('.motor-bike') : null;

        function measure() {
            doc.style.setProperty('--motor-header-h', header.offsetHeight + 'px');
        }

        function update() {
            ticking = false;

            var y = window.scrollY || window.pageYOffset;
            var max = Math.max(1, doc.scrollHeight - window.innerHeight);

            body.classList.toggle('motor-scrolled', y > 8);
            header.style.setProperty('--motor-progress', Math.min(1, y / max).toFixed(4));

            if (bike && !reduceMotion) {
                var rect = hero.getBoundingClientRect();
                if (rect.bottom > 0) {
                    bike.style.setProperty('--motor-parallax', (Math.max(0, -rect.top) * 0.12).toFixed(1) + 'px');
                }
            }
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

        window.requestAnimationFrame(function () {
            window.requestAnimationFrame(function () {
                hero.classList.add('is-ready');
            });
        });

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
