import { clearConfirmedRecovery } from './post-recovery.js';

const receipt = document.getElementById('post-recovery-receipt');
if (receipt) {
    const storage = {
        getItem: key => window.localStorage.getItem(key),
        removeItem: key => window.localStorage.removeItem(key)
    };
    const cleared = await clearConfirmedRecovery({ key: receipt.dataset.recoveryKey,
        revision: receipt.dataset.recoveryRevision, storage, locks: navigator.locks });
    receipt.textContent = cleared ? 'Post saved. Its local recovery copy was cleared.'
        : 'Post saved. Any newer or unavailable local recovery copy was kept.';
}
