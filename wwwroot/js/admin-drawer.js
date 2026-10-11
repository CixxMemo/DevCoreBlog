(() => {
    const sidebar = document.getElementById('admin-sidebar');
    const toggle = document.getElementById('admin-menu-toggle');
    const pageContent = document.getElementById('admin-page-content');
    const header = document.querySelector('.admin-header');
    const backdrop = document.getElementById('admin-drawer-backdrop');

    if (!sidebar || !toggle || !pageContent || !header || !backdrop) return;

    // Match the rendered navigation control, including Safari's scrollbar boundary.
    const isMobileLayout = () => toggle.getClientRects().length > 0;
    let mobileLayout = isMobileLayout();
    let isOpen = false;
    let returnFocusToToggle = false;

    function getFocusableItems() {
        return [...sidebar.querySelectorAll(
            'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
        )].filter(item => item.getClientRects().length > 0);
    }

    function setOpen(nextOpen, restoreFocus = false) {
        isOpen = isMobileLayout() && nextOpen;
        sidebar.classList.toggle('is-open', isOpen);
        sidebar.setAttribute('aria-hidden', String(isMobileLayout() && !isOpen));
        sidebar.inert = isMobileLayout() && !isOpen;
        pageContent.inert = isOpen;
        [...header.children].forEach(child => {
            if (child !== toggle) child.inert = isOpen;
        });
        backdrop.classList.toggle('is-visible', isOpen);
        backdrop.tabIndex = isOpen ? 0 : -1;
        toggle.setAttribute('aria-expanded', String(isOpen));
        toggle.setAttribute('aria-label', isOpen ? 'Close admin navigation' : 'Open admin navigation');
        document.body.classList.toggle('admin-drawer-open', isOpen);

        if (isOpen) {
            const [firstItem] = getFocusableItems();
            firstItem?.focus({ preventScroll: true });
        } else if (restoreFocus || returnFocusToToggle) {
            toggle.focus();
        }
        returnFocusToToggle = false;
    }

    toggle.addEventListener('click', () => setOpen(!isOpen, isOpen));
    backdrop.addEventListener('click', () => setOpen(false, true));

    document.addEventListener('keydown', event => {
        if (!isOpen) return;

        if (event.key === 'Escape') {
            event.preventDefault();
            setOpen(false, true);
            return;
        }

        if (event.key !== 'Tab') return;
        const focusableItems = getFocusableItems();
        const firstItem = focusableItems[0];
        const lastItem = focusableItems[focusableItems.length - 1];
        if (!firstItem || !lastItem) {
            event.preventDefault();
            toggle.focus();
        } else if (!sidebar.contains(document.activeElement)) {
            event.preventDefault();
            firstItem.focus({ preventScroll: true });
        } else if (event.shiftKey && document.activeElement === firstItem) {
            event.preventDefault();
            lastItem.focus();
        } else if (!event.shiftKey && document.activeElement === lastItem) {
            event.preventDefault();
            firstItem.focus();
        }
    });

    function syncLayout() {
        const nextMobileLayout = isMobileLayout();
        if (nextMobileLayout === mobileLayout) return;
        mobileLayout = nextMobileLayout;
        returnFocusToToggle = sidebar.contains(document.activeElement);
        setOpen(false, returnFocusToToggle);
    }
    window.addEventListener('resize', syncLayout);
    new ResizeObserver(syncLayout).observe(document.documentElement);

    setOpen(false);
})();
