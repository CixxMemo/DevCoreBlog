// Apply the saved theme before styles load, then bind controls when the DOM is ready.
(() => {
    let savedTheme;
    try { savedTheme = localStorage.getItem('theme'); } catch { /* Storage may be disabled. */ }
    const systemDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    document.documentElement.classList.toggle('dark', savedTheme === 'dark' || (!savedTheme && systemDark));

    function updateIcons() {
        const isDark = document.documentElement.classList.contains('dark');
        for (const icon of document.querySelectorAll('.theme-icon-dark')) icon.classList.toggle('hidden', isDark);
        for (const icon of document.querySelectorAll('.theme-icon-light')) icon.classList.toggle('hidden', !isDark);
    }

    document.addEventListener('DOMContentLoaded', () => {
        updateIcons();
        document.getElementById('theme-toggle-btn')?.addEventListener('click', () => {
            const isDark = document.documentElement.classList.toggle('dark');
            try { localStorage.setItem('theme', isDark ? 'dark' : 'light'); } catch { /* Keep the in-page theme usable. */ }
            updateIcons();
        });
    });
})();
