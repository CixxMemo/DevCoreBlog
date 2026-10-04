// Scratchpad Auto-Persistence & Draft Creation Logic
// Saves user notes directly to browser localStorage on every input event.
document.addEventListener('DOMContentLoaded', function () {
    const STORAGE_KEY = 'devcore_admin_scratchpad';
    const textarea = document.getElementById('dashboard-scratchpad');
    const statusBadge = document.getElementById('scratchpad-status');
    const charCount = document.getElementById('scratchpad-char-count');
    const clearBtn = document.getElementById('scratchpad-clear-btn');
    const createDraftBtn = document.getElementById('scratchpad-create-draft-btn');

    if (!textarea) return;

    // Load persisted note from localStorage
    const savedNote = localStorage.getItem(STORAGE_KEY);
    if (savedNote) {
        textarea.value = savedNote;
        updateMetrics();
    }

    // Save on keystroke with instant visual feedback
    textarea.addEventListener('input', function () {
        localStorage.setItem(STORAGE_KEY, textarea.value);
        statusBadge.textContent = 'SAVED';
        statusBadge.className = 'font-mono text-[10px] font-bold text-emerald-600 bg-emerald-50 border border-emerald-300 px-1.5 py-0.5';
        updateMetrics();
    });

    function updateMetrics() {
        const len = textarea.value.length;
        charCount.textContent = `${len} character${len !== 1 ? 's' : ''}`;
    }

    // Clear button handler
    clearBtn.addEventListener('click', function () {
        if (textarea.value.trim() && confirm('Clear scratchpad note?')) {
            textarea.value = '';
            localStorage.removeItem(STORAGE_KEY);
            updateMetrics();
            if (window.showToast) window.showToast('Scratchpad cleared.', 'info');
        }
    });

    // Create Draft from Note handler
    createDraftBtn.addEventListener('click', function () {
        const note = textarea.value.trim();
        if (!note) {
            if (window.showToast) window.showToast('Please type a note in the scratchpad first.', 'error');
            return;
        }

        // Store note in the editor draft cache for /AdminPost/Create to restore
        localStorage.setItem('devcore_editor_scratch_transfer', note);

        if (window.showToast) {
            window.showToast('Transferring note to post editor...', 'success');
        }

        // Redirect to post create page
        setTimeout(() => {
            window.location.href = '/AdminPost/Create';
        }, 400);
    });
});
