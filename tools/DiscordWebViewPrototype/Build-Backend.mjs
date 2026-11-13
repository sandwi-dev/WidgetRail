import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';
import { minify } from 'terser';

// Build-time syntax minification only. No property mangling, compression,
// external fetches or dynamic module loader enter the sealed backend.
export async function minifySource(source) {
    const result = await minify(source, {
        compress: false,
        mangle: { toplevel: false },
        keep_fnames: true,
        format: { comments: /@license|@preserve|SPDX|Copyright/i }
    });
    if (!result.code) throw new Error('Backend minification produced no code.');
    return result.code;
}

async function build(destination) {
    const repo = fileURLToPath(new URL('../../', import.meta.url));
    const read = relative => readFile(path.join(repo, relative), 'utf8');
    const contract = JSON.parse(await read('samples/DiscordWebViewWidget/web/provider/adapter-contract.json'));
    if (!Number.isSafeInteger(contract.build) || contract.build < 1) throw new Error('Invalid adapter build.');
    const adapter = await read('samples/DiscordWebViewWidget/web/provider/adapter.js');
    const source = [await read('tools/DiscordWebViewPrototype/webpack.js'),
        await read('samples/DiscordWebViewWidget/web/provider/activity-provider.js'), adapter,
        `initializeWidgetRailDiscordAdapter(${contract.build},host);`,
        await read('tools/DiscordWebViewPrototype/discord-commands.js'),
        await read('samples/DiscordWebViewWidget/web/provider/observations.js'),
        await read('samples/DiscordWebViewWidget/web/session-backend.js')].join('\n');
    const output = await minifySource(source);
    const bytes = Buffer.byteLength(output, 'utf8');
    if (bytes > 65536) throw new Error(`Sealed backend exceeds 65536 bytes after minification (${bytes}).`);
    await writeFile(destination, output);
    console.log(`Sealed backend: ${Buffer.byteLength(source, 'utf8')} source bytes → ${bytes} bytes; adapter ${contract.build}.`);
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
    if (process.argv.length !== 3) throw new Error('Usage: node Build-Backend.mjs <output-file>');
    await build(path.resolve(process.argv[2]));
}
