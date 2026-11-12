export function create(dotnet, audio) {
  let stream = null, preparing = null, peer = null, key = null, disposed = false, generation = 0, muted = false;
  let pendingIce = [], sendQueue = Promise.resolve();
  const notify = (method, ...args) => { if (!disposed) return dotnet.invokeMethodAsync(method, ...args).catch(() => {}); };
  const signal = (id, revision, kind, value) => {
    sendQueue = sendQueue.then(() => disposed || key !== `${id}:${revision}` ? undefined :
      dotnet.invokeMethodAsync('OnRtcSignal', id, revision, kind, JSON.stringify(value))).catch(() => {});
    return sendQueue;
  };
  const stopPeer = () => {
    const old = peer; peer = null; key = null; pendingIce = [];
    if (old) { old.onicecandidate = old.ontrack = old.onconnectionstatechange = null; old.close(); }
    stream?.getTracks().forEach(track => { track.enabled = false; });
    audio.pause?.(); audio.srcObject = null;
  };
  const prepare = async () => {
    if (disposed || !globalThis.isSecureContext || !navigator.mediaDevices?.getUserMedia || !globalThis.RTCPeerConnection) {
      await notify('OnMediaError', 'not-supported'); return false;
    }
    if (stream?.getAudioTracks().some(track => track.readyState === 'live')) return true;
    if (preparing) return preparing;
    const version = generation;
    const task = (async () => {
      try {
        const acquired = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true }, video: false });
        if (disposed || version !== generation) { acquired.getTracks().forEach(track => track.stop()); return false; }
        if (!acquired.getAudioTracks().some(track => track.readyState === 'live')) {
          acquired.getTracks().forEach(track => track.stop()); await notify('OnMediaError', 'NotFoundError'); return false;
        }
        stream = acquired;
        stream.getTracks().forEach(track => { track.enabled = false; track.onended = () => { if (!disposed) notify('OnMediaError', 'microphone-lost'); }; });
        return true;
      } catch (error) { if (!disposed && version === generation) await notify('OnMediaError', error?.name || 'capture-failed'); return false; }
      finally { if (preparing === task) preparing = null; }
    })();
    preparing = task;
    return task;
  };
  const play = async (id, revision) => {
    try { await audio.play(); await notify('OnRtcState', id, revision, 'audio-playing'); return true; }
    catch { await notify('OnRtcState', id, revision, 'audio-blocked'); return false; }
  };
  const stop = () => { generation++; stopPeer(); muted = false; const old = stream; stream = null; old?.getTracks().forEach(track => { track.onended = null; track.stop(); }); };
  return {
    prepare,
    async connect(id, revision, resident, iceServers) {
      if (disposed || key === `${id}:${revision}`) return !!peer;
      if (!await prepare()) return false;
      stopPeer(); key = `${id}:${revision}`;
      const current = peer = new RTCPeerConnection({ iceServers });
      stream.getAudioTracks().forEach(track => { track.enabled = !muted; current.addTrack(track, stream); });
      current.onicecandidate = event => { if (peer === current && event.candidate) signal(id, revision, 'ice', event.candidate.toJSON()); };
      current.ontrack = event => {
        if (peer !== current) return;
        audio.srcObject = event.streams[0] || new MediaStream([event.track]);
        play(id, revision);
      };
      current.onconnectionstatechange = () => { if (peer === current) notify('OnRtcState', id, revision, current.connectionState); };
      if (resident) {
        await current.setLocalDescription(await current.createOffer());
        if (peer === current) await signal(id, revision, 'offer', current.localDescription);
      }
      return peer === current;
    },
    async receive(id, revision, kind, data) {
      const current = peer;
      if (disposed || !current || key !== `${id}:${revision}`) return false;
      const value = JSON.parse(data);
      if (kind === 'ice') {
        if (current.remoteDescription) await current.addIceCandidate(value);
        else if (pendingIce.length < 128) pendingIce.push(value);
      } else {
        await current.setRemoteDescription(value);
        if (peer !== current) return false;
        for (const candidate of pendingIce) await current.addIceCandidate(candidate);
        pendingIce = [];
        if (kind === 'offer') {
          await current.setLocalDescription(await current.createAnswer());
          if (peer === current) await signal(id, revision, 'answer', current.localDescription);
        }
      }
      return true;
    },
    mute(value) { muted = value; if (peer) stream?.getAudioTracks().forEach(track => { track.enabled = !muted; }); },
    play(id, revision) { return play(id, revision); },
    stop,
    dispose() { disposed = true; stop(); }
  };
}
