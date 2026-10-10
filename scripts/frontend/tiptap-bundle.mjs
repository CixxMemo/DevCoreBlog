import { build } from 'esbuild';
import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, relative, resolve } from 'node:path';
import { sha256 } from './editor-bundle.mjs';

// Bundle only local modules and retain the licenses of every package actually included.
export async function buildTiptap(root, output) {
    await mkdir(output, { recursive: true });
    const manifest = JSON.parse(await readFile(resolve(root, 'package.json'), 'utf8'));
    const result = await build({
        absWorkingDir: root, entryPoints: { trial: 'frontend/tiptap/trial.js', form: 'frontend/tiptap/form.js' },
        outdir: output, bundle: true, splitting: true, format: 'esm', platform: 'browser',
        target: 'es2022', minify: true, charset: 'utf8', sourcemap: false,
        chunkNames: 'chunks/[name]-[hash]', legalComments: 'external', metafile: true,
    });
    const directories = new Set([resolve(root, 'node_modules/esbuild')]);
    for (const input of Object.keys(result.metafile.inputs).filter(input => input.startsWith('node_modules/'))) {
        let directory = dirname(resolve(root, input));
        let found = false;
        while (directory !== resolve(root) && directory !== dirname(directory)) {
            try {
                await readFile(resolve(directory, 'package.json'));
                directories.add(directory);
                found = true;
                break;
            } catch (error) {
                if (error.code !== 'ENOENT') throw error;
                directory = dirname(directory);
            }
        }
        if (!found) throw new Error('Bundled package metadata missing.');
    }
    const licenses = [];
    await mkdir(resolve(output, 'licenses'), { recursive: true });
    for (const directory of [...directories].sort()) {
        const pkg = JSON.parse(await readFile(resolve(directory, 'package.json'), 'utf8'));
        if (!['MIT', 'ISC', 'BSD-3-Clause'].includes(pkg.license)) throw new Error('Unreviewed editor package license.');
        if (pkg.name.startsWith('@tiptap/') && pkg.version !== manifest.devDependencies['@tiptap/core'])
            throw new Error('Tiptap package versions differ.');
        if (manifest.devDependencies[pkg.name] && pkg.version !== manifest.devDependencies[pkg.name])
            throw new Error('Editor build package version differs from manifest.');
        let license;
        for (const file of ['LICENSE', 'LICENSE.md', 'LICENSE.txt']) {
            try { license = await readFile(resolve(directory, file)); break; }
            catch (error) { if (error.code !== 'ENOENT') throw error; }
        }
        if (!license?.length) throw new Error('Editor package license text missing.');
        const file = `licenses/${pkg.name.replaceAll('/', '__').replaceAll('@', '')}.txt`;
        await writeFile(resolve(output, file), license);
        licenses.push({ name: pkg.name, version: pkg.version, license: pkg.license, file, sha256: sha256(license) });
        if (pkg.name === '@tiptap/pm') {
            const thirdParty = await readFile(resolve(directory, 'THIRD_PARTY_LICENSES.md'));
            await writeFile(resolve(output, 'licenses/tiptap__pm-third-party.txt'), thirdParty);
        }
    }
    await copyFile(resolve(root, 'frontend/tiptap/editor.css'), resolve(output, 'editor.css'));
    const files = {};
    for (const path of [...Object.keys(result.metafile.outputs), relative(root, resolve(output, 'editor.css'))].sort()) {
        const bytes = await readFile(resolve(root, path));
        files[relative(output, resolve(root, path)).replaceAll('\\', '/')] = { bytes: bytes.length, sha256: sha256(bytes) };
    }
    await writeFile(resolve(output, 'build.json'), JSON.stringify({
        tiptap: manifest.devDependencies['@tiptap/core'], bundler: manifest.devDependencies.esbuild,
        files, packages: licenses, inputs: Object.keys(result.metafile.inputs).sort(),
    }, null, 2) + '\n');
}
