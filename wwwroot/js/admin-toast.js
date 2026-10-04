// Vanilla JS Toast Notification Engine
// Exposes `window.showToast(message, type)` for global asynchronous feedback.
// Adheres to the Tech Minimal style: sharp borders, monospace font, no blur.
window.showToast = function (message, type = 'info') {
    if (typeof message !== 'string' || message.length === 0) return;

    const container = document.getElementById('admin-toast-container');
    if (!container) return;

    // Create the toast wrapper element
    const toast = document.createElement('div');
    toast.className = 'border-2 border-black bg-white p-3.5 font-mono text-xs font-bold text-black flex items-center justify-between gap-4 pointer-events-auto transition-none';

    // Determine border accent and type badge based on severity
    let typeBadge = '[INFO]';

    if (type === 'success') {
        typeBadge = '[SUCCESS]';
        toast.classList.add('border-l-[6px]', 'border-l-emerald-600');
    } else if (type === 'error') {
        typeBadge = '[ERROR]';
        toast.classList.add('border-l-[6px]', 'border-l-rose-600');
    } else {
        toast.classList.add('border-l-[6px]', 'border-l-black');
    }

    const content = document.createElement('div');
    content.className = 'flex items-center gap-2 min-w-0';

    const badge = document.createElement('span');
    badge.className = 'text-neutral-500 uppercase flex-shrink-0';
    badge.textContent = typeBadge;

    const messageText = document.createElement('span');
    messageText.className = 'truncate';
    messageText.textContent = message;

    const closeButton = document.createElement('button');
    closeButton.type = 'button';
    closeButton.className = 'text-neutral-500 hover:text-black uppercase font-bold text-xs flex-shrink-0 ml-2';
    closeButton.textContent = '[X]';
    closeButton.setAttribute('aria-label', 'Dismiss notification');
    closeButton.addEventListener('click', () => toast.remove());

    content.append(badge, messageText);
    toast.append(content, closeButton);

    // Append toast to the fixed bottom container
    container.appendChild(toast);

    // Auto-dismiss toast after 4000 milliseconds
    setTimeout(() => {
        if (toast && toast.parentElement) {
            toast.remove();
        }
    }, 4000);
};

// Render server-side TempData notifications on page load if present
document.addEventListener('DOMContentLoaded', function () {
    const container = document.getElementById('admin-toast-container');
    const serverSuccess = container?.dataset.success;
    const serverError = container?.dataset.error;
    const serverMessage = container?.dataset.message;

    if (serverSuccess) {
        window.showToast(serverSuccess, 'success');
    }
    if (serverError) {
        window.showToast(serverError, 'error');
    }
    if (serverMessage) {
        window.showToast(serverMessage, 'info');
    }
});
