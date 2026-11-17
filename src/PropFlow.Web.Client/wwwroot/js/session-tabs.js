const logoutKey = "propflow:session-ended";

export function observeLogout(target) {
    const notify = id => target.invokeMethodAsync("SessionEndedInAnotherTab", id).catch(() => {});
    const channel = typeof BroadcastChannel === "function" ? new BroadcastChannel(logoutKey) : null;
    const onStorage = event => {
        if (event.key === logoutKey && event.newValue) notify(event.newValue);
    };

    if (channel) channel.onmessage = event => notify(event.data);
    else window.addEventListener("storage", onStorage);

    return DotNet.createJSObjectReference({
        publish(id) {
            if (channel) channel.postMessage(id);
            else {
                localStorage.setItem(logoutKey, id);
                localStorage.removeItem(logoutKey);
            }
        },
        close() {
            channel?.close();
            window.removeEventListener("storage", onStorage);
        }
    });
}
