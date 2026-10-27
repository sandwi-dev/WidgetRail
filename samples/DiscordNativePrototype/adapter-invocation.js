async function invokeWidgetRailDiscordAdapter(expectedBuild, request, install) {
    const complete = result => ({ needsInstall: false, result });
    const current = globalThis.WidgetRailDiscordPrototype;
    if (current?.build === expectedBuild) return complete(await current.invoke(request));
    if (current != null && (!Number.isSafeInteger(current.build) || current.build < 1))
        return complete({ ok: false, code: 'adapter_incompatible' });
    if (current?.build > expectedBuild) return complete({ ok: false, code: 'adapter_newer' });
    // This branch has not invoked any provider action. Only it can request installation.
    if (!install) return { needsInstall: true };
    install();
    const installed = globalThis.WidgetRailDiscordPrototype;
    if (installed?.build !== expectedBuild || typeof installed.invoke !== 'function')
        return complete({ ok: false, code: 'adapter_incompatible' });
    return complete(await installed.invoke(request));
}
