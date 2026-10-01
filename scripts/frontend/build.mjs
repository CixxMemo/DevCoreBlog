import { mkdir, copyFile, cp, rm } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';

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
await cp(resolve(root, 'frontend/vendor/toastui'), resolve(output, 'toastui'), { recursive: true });
await copyFile(resolve(root, 'node_modules/tailwindcss/LICENSE'), resolve(output, 'TAILWIND-LICENSE'));
console.log('Frontend assets built: Tailwind 3.4.17, Prism 1.30.0.');
