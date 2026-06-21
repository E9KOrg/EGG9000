// Lazily loads the MyFarms inventory image, lays a grid of invisible hover targets over it, and drives
// the rich hover tooltip shared with the artifact-combos tiles.
//
// The server hands back a JPEG (base64) plus a manifest: a list of hotspots whose positions are
// expressed as percentages of the image size. Because the image scales responsively, percentage
// hotspots scale right along with it, so the hover targets always line up with the artwork without any
// resize math here. Each hotspot also carries a chunk of tooltip HTML (name, rarity, effect, stones)
// which we stash in a hidden child element and surface through the floating tooltip below.
(function () {
    // -- Floating tooltip ---------------------------------------------------------------------------
    // One element reused for every hover target on the page. Any element with class .has-tip that
    // contains a hidden .afx-tip-content child shows that content on hover.
    //
    // The tooltip is ANCHORED to the hovered element, not the cursor. A cursor-following tooltip lags a
    // frame behind the pointer and visibly chases it, which reads as jitter no matter how cheap the math
    // is. Anchoring positions the tooltip once on enter and then leaves it alone while the pointer moves
    // within the item, so there is zero per-move work and nothing to stutter.
    var tip = null;
    var active = null;   // the .has-tip currently being hovered

    function ensureTip() {
        if (tip) return tip;
        tip = document.createElement('div');
        tip.className = 'afx-tooltip';
        tip.style.display = 'none';
        tip.style.left = '0';
        tip.style.top = '0';
        document.body.appendChild(tip);
        return tip;
    }

    function showTipFor(el, html) {
        var t = ensureTip();
        t.innerHTML = html;
        t.style.display = 'block';

        var r = el.getBoundingClientRect();
        var tw = t.offsetWidth;
        var th = t.offsetHeight;
        var pad = 8;

        // Prefer directly above the item, left-aligned to it; drop below when there's no room above, and
        // nudge horizontally so we never spill off the viewport.
        var left = r.left;
        var top = r.top - th - pad;
        if (top < 4) top = r.bottom + pad;
        if (left + tw > window.innerWidth - 4) left = window.innerWidth - 4 - tw;
        if (left < 4) left = 4;

        t.style.transform = 'translate3d(' + Math.round(left) + 'px,' + Math.round(top) + 'px,0)';
    }

    function hideTip() {
        active = null;
        if (tip) tip.style.display = 'none';
    }

    function tipHtmlFor(el) {
        var holder = el.querySelector(':scope > .afx-tip-content');
        return holder ? holder.innerHTML : '';
    }

    document.addEventListener('mouseover', function (e) {
        var el = e.target.closest ? e.target.closest('.has-tip') : null;
        if (!el || el === active) return;
        var html = tipHtmlFor(el);
        if (html) {
            active = el;
            showTipFor(el, html);
        }
    });

    document.addEventListener('mouseout', function (e) {
        var from = e.target.closest ? e.target.closest('.has-tip') : null;
        if (!from) return;
        var to = e.relatedTarget && e.relatedTarget.closest ? e.relatedTarget.closest('.has-tip') : null;
        if (to !== from) hideTip();
    });

    // -- Inventory overlay --------------------------------------------------------------------------
    function buildOverlay(container, data) {
        container.innerHTML = '';

        if (!data || data.error) {
            container.textContent = (data && data.error) ? data.error : 'Could not load inventory.';
            return;
        }

        var manifest = data.manifest || {};
        var hotspots = manifest.hotspots || [];

        var wrap = document.createElement('div');
        wrap.className = 'afx-overlay-wrap';

        var img = document.createElement('img');
        img.className = 'afx-overlay-img';
        img.alt = 'Artifact inventory';
        img.src = 'data:image/jpeg;base64,' + data.imageB64;
        wrap.appendChild(img);

        hotspots.forEach(function (h) {
            var spot = document.createElement('div');
            spot.className = 'afx-hotspot has-tip';
            spot.style.left = h.x + '%';
            spot.style.top = h.y + '%';
            spot.style.width = h.w + '%';
            spot.style.height = h.h + '%';

            var content = document.createElement('span');
            content.className = 'afx-tip-content';
            content.innerHTML = h.tip || '';
            spot.appendChild(content);

            wrap.appendChild(spot);
        });

        container.appendChild(wrap);
    }

    function load(container) {
        if (container.getAttribute('data-loaded') === 'true' || container.getAttribute('data-loading') === 'true') {
            return;
        }
        var eid = container.getAttribute('data-eid');
        if (!eid) {
            return;
        }

        container.setAttribute('data-loading', 'true');
        container.textContent = 'Loading inventory...';

        fetch('/MyFarms/InventoryOverlay?eid=' + encodeURIComponent(eid), {
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
            .then(function (r) { return r.json(); })
            .then(function (d) {
                container.setAttribute('data-loaded', 'true');
                container.removeAttribute('data-loading');
                buildOverlay(container, d);
            })
            .catch(function () {
                container.removeAttribute('data-loading');
                container.textContent = 'Could not load inventory.';
            });
    }

    // Load any inventory container that is currently visible (its tab is the active one). offsetParent
    // is null for elements inside a hidden tab-pane, which is exactly how we tell "this tab is open".
    function scan() {
        var containers = document.querySelectorAll('.afx-inventory');
        for (var i = 0; i < containers.length; i++) {
            if (containers[i].offsetParent !== null) {
                load(containers[i]);
            }
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        if (window.jQuery) {
            // Bootstrap fires this on the tab link once its pane is shown.
            window.jQuery(document).on('shown.bs.tab', scan);
        }
        // Covers the case where an inventory tab is already the active one on first paint.
        scan();
    });

    window.ArtifactOverlay = { scan: scan };
})();
