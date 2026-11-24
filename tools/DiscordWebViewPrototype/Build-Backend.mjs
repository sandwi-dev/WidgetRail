import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';
import { minify } from 'terser';

// WebSessionContract.MaximumSessionBackendScriptBytes; message and isolated
// page-action budgets remain smaller and are not changed by this build tool.
export const maximumBackendBytes = 128 * 1024;
export function validateBackendSource(source) {
    const bytes = Buffer.byteLength(source, 'utf8');
    if (bytes < 1 || bytes > maximumBackendBytes)
        throw new Error(`Sealed backend exceeds its 1..${maximumBackendBytes} byte bound after minification (${bytes}).`);
    return bytes;
}

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

async function build(destination, experimentalEncodedSource) {
    const repo = fileURLToPath(new URL('../../', import.meta.url));
    const read = relative => readFile(path.join(repo, relative), 'utf8');
    const contract = JSON.parse(await read('samples/DiscordWebViewWidget/web/provider/adapter-contract.json'));
    if (!Number.isSafeInteger(contract.build) || contract.build < 1) throw new Error('Invalid adapter build.');
    const adapter = await read('samples/DiscordWebViewWidget/web/provider/adapter.js');
    const source = [await read('tools/DiscordWebViewPrototype/webpack.js'),
        await read('samples/DiscordWebViewWidget/web/provider/activity-provider.js'),
        await read('samples/DiscordWebViewWidget/web/provider/messaging-provider.js'),
        await read('samples/DiscordWebViewWidget/web/provider/call-provider.js'), adapter,
        ...(experimentalEncodedSource ? [await read('tools/DiscordWebViewPrototype/encoded-source-worker.js'),
            await read('samples/DiscordWebViewWidget/web/provider/experimental-encoded-source.js')] : []),
        `initializeWidgetRailDiscordAdapter(${contract.build},host${experimentalEncodedSource ? ',createExperimentalCapture' : ''});`,
        await read('tools/DiscordWebViewPrototype/discord-commands.js'),
        await read('samples/DiscordWebViewWidget/web/provider/observations.js'),
        await read('samples/DiscordWebViewWidget/web/session-backend.js')].join('\n');
    const output = await minifySource(source);
    const bytes = validateBackendSource(output);
    await writeFile(destination, output);
    console.log(`Sealed backend: ${Buffer.byteLength(source, 'utf8')} source bytes → ${bytes} bytes; adapter ${contract.build}.`);
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
    const [, , destination, flag, ...extra] = process.argv;
    if (!destination || extra.length || (flag && flag !== '--experimental-encoded-source'))
        throw new Error('Usage: node Build-Backend.mjs <output-file> [--experimental-encoded-source]');
    await build(path.resolve(destination), flag === '--experimental-encoded-source');
}
