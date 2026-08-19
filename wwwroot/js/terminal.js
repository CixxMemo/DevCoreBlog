// terminal.js - Interactive CLI Parser (Phase 3)

document.addEventListener('DOMContentLoaded', () => {
    const cliInput = document.getElementById('cli-input');
    if (!cliInput) return;

    // Command History (Persist across page loads)
    let history = [];
    let historyIndex = -1;

    try {
        const savedHistory = sessionStorage.getItem('cli_history');
        if (savedHistory) {
            history = JSON.parse(savedHistory);
            historyIndex = history.length;
        }
    } catch (e) { }

    // Output area - prepended to main content
    let outputArea = document.getElementById('terminal-output');
    if (!outputArea) {
        outputArea = document.createElement('div');
        outputArea.id = 'terminal-output';
        outputArea.className = 'font-mono text-sm mb-6';
        const content = document.querySelector('.site-content');
        if (content) {
            content.prepend(outputArea);
        }
    }

    // Restore saved output
    const savedOutput = sessionStorage.getItem('cli_output');
    if (savedOutput) {
        outputArea.innerHTML = savedOutput;
    }

    function saveState() {
        sessionStorage.setItem('cli_history', JSON.stringify(history));
        sessionStorage.setItem('cli_output', outputArea.innerHTML);
    }

    function printOutput(text, isError = false) {
        const line = document.createElement('div');
        line.style.color = isError ? '#ef4444' : 'var(--text-secondary)';
        line.style.marginBottom = '4px';
        line.innerHTML = text;
        outputArea.appendChild(line);
        saveState();
    }

    function printCommand(cmd) {
        const line = document.createElement('div');
        line.style.color = 'var(--text-primary)';
        line.style.marginBottom = '4px';
        line.innerHTML = `<span style="color: var(--accent);">devcore:~$</span> ${cmd}`;
        outputArea.appendChild(line);
        saveState();
    }

    cliInput.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') {
            const val = cliInput.value.trim();
            if (val) {
                history.push(val);
                historyIndex = history.length;
                printCommand(val);
                parseCommand(val);
            }
            cliInput.value = '';
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            if (historyIndex > 0) {
                historyIndex--;
                cliInput.value = history[historyIndex];
            }
        } else if (e.key === 'ArrowDown') {
            e.preventDefault();
            if (historyIndex < history.length - 1) {
                historyIndex++;
                cliInput.value = history[historyIndex];
            } else {
                historyIndex = history.length;
                cliInput.value = '';
            }
        }
    });

    async function parseCommand(cmd) {
        const parts = cmd.split(' ').filter(Boolean);
        const command = parts[0].toLowerCase();
        const args = parts.slice(1);

        switch (command) {
            case 'help':
                printOutput(`
                    <table style="width: 100%; max-width: 500px; margin-top: 8px; margin-bottom: 8px;">
                        <tr><td style="color: var(--accent); padding-right: 16px;">help</td><td>Mevcut komutları listeler</td></tr>
                        <tr><td style="color: var(--accent); padding-right: 16px;">ls</td><td>Kategorileri listeler</td></tr>
                        <tr><td style="color: var(--accent); padding-right: 16px;">cd &lt;slug&gt;</td><td>Belirtilen kategoriye gider (örn: cd csharp)</td></tr>
                        <tr><td style="color: var(--accent); padding-right: 16px;">clear</td><td>Terminal ekranını temizler</td></tr>
                        <tr><td style="color: var(--accent); padding-right: 16px;">home</td><td>Ana sayfaya döner</td></tr>
                    </table>
                `);
                break;
            case 'clear':
                outputArea.innerHTML = '';
                saveState();
                break;
            case 'ls':
                try {
                    const res = await fetch('/api/categories');
                    if (res.ok) {
                        const categories = await res.json();
                        let html = '<div style="display: flex; gap: 1rem; flex-wrap: wrap; margin-top: 8px; margin-bottom: 8px;">';
                        categories.forEach(c => {
                            html += `<span style="color: #3b82f6;">${c.slug}/</span>`;
                        });
                        html += '</div>';
                        printOutput(html);
                    } else {
                        printOutput('Kategoriler alınamadı. (Endpoint henüz hazır olmayabilir)', true);
                    }
                } catch (err) {
                    printOutput('ls komutu başarısız: API sunucusuna ulaşılamadı.', true);
                }
                break;
            case 'cd':
                if (args.length === 0 || args[0] === '~') {
                    window.location.href = '/';
                } else if (args[0] === '..') {
                    window.history.back();
                } else {
                    // Kullanıcı 'cd <Technology news>' yazmış olabilir.
                    // Argümanları birleştirip, gereksiz karakterleri temizleyip slug formatına getirelim.
                    let rawSlug = args.join('-');
                    let cleanSlug = rawSlug.replace(/[<>]/g, '').toLowerCase().trim();
                    window.location.href = '/kategori/' + cleanSlug;
                }
                break;
            case 'home':
                window.location.href = '/';
                break;
            default:
                printOutput(`bash: ${command}: komut bulunamadı. Komutları görmek için 'help' yazın.`, true);
                break;
        }
    }
});
