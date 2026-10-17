// Grandmaster voice for the browser build: Piper (the same en_GB-alan voice as the desktop
// game) running in WebAssembly via vits-web. Runs in a worker so the game never stutters.
// The 63 MB voice model is downloaded once and kept in the browser's private storage (OPFS).
import * as tts from "https://cdn.jsdelivr.net/npm/@diffusionstudio/vits-web@1.0.3/+esm";

const VOICE = "en_GB-alan-medium";

const ready = (async () => {
  const stored = await tts.stored();
  if (!stored.includes(VOICE)) {
    await tts.download(VOICE, (p) => postMessage({ type: "progress", loaded: p.loaded, total: p.total }));
  }
  postMessage({ type: "ready" });
})().catch((e) => {
  postMessage({ type: "failed", error: String(e) });
  throw e;
});

// one sentence at a time, in the order asked
let chain = Promise.resolve();
onmessage = (e) => {
  const { id, text } = e.data;
  chain = chain.then(async () => {
    try {
      await ready;
      const wav = await tts.predict({ text, voiceId: VOICE });
      postMessage({ type: "audio", id, wav });
    } catch (err) {
      postMessage({ type: "error", id, error: String(err) });
    }
  });
};
