export function capabilities() {
  return globalThis.isSecureContext === true && !!navigator.mediaDevices?.getUserMedia &&
    typeof AudioContext === 'function' && typeof AudioWorkletNode === 'function' && typeof WebSocket === 'function';
}
export function pcm16(samples, inputRate) {
  const step = inputRate / 16000, output = new Uint8Array(Math.floor(samples.length / step) * 2), view = new DataView(output.buffer);
  for (let i = 0; i < output.length / 2; i++) {
    const from = Math.floor(i * step), to = Math.max(from + 1, Math.floor((i + 1) * step));
    let sum = 0; for (let j = from; j < to && j < samples.length; j++) sum += samples[j];
    const sample = Math.max(-1, Math.min(1, sum / (to - from)));
    view.setInt16(i * 2, sample < 0 ? sample * 32768 : sample * 32767, true);
  }
  return output;
}
const encode = bytes => btoa(String.fromCharCode(...bytes));
export function create(dotnet) {
  let disposed = false, epoch = 0, socket, stream, context, capture, source, gain, deadline, timeout, finishStart;
  let ready = false, muted = false, nextAudio = 0, generation = 0, turnDone = false, input = '', output = '', newInput = true, newOutput = true, lastState;
  let ending = false, farewellReceived = false;
  const playing = new Set(), tools = new Map(), seenTools = new Set();
  const notify = (name, ...args) => { if (!disposed) void dotnet.invokeMethodAsync(name, epoch, ...args).catch(() => {}); };
  const state = value => { if (lastState !== value) { lastState = value; notify('OnLiveState', value); } };
  const send = value => {
    if (!socket || socket.readyState !== 1) return false;
    if (socket.bufferedAmount > 262144) { fail('network'); return false; }
    socket.send(JSON.stringify(value)); return true;
  };
  function silence() {
    for (const node of playing) { node.onended = null; try { node.stop(); } catch {} }
    playing.clear(); nextAudio = 0;
  }
  function close() {
    generation++; ready = false; clearTimeout(timeout); clearTimeout(deadline); finishStart?.(false); finishStart = null;
    ending = farewellReceived = false;
    silence();
    for (const id of tools.keys()) notify('OnLiveToolCancelled', id);
    tools.clear(); seenTools.clear();
    if (capture) { capture.port.onmessage = null; capture.disconnect(); capture = null; }
    source?.disconnect(); source = null; gain?.disconnect(); gain = null;
    stream?.getTracks().forEach(track => track.stop()); stream = null;
    if (context) { void context.close().catch(() => {}); context = null; }
    if (socket) { socket.onopen = socket.onmessage = socket.onerror = socket.onclose = null; socket.close(); socket = null; }
  }
  function fail(reason) { const wasEnding = ending; close(); if (wasEnding) notify('OnLiveEnded'); else state(reason); }
  function visibilityChanged() {
    // A hidden tab is still the user's active voice session. Only navigation, stop or disposal ends it.
    if (document.visibilityState === 'visible' && ready && context?.state === 'suspended')
      void context.resume().catch(() => state('playback-blocked'));
  }
  function pagehide() { close(); state('paused'); }
  function settleTurn() {
    if (!turnDone || playing.size || !ready) return;
    if (ending) {
      if (!farewellReceived) return;
      close(); notify('OnLiveEnded');
    } else state('listening');
  }
  function play(encoded, mime) {
    if (muted || !context || !mime?.startsWith('audio/pcm') || typeof encoded !== 'string' || encoded.length > 4_100_000) return;
    const rate = Number(/rate=(\d+)/.exec(mime)?.[1] || 24000);
    if (rate < 8000 || rate > 48000) throw Error('Invalid rate');
    const bytes = Uint8Array.from(atob(encoded), c => c.charCodeAt(0));
    if (!bytes.length || bytes.length % 2) throw Error('Invalid PCM');
    const view = new DataView(bytes.buffer), buffer = context.createBuffer(1, bytes.length / 2, rate), samples = buffer.getChannelData(0);
    for (let i = 0; i < samples.length; i++) samples[i] = view.getInt16(i * 2, true) / 32768;
    const node = context.createBufferSource(); node.buffer = buffer; node.connect(context.destination);
    const startsAt = Math.max(context.currentTime + .025, nextAudio);
    if (startsAt - context.currentTime > 45) throw Error('Audio backlog');
    nextAudio = startsAt + buffer.duration; playing.add(node); state('speaking');
    node.onended = () => { playing.delete(node); settleTurn(); };
    node.start(startsAt);
  }
  async function tool(call, turn) {
    if (!call?.id || seenTools.has(call.id)) return;
    if (seenTools.size >= 30 || tools.size >= 2) { fail('tool-limit'); return; }
    seenTools.add(call.id);
    if (ending) { send({ toolResponse: { functionResponses: [{ id: call.id, name: call.name, response: { error: 'Phiên đang kết thúc, không thực hiện tra cứu mới.' } }] } }); return; }
    tools.set(call.id, true);
    state(call.name === 'end_conversation' ? 'ending' : 'searching');
    try {
      let response = { error: 'Công cụ hoặc nhu cầu không hợp lệ.' };
      if (call.name === 'end_conversation') {
        ending = true; farewellReceived = false; turnDone = false;
        for (const id of [...tools.keys()]) if (id !== call.id) {
          tools.delete(id); notify('OnLiveToolCancelled', id);
          send({ toolResponse: { functionResponses: [{ id, name: 'find_facilities', response: { error: 'Cư dân kết thúc phiên, tra cứu đã hủy.' } }] } });
        }
        response = { ending: true, instruction: 'Chào tạm biệt bằng một câu tiếng Việt ngắn. Ứng dụng sẽ tắt micro và về trang chủ sau khi phát xong.' };
      }
      else if (call.name === 'find_facilities' && typeof call.args?.need === 'string' && call.args.need.trim().length >= 2 && call.args.need.length <= 1200)
        response = await dotnet.invokeMethodAsync('OnLiveTool', epoch, call.id, call.args.need);
      if (!disposed && turn === generation && tools.has(call.id)) send({ toolResponse: { functionResponses: [{ id: call.id, name: call.name, response: response || { error: 'Yêu cầu đã hủy.' } }] } });
    } catch { if (turn === generation && tools.has(call.id)) send({ toolResponse: { functionResponses: [{ id: call.id, name: call.name, response: { error: 'Chưa tra cứu được dữ liệu. Hãy thông báo lỗi, không suy đoán.' } }] } }); }
    finally { tools.delete(call.id); }
  }
  function message(data, turn) {
    if (data.error) { fail('provider-error'); return; }
    if (data.setupComplete) {
      clearTimeout(timeout); ready = true; finishStart?.(true); finishStart = null; state('listening');
      send({ clientContent: { turns: [{ role: 'user', parts: [{ text: 'Bắt đầu cuộc trò chuyện: chào ngắn gọn bằng tiếng Việt và hỏi hôm nay tôi muốn làm gì, rồi lắng nghe tôi.' }] }], turnComplete: true } });
    }
    if (data.goAway) { fail('expired'); return; }
    for (const id of data.toolCallCancellation?.ids || []) { tools.delete(id); notify('OnLiveToolCancelled', id); }
    for (const call of data.toolCall?.functionCalls || []) void tool(call, turn);
    const content = data.serverContent;
    if (!content) return;
    if (content.interrupted) { silence(); ending = farewellReceived = false; newOutput = true; turnDone = false; state('listening'); }
    if (content.inputTranscription?.text) {
      if (newInput) { input = ''; newInput = false; }
      input = (input + content.inputTranscription.text).slice(-1200); newOutput = true;
      notify('OnLiveTranscript', 'user', input);
    }
    if (content.outputTranscription?.text) {
      if (ending) farewellReceived = true;
      if (newOutput) { output = ''; newOutput = false; }
      output = (output + content.outputTranscription.text).slice(-6000);
      notify('OnLiveTranscript', 'assistant', output);
    }
    for (const part of content.modelTurn?.parts || []) {
      if (part.inlineData) { if (ending) farewellReceived = true; turnDone = false; play(part.inlineData.data, part.inlineData.mimeType); }
    }
    if (content.turnComplete) { newInput = true; newOutput = true; turnDone = true; settleTurn(); }
  }
  globalThis.addEventListener('pagehide', pagehide); document.addEventListener('visibilitychange', visibilityChanged);
  return {
    async start(session, newEpoch, audioEnabled = true) {
      if (disposed || !capabilities()) return false;
      close(); epoch = newEpoch; lastState = undefined; muted = !audioEnabled; input = output = ''; newInput = newOutput = true;
      const turn = generation;
      try {
        const url = new URL(session.webSocketUrl);
        if (url.protocol !== 'wss:' || url.hostname !== 'generativelanguage.googleapis.com' || url.search || !session.token?.startsWith('auth_tokens/')) throw Error('Invalid connection');
        const remaining = new Date(session.expiresAt).getTime() - Date.now();
        if (remaining < 1000 || remaining > 15 * 60 * 1000 + 5000) throw Error('Expired token');
        state('connecting');
        const obtained = await navigator.mediaDevices.getUserMedia({ audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true }, video: false });
        if (disposed || turn !== generation) { obtained.getTracks().forEach(track => track.stop()); return false; }
        stream = obtained;
        context = new AudioContext({ sampleRate: 16000 });
        let audioTimeout;
        try { await Promise.race([context.resume(), new Promise((_, reject) => { audioTimeout = setTimeout(() => { const error = Error('Audio activation required'); error.name = 'NotAllowedError'; reject(error); }, 3000); })]); }
        finally { clearTimeout(audioTimeout); }
        if (disposed || turn !== generation || !context) return false;
        if (context.state !== 'running') { fail('playback-blocked'); return false; }
        await context.audioWorklet.addModule(new URL('./facility-live-capture.js?v=20261122-2', import.meta.url));
        if (disposed || turn !== generation || !context) return false;
        source = context.createMediaStreamSource(stream); capture = new AudioWorkletNode(context, 'propflow-facility-capture');
        gain = context.createGain(); gain.gain.value = 0;
        source.connect(capture); capture.connect(gain); gain.connect(context.destination);
        capture.port.onmessage = event => { if (ready && context && turn === generation) send({ realtimeInput: { audio: { data: encode(pcm16(event.data, context.sampleRate)), mimeType: 'audio/pcm;rate=16000' } } }); };
        url.searchParams.set('access_token', session.token); socket = new WebSocket(url.href);
        const started = new Promise(resolve => { finishStart = resolve; });
        timeout = setTimeout(() => fail('network'), 15000); deadline = setTimeout(() => fail('expired'), remaining);
        socket.onopen = () => { if (turn === generation) send({ setup: session.setup }); };
        let queue = Promise.resolve();
        socket.onmessage = event => {
          queue = queue.then(async () => {
            const text = typeof event.data === 'string' ? event.data : await event.data.text();
            if (turn !== generation || disposed) return;
            if (text.length > 8_000_000) throw Error('Oversized message');
            message(JSON.parse(text), turn);
          }).catch(() => { if (turn === generation) fail('provider-error'); });
        };
        socket.onerror = () => { if (turn === generation) fail('network'); };
        socket.onclose = () => { if (turn === generation) fail('disconnected'); };
        return await started;
      } catch (error) { if (turn === generation && !disposed) fail(error?.name === 'NotAllowedError' ? 'not-allowed' : 'network'); return false; }
    },
    sendText(text) {
      if (!ready || typeof text !== 'string' || text.trim().length < 2 || text.length > 1200) return false;
      silence(); ending = farewellReceived = false; newInput = newOutput = true;
      return send({ clientContent: { turns: [{ role: 'user', parts: [{ text }] }], turnComplete: true } });
    },
    interrupt() {
      silence(); ending = farewellReceived = false; newOutput = true;
      if (ready) send({ clientContent: { turns: [{ role: 'user', parts: [{ text: 'Hãy dừng trả lời để nghe mình nói tiếp.' }] }], turnComplete: false } });
      state('listening');
    },
    setMuted(value) { muted = !!value; if (muted) { silence(); settleTurn(); if (ready && !ending) state('listening'); } },
    stop: close,
    dispose() { close(); disposed = true; globalThis.removeEventListener('pagehide', pagehide); document.removeEventListener('visibilitychange', visibilityChanged); }
  };
}
