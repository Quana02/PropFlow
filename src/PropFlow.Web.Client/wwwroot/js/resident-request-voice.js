export function isSupported() {
  return globalThis.isSecureContext === true && !!(globalThis.SpeechRecognition || globalThis.webkitSpeechRecognition);
}

export function create(dotnet) {
  const Recognition = globalThis.SpeechRecognition || globalThis.webkitSpeechRecognition;
  let recognition = null;
  let currentSession;
  let disposed = false;
  let silenceTimer;
  let queue = Promise.resolve();
  const notify = (name, ...args) => {
    queue = queue.then(() => disposed ? undefined : dotnet.invokeMethodAsync(name, ...args)).catch(() => {});
  };
  const clearSilence = () => { clearTimeout(silenceTimer); silenceTimer = undefined; };
  const cancel = () => {
    clearSilence();
    if (!recognition) return;
    const previous = recognition;
    recognition = null;
    previous.onresult = previous.onend = previous.onerror = previous.onspeechend = previous.onspeechstart = null;
    try { previous.abort(); } catch { /* The browser may already have ended the session. */ }
  };
  return {
    start(session) {
      if (disposed || !isSupported() || !Recognition) return false;
      cancel();
      currentSession = session;
      const current = recognition = new Recognition();
      current.lang = "vi-VN";
      current.continuous = true;
      current.interimResults = true;
      current.maxAlternatives = 1;
      let error = "";
      current.onresult = event => {
        if (recognition !== current || disposed) return;
        let final = "", interim = "";
        for (let index = 0; index < event.results.length; index++) {
          const result = event.results[index];
          if (result.isFinal) final += result[0].transcript + " ";
          else interim += result[0].transcript + " ";
        }
        notify("OnVoiceResult", session, final.trim(), interim.trim());
      };
      current.onerror = event => { if (recognition === current) error = event.error || "unknown"; };
      current.onspeechstart = clearSilence;
      current.onspeechend = () => {
        clearSilence();
        silenceTimer = setTimeout(() => {
          if (recognition === current) { try { current.stop(); } catch { cancel(); notify("OnVoiceEnded", session, "unknown"); } }
        }, 1500);
      };
      current.onend = () => {
        if (recognition !== current || disposed) return;
        clearSilence();
        recognition = null;
        notify("OnVoiceEnded", session, error);
      };
      try { current.start(); return true; }
      catch { cancel(); notify("OnVoiceEnded", session, "not-allowed"); return false; }
    },
    stop() {
      clearSilence();
      try { recognition?.stop(); }
      catch { const session = currentSession; cancel(); notify("OnVoiceEnded", session, "unknown"); }
    },
    cancel,
    dispose() { disposed = true; cancel(); }
  };
}
