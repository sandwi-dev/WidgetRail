// Read only the diagnostic fields needed for encoder triage. Never serialize
// clientInfo (which contains command-line/profile paths), logs or display data.
(() => {
    if (location.protocol !== 'edge:' && location.protocol !== 'chrome:') return { ready: false };
    if (location.hostname !== 'gpu') return { ready: false };
    const view = [...document.querySelectorAll('*')].find(element => element.browserBridge?.gpuInfo);
    const info = view?.browserBridge?.gpuInfo;
    if (!info?.featureStatus || !Array.isArray(info.videoAcceleratorsInfo)) return { ready: false };
    const safe = value => String(value ?? '').replace(/[\u0000-\u001f\u007f]/g, ' ')
        .replace(/(?:https?|file):\S+/gi, '[url omitted]')
        .replace(/(?:[a-z]:\\|\\\\)\S+/gi, '[path omitted]').slice(0, 1200);
    const rows = value => Array.isArray(value) ? value.slice(0, 256) : [];
    const status = value => {
        if (!value) return null;
        const features = value.featureStatus ?? {};
        return {
            videoEncode: safe(features.video_encode),
            videoDecode: safe(features.video_decode),
            gpuCompositing: safe(features.gpu_compositing),
            problems: rows(value.problems).map(problem => ({
                description: safe(problem.description),
                affectedSettings: rows(problem.affectedGpuSettings).map(safe),
                tag: safe(problem.tag)
            })),
            workarounds: rows(value.workarounds).map(safe)
        };
    };
    const adapters = value => rows(value).filter(row =>
        /^(GPU\d+|GL_RENDERER|Driver vendor|Driver version|Driver date)$/i.test(row.description ?? ''))
        .map(row => ({ name: safe(row.description), value: safe(row.value) }));
    const encodeProfiles = rows(info.videoAcceleratorsInfo).filter(row => /^encode\b/i.test(row.description ?? ''))
        .map(row => ({ name: safe(row.description), value: safe(row.value) }));
    return {
        ready: true,
        gpuIdentityAvailable: rows(info.basicInfo).some(row => /^GPU\d+$/i.test(row.description ?? '') && /VENDOR=\s*0x(?!0000)[0-9a-f]{4}/i.test(String(row.value))),
        adapters: adapters(info.basicInfo), hardwareAdapters: adapters(info.basicInfoForHardwareGpu),
        status: status(info.featureStatus), hardwareStatus: status(info.featureStatusForHardwareGpu), encodeProfiles
    };
})();
