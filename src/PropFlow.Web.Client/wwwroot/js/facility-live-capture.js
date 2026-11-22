// Runs off the UI thread. Sends small PCM frames; never stores or uploads recordings.
class FacilityCapture extends AudioWorkletProcessor {
  constructor() { super(); this.samples = new Float32Array(1024); this.offset = 0; }
  process(inputs) {
    const input = inputs[0]?.[0];
    if (input) for (const sample of input) {
      this.samples[this.offset++] = sample;
      if (this.offset === this.samples.length) {
        this.port.postMessage(this.samples); this.offset = 0;
      }
    }
    return true;
  }
}
registerProcessor('propflow-facility-capture', FacilityCapture);
