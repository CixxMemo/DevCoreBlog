// Keep loading failures visible even when the editor dependency chunk cannot load.
const trial = document.querySelector('[data-tiptap-trial]');
if (trial) {
    try {
        const { mountTrial } = await import('./editor.js');
        mountTrial(trial);
    } catch {
        trial.querySelector('[data-editor-status]').textContent =
            'Editör açılamadı. Sayfayı yeniden yükleyip tekrar deneyin.';
    }
}
