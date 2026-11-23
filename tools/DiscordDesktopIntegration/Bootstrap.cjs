// Generated into the reviewed app.asar; configuration is replaced at packaging.
// Original-entrypoint loading follows the pinned Vencord patcher's app-path model.
"use strict";
const config = __WR_CONFIG__;
const fs = require("fs");
const path = require("path");

let runtimeAvailable = false;
try {
    runtimeAvailable = config.runtimeFiles.every(file => fs.statSync(file).isFile());
} catch {
    // Only preflight failures may fall back. No Vencord module has loaded yet.
}

if (runtimeAvailable) {
    process.env.VENCORD_USER_DATA_DIR = config.dataDirectory;
    process.env.DATA_DIR = config.dataDirectory;
    // Intentionally no catch here: a failure after patcher initialization may
    // leave hooks installed. Running Discord again could double-start it.
    require(config.patcher);
} else {
    if (!require.main || typeof require.main.filename !== "string") {
        throw new Error("Discord bootstrap has no main module; use the reviewed restore operation.");
    }
    const bootstrapDirectory = path.dirname(require.main.filename);
    if (path.basename(bootstrapDirectory) !== "app.asar") {
        throw new Error("Unexpected Discord bootstrap location; use the reviewed restore operation.");
    }
    const original = path.resolve(bootstrapDirectory, "..", "_app.asar");
    const packageJson = require(path.join(original, "package.json"));
    if (packageJson.name !== "discord" || typeof packageJson.main !== "string" ||
        !packageJson.main || path.isAbsolute(packageJson.main)) {
        throw new Error("Original Discord entrypoint is unavailable; use the reviewed restore operation.");
    }
    const entrypoint = path.resolve(original, packageJson.main);
    const relative = path.relative(original, entrypoint);
    if (!relative || relative === ".." || relative.startsWith(".." + path.sep)) {
        throw new Error("Original Discord entrypoint escapes its archive; use the reviewed restore operation.");
    }
    require("electron").app.setAppPath(original);
    require.main.filename = entrypoint;
    require.main.path = original;
    require(entrypoint);
}
