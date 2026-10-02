// Enhance existing code blocks without altering trusted article HTML or code text.
for (const [index, pre] of [...document.querySelectorAll('.reading-article .markdown-content pre')].entries()) {
    const code = pre.querySelector('code');
    if (!code) continue;
    pre.tabIndex = 0;
    pre.setAttribute('aria-label', `Code block ${index + 1}`);
    const controls = document.createElement('div');
    controls.className = 'code-copy-controls';
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = 'Copy';
    button.setAttribute('aria-label', `Copy code block ${index + 1}`);
    const status = document.createElement('span');
    status.setAttribute('role', 'status');
    status.setAttribute('aria-live', 'polite');
    button.addEventListener('click', async () => {
        button.disabled = true;
        try {
            await navigator.clipboard.writeText(code.textContent ?? '');
            status.textContent = 'Copied.';
        } catch {
            status.textContent = 'Could not copy. Select the code and copy it manually.';
        } finally {
            button.disabled = false;
        }
    });
    controls.append(button, status);
    (pre.closest('.code-toolbar') ?? pre).before(controls);
}

// Wide tables remain keyboard-scrollable without replacing their native semantics.
for (const [index, table] of [...document.querySelectorAll('.reading-article .markdown-content table')].entries()) {
    table.tabIndex = 0;
    table.setAttribute('aria-label', `Article table ${index + 1}`);
}
