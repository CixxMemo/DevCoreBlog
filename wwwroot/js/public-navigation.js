// Progressive enhancement: the server's navigation and GET search need no script.
(() => {
    const sidebar = document.getElementById('main-sidebar');
    const toggle = document.getElementById('mobile-menu-btn');
    const close = document.getElementById('mobile-close-btn');
    const backdrop = document.getElementById('sidebar-backdrop');
    const main = document.getElementById('main-content');
    const header = document.getElementById('public-header');
    const skip = document.getElementById('skip-content');
    const search = document.getElementById('global-search-input');
    if (!sidebar || !toggle || !close || !backdrop || !main || !header || !skip) return;

    const mobile = window.matchMedia('(max-width: 767.98px)');
    const background = [main, header, skip];
    let isOpen = false;
    let lastFocused = document.activeElement;

    function focusableItems() {
        return [...sidebar.querySelectorAll('a[href], button:not([disabled]), [tabindex="0"]')]
            .filter(item => item.getClientRects().length > 0);
    }

    // One state transition owns visibility, semantics, scroll and focus boundaries.
    function setOpen(nextOpen, restoreFocus = false) {
        isOpen = mobile.matches && nextOpen;
        sidebar.classList.toggle('is-open', isOpen);
        toggle.setAttribute('aria-expanded', String(isOpen));
        background.forEach(element => { element.inert = isOpen; });
        document.body.classList.toggle('public-drawer-open', isOpen);

        if (isOpen) {
            sidebar.inert = false;
            sidebar.removeAttribute('aria-hidden');
            sidebar.setAttribute('role', 'dialog');
            sidebar.setAttribute('aria-modal', 'true');
            close.focus({ preventScroll: true });
        } else {
            sidebar.setAttribute('role', 'complementary');
            sidebar.removeAttribute('aria-modal');
            if (restoreFocus) toggle.focus({ preventScroll: true });
            sidebar.inert = mobile.matches;
            if (mobile.matches) sidebar.setAttribute('aria-hidden', 'true');
            else sidebar.removeAttribute('aria-hidden');
        }
    }

    toggle.addEventListener('click', () => setOpen(true));
    close.addEventListener('click', () => setOpen(false, true));
    backdrop.addEventListener('click', () => setOpen(false, true));

    document.addEventListener('keydown', event => {
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k' && search) {
            event.preventDefault();
            if (isOpen) setOpen(false);
            search.focus();
            search.select();
            return;
        }
        if (!isOpen) return;
        if (event.key === 'Escape') {
            event.preventDefault();
            setOpen(false, true);
            return;
        }
        if (event.key !== 'Tab') return;
        const items = focusableItems();
        const first = items[0];
        const last = items[items.length - 1];
        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last?.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first?.focus();
        }
    });

    document.addEventListener('focusin', event => {
        lastFocused = event.target;
        if (isOpen && !sidebar.contains(event.target)) close.focus({ preventScroll: true });
    });

    mobile.addEventListener('change', () => {
        // CSS may hide the focused element before the media-query event runs.
        const focusedInSidebar = sidebar.contains(lastFocused);
        const focusedOnToggle = lastFocused === toggle;
        const focusedOnClose = lastFocused === close;
        setOpen(false, mobile.matches && focusedInSidebar);
        if (!mobile.matches && (focusedOnToggle || focusedOnClose)) {
            sidebar.querySelector('nav a[href]')?.focus({ preventScroll: true });
        }
    });

    document.documentElement.classList.add('public-navigation-ready');
    setOpen(false);
})();
