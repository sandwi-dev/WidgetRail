// Phase A permits two observations per desktop process, with no queue. A stalled
// filesystem cannot accumulate promises or repeated writes from renderer IPC.
export function createBoundedProbeWriter(write) {
    let attempts = 0;
    let busy = false;
    return async record => {
        if (busy || attempts >= 2) return { recorded: false };
        attempts++;
        busy = true;
        try {
            await write(record);
            return { recorded: true };
        } finally { busy = false; }
    };
}

// Identification is fail-closed: if multiple top-level app windows qualify, do
// not select the first or trust a renderer's claim about the main window.
export function isUniqueProbeSender(senderId, candidates) {
    return Number.isSafeInteger(senderId) && senderId > 0 &&
        candidates.length === 1 && candidates[0] === senderId;
}

// Vencord does not await Plugin.start(). Own/catch the asynchronous diagnostic
// work here, and invalidate delayed observations on stop or a replacement start.
export function createProbeLifecycle({ observe, publish, schedule, cancel, onError }) {
    let owner = null;
    let timer;
    const report = () => { try { onError(); } catch { } };
    async function run(captured) {
        if (owner !== captured) return;
        try {
            const summary = observe();
            if (owner === captured) await publish(summary);
        } catch { report(); }
    }
    function stop() {
        owner = null;
        if (timer !== undefined) cancel(timer);
        timer = undefined;
    }
    return {
        start() {
            stop();
            const captured = owner = Symbol('probe');
            void run(captured).then(() => {
                if (owner === captured) timer = schedule(() => { void run(captured); }, 3000);
            }).catch(report);
        },
        stop
    };
}
