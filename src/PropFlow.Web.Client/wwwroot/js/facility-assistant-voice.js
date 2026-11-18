import { create as createRecognition, isSupported } from './resident-request-voice.js?v=20261112-1';

const recordings = Object.freeze({ WELCOME: 'welcome-vi.wav', NEW_NEED: 'new-need-vi.wav' });
const recordingCache = new Map();
function loadRecording(kind) {
  if (!Object.hasOwn(recordings, kind)) return Promise.reject(Error('Unknown greeting'));
  const url = new URL('audio/facility/' + recordings[kind] + '?v=20261117-2', document.baseURI).href;
  if (!recordingCache.has(url)) {
    const request = (async () => {
      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), 8000);
      try {
        const response = await fetch(url, { cache: 'force-cache', credentials: 'same-origin', signal: controller.signal });
        if (!response.ok) throw Error('Greeting unavailable');
        const bytes = new Uint8Array(await response.arrayBuffer());
        if (bytes.length < 44 || bytes.length > 2_000_000 || String.fromCharCode(...bytes.slice(0, 4)) !== 'RIFF' || String.fromCharCode(...bytes.slice(8, 12)) !== 'WAVE') throw Error('Invalid greeting');
        return new Blob([bytes], { type: 'audio/wav' });
      } finally { clearTimeout(timeout); }
    })();
    recordingCache.set(url, request);
    request.catch(() => { if (recordingCache.get(url) === request) recordingCache.delete(url); });
  }
  return recordingCache.get(url);
}
// Warm only public, fixed recordings. Does not open a microphone or play sound on the dashboard.
export function preloadGreetings() { void loadRecording('WELCOME').catch(() => {}); }

export function capabilities() {
  return { recognition: isSupported(), speech: typeof globalThis.Audio === 'function' };
}
export function showResults() {
  document.getElementById('facility-ai-results')?.scrollIntoView({ block: 'start', behavior: globalThis.matchMedia?.('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth' });
}
// Fixed greetings are bundled Vietnamese recordings; personalized replies use server-generated audio.
export function create(dotnet) {
  const microphone = createRecognition(dotnet);
  let disposed = false, audio = null, audioUrl = null, generation = 0, currentEpoch = 0;
  const notify = (epoch, state) => { if (!disposed) dotnet.invokeMethodAsync('OnAssistantSpeech', epoch, state).catch(() => {}); };
  function release() {
    if (audio) {
      audio.onplay = audio.onended = audio.onerror = null;
      audio.pause(); audio.removeAttribute('src'); audio.load(); audio = null;
    }
    if (audioUrl) { URL.revokeObjectURL(audioUrl); audioUrl = null; }
  }
  function stopSpeaking() { generation++; release(); }
  function cancel() { microphone.cancel(); stopSpeaking(); }
  function pause() { cancel(); notify(currentEpoch, 'paused'); }
  function hide() { if (document.visibilityState === 'hidden') pause(); }
  async function playBlob(blob, epoch, turn) {
    audioUrl = URL.createObjectURL(blob);
    const current = audio = new Audio(audioUrl);
    current.onplay = () => { if (!disposed && generation === turn) notify(epoch, 'speaking'); };
    current.onended = () => { if (!disposed && generation === turn) { release(); notify(epoch, 'idle'); } };
    current.onerror = () => { if (!disposed && generation === turn) { release(); notify(epoch, 'unavailable'); } };
    await current.play();
    return !disposed && generation === turn;
  }
  function playbackFailed(error, epoch, turn) {
    if (!disposed && generation === turn) { release(); notify(epoch, error?.name === 'NotAllowedError' ? 'playback-blocked' : 'unavailable'); }
    return false;
  }
  globalThis.addEventListener('pagehide', pause);
  document.addEventListener('visibilitychange', hide);
  return {
    start(epoch) { if (disposed) return false; currentEpoch = epoch; stopSpeaking(); return microphone.start(epoch); },
    stop() { microphone.stop(); },
    async playGreeting(kind, epoch) {
      if (disposed || document.visibilityState === 'hidden' || !capabilities().speech) return false;
      stopSpeaking(); microphone.cancel(); currentEpoch = epoch;
      const turn = generation;
      try {
        const blob = await loadRecording(kind);
        if (disposed || generation !== turn || document.visibilityState === 'hidden') return false;
        return await playBlob(blob, epoch, turn);
      } catch (error) { return playbackFailed(error, epoch, turn); }
    },
    async play(encoded, contentType, epoch) {
      if (disposed || document.visibilityState === 'hidden' || !capabilities().speech) return false;
      stopSpeaking(); microphone.cancel(); currentEpoch = epoch;
      const turn = generation;
      try {
        if (contentType !== 'audio/wav' || typeof encoded !== 'string' || encoded.length > 4_100_000) throw Error('Invalid audio');
        const bytes = Uint8Array.from(atob(encoded), character => character.charCodeAt(0));
        return await playBlob(new Blob([bytes], { type: contentType }), epoch, turn);
      } catch (error) {
        return playbackFailed(error, epoch, turn);
      }
    },
    stopSpeaking,
    cancel,
    dispose() {
      disposed = true; cancel(); microphone.dispose();
      globalThis.removeEventListener('pagehide', pause);
      document.removeEventListener('visibilitychange', hide);
    }
  };
}
