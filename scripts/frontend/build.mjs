import { mkdir, copyFile, cp, rm, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
import { buildEditor, sha256 } from './editor-bundle.mjs';
import { buildTiptap } from './tiptap-bundle.mjs';

// Resolve paths from this file so the same command works in MSBuild and npm.
const root = fileURLToPath(new URL('../../', import.meta.url));
const output = resolve(root, 'wwwroot/generated');
await rm(output, { recursive: true, force: true });
await mkdir(output, { recursive: true });
for (const theme of ['public', 'admin']) {
    execFileSync(process.execPath, [resolve(root, 'node_modules/tailwindcss/lib/cli.js'),
        '-c', `frontend/tailwind.${theme}.cjs`, '-i', 'frontend/tailwind.css',
        '-o', `wwwroot/generated/tailwind-${theme}.css`, '--minify'],
        { cwd: root, stdio: 'inherit' });
}

// Ship upstream files unchanged, with their licenses and autoloaded grammars.
for (const [name, source, files] of [
    ['prism', 'prismjs', ['components', 'plugins/autoloader', 'plugins/toolbar',
        'plugins/show-language', 'themes/prism-tomorrow.css', 'LICENSE']]
]) {
    const destination = resolve(output, name);
    await mkdir(destination, { recursive: true });
    for (const file of files) {
        await cp(resolve(root, 'node_modules', source, file), resolve(destination, file), { recursive: true });
    }
}
// Only reviewed distribution files ship; the old upstream sanitizer is build input.
const editorOutput = resolve(output, 'toastui');
await mkdir(editorOutput, { recursive: true });
for (const file of ['toastui-editor.min.css', 'LICENSE', 'PROSEMIRROR-LICENSE', 'README.md']) {
    await copyFile(resolve(root, 'frontend/vendor/toastui', file), resolve(editorOutput, file));
}
await copyFile(resolve(root, 'node_modules/dompurify/LICENSE'), resolve(editorOutput, 'DOMPURIFY-LICENSE'));
const editor = await buildEditor(new URL('../../', import.meta.url));
await writeFile(resolve(editorOutput, 'toastui-editor-all.min.js'), editor);
await writeFile(resolve(editorOutput, 'build.json'), JSON.stringify({
    editor: '3.2.2', dompurify: '3.4.16', sha256: sha256(editor)
}, null, 2) + '\n');
await copyFile(resolve(root, 'node_modules/tailwindcss/LICENSE'), resolve(output, 'TAILWIND-LICENSE'));
await buildTiptap(root, resolve(output, 'tiptap'));
console.log('Frontend assets built: Tailwind 3.4.17, Prism 1.30.0, Tiptap 3.31.4.');
