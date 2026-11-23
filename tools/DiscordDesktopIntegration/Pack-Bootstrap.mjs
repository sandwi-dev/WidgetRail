import { createHash } from "node:crypto";
import { createRequire } from "node:module";
import { readFile, writeFile, mkdir, lstat, realpath } from "node:fs/promises";
import { dirname, resolve, relative, isAbsolute, sep } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const repo = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const artifactRoot = resolve(repo, "artifacts/discord-desktop");
const output = resolve(process.argv[2] ?? "");
const relativeOutput = relative(artifactRoot, output);
if (!relativeOutput || isAbsolute(relativeOutput) || relativeOutput === ".." || relativeOutput.startsWith(`..${sep}`)) {
    throw new Error("Bootstrap packaging is restricted to this checkout's isolated artifact subtree.");
}
for (let current = output; current && current !== dirname(current); current = dirname(current)) {
    if ((await lstat(current)).isSymbolicLink()) throw new Error("Reparse/symlink build paths are unsupported.");
}
if ((await realpath(output)).toLowerCase() !== output.toLowerCase()) throw new Error("Build output identity changed.");
const require = createRequire(resolve(output, "build-tools/package.json"));
const asar = await import(pathToFileURL(require.resolve("@electron/asar")));
const index = await readFile(resolve(output, "bootstrap-index.js"));
if (!index.length || index.length > 64 * 1024) throw new Error("Bootstrap entrypoint exceeds its bounded size.");
const source = resolve(output, "bootstrap-source");
await mkdir(source); // Fresh only; never overwrite a previously reviewed payload.
const packageJson = Buffer.from('{"name":"discord","main":"index.js"}\n');
await writeFile(resolve(source, "index.js"), index, { flag: "wx" });
await writeFile(resolve(source, "package.json"), packageJson, { flag: "wx" });
const archive = resolve(output, "bootstrap.asar");
try { await lstat(archive); throw new Error("Bootstrap archive already exists."); }
catch (error) { if (error.code !== "ENOENT") throw error; }
await asar.createPackage(source, archive);
const entries = asar.listPackage(archive).map(value => value.replaceAll("\\", "/")).sort();
if (JSON.stringify(entries) !== JSON.stringify(["/index.js", "/package.json"])) throw new Error("Unexpected bootstrap archive entries.");
if (!asar.extractFile(archive, "index.js").equals(index) || !asar.extractFile(archive, "package.json").equals(packageJson)) {
    throw new Error("Bootstrap archive round trip changed its payload.");
}
const bytes = await readFile(archive);
const result = {
    schemaVersion: 1,
    library: "@electron/asar@4.0.1",
    archivePath: "bootstrap.asar",
    archiveSha256: createHash("sha256").update(bytes).digest("hex").toUpperCase(),
    bytes: bytes.length,
    entries,
    roundTripVerified: true,
    installed: false
};
await writeFile(resolve(output, "bootstrap-archive.json"), JSON.stringify(result, null, 2) + "\n", { flag: "wx" });
console.log(JSON.stringify(result));
