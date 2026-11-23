import { readFileSync } from "node:fs";
import { win32 as path } from "node:path";
import vm from "node:vm";
import test from "node:test";
import assert from "node:assert/strict";

const template = readFileSync(new URL("./Bootstrap.cjs", import.meta.url), "utf8");
const config = {
    patcher: "C:\\fixture\\adapter\\patcher.js",
    dataDirectory: "C:\\fixture\\adapter-data",
    runtimeFiles: ["patcher.js", "preload.js", "renderer.js", "renderer.css"].map(name => "C:\\fixture\\adapter\\" + name)
};
const script = new vm.Script(template.replace("__WR_CONFIG__", JSON.stringify(config)));
const original = "C:\\fixture\\Discord\\app-1.0.1\\resources\\_app.asar";
function run({ missing, nonFile, patcherError, originalError, packageMain = "bundle.js" } = {}) {
    const calls = [];
    const environment = { existing: "preserved" };
    const main = { filename: "C:\\fixture\\Discord\\app-1.0.1\\resources\\app.asar\\index.js", path: "old" };
    const requireStub = name => {
        if (name === "fs") return { statSync(file) {
            calls.push(["stat", file]);
            if (file === missing) throw new Error("missing fixture file");
            return { isFile: () => file !== nonFile };
        } };
        if (name === "path") return path;
        if (name === "electron") return { app: { setAppPath(value) { calls.push(["appPath", value]); } } };
        if (name === config.patcher) {
            calls.push(["patcher", name]);
            if (patcherError) throw patcherError;
            return;
        }
        if (name === path.join(original, "package.json")) return { name: "discord", main: packageMain };
        if (name === path.join(original, "bundle.js")) {
            calls.push(["original", name]);
            if (originalError) throw originalError;
            return;
        }
        throw new Error("Unexpected require: " + name);
    };
    requireStub.main = main;
    let error;
    try { script.runInNewContext({ require: requireStub, process: { env: environment } }); }
    catch (caught) { error = caught; }
    return { calls, environment, main, error };
}
test("complete runtime loads the staged patcher exactly once with isolated settings", () => {
    const result = run();
    assert.equal(result.error, undefined);
    assert.equal(result.calls.filter(([kind]) => kind === "patcher").length, 1);
    assert.equal(result.calls.filter(([kind]) => kind === "original" || kind === "appPath").length, 0);
    assert.equal(result.environment.VENCORD_USER_DATA_DIR, config.dataDirectory);
    assert.equal(result.environment.DATA_DIR, config.dataDirectory);
});
for (const missing of config.runtimeFiles) {
    test("missing " + path.basename(missing) + " loads only original Discord and fixes app/main paths", () => {
        const result = run({ missing });
        assert.equal(result.error, undefined);
        assert.deepEqual(result.environment, { existing: "preserved" });
        assert.equal(result.calls.filter(([kind]) => kind === "patcher").length, 0);
        assert.deepEqual(result.calls.filter(([kind]) => kind === "appPath" || kind === "original"), [
            ["appPath", original], ["original", path.join(original, "bundle.js")]
        ]);
        assert.equal(result.main.filename, path.join(original, "bundle.js"));
        assert.equal(result.main.path, original);
    });
}
test("a runtime path that became a directory cannot be loaded", () => {
    const result = run({ nonFile: config.runtimeFiles[0] });
    assert.equal(result.error, undefined);
    assert.equal(result.calls.filter(([kind]) => kind === "patcher").length, 0);
    assert.equal(result.calls.filter(([kind]) => kind === "original").length, 1);
});
test("patcher failure is propagated without double-starting Discord", () => {
    const error = new Error("partial Vencord initialization");
    const result = run({ patcherError: error });
    assert.equal(result.error, error);
    assert.equal(result.calls.filter(([kind]) => kind === "patcher").length, 1);
    assert.equal(result.calls.filter(([kind]) => kind === "original" || kind === "appPath").length, 0);
});
test("original-client failure is propagated without retry", () => {
    const error = new Error("original client initialization failed");
    const result = run({ missing: config.runtimeFiles[0], originalError: error });
    assert.equal(result.error, error);
    assert.equal(result.calls.filter(([kind]) => kind === "original").length, 1);
});
test("an invalid original package entrypoint cannot escape the saved archive", () => {
    for (const packageMain of ["..\\other.js", "C:\\other.js", ""]) {
        const result = run({ missing: config.runtimeFiles[0], packageMain });
        assert.ok(result.error);
        assert.equal(result.calls.filter(([kind]) => kind === "original" || kind === "patcher").length, 0);
    }
});
