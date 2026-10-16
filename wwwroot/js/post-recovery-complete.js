import { clearConfirmedRecovery } from './post-recovery.js';

const receipt = document.getElementById('post-recovery-receipt');
if (receipt) {
    const storage = {
        getItem: key => window.localStorage.getItem(key),
        removeItem: key => window.localStorage.removeItem(key)
    };
    const cleared = await clearConfirmedRecovery({ key: receipt.dataset.recoveryKey,
        revision: receipt.dataset.recoveryRevision, storage, locks: navigator.locks });
    receipt.textContent = cleared ? 'Yazı kaydedildi. Yerel kurtarma kopyası temizlendi.'
        : 'Yazı kaydedildi. Daha yeni veya erişilemeyen yerel kurtarma kopyası korundu.';
}
