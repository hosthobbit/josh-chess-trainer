// Speech for the grandmaster in the browser build, called from Unity via WebSpeech.jslib.
// 1. Piper in a web worker (kf-tts-worker.js), spoken sentence by sentence as each is ready.
// 2. If that can't load: the browser's own speechSynthesis voice (often missing on Linux).
// 3. Otherwise nothing, and the game just shows the subtitles.
(function () {
  var S = {
    status: 0,          // piper: 0 loading, 1 ready, -1 unavailable
    worker: null,
    cache: new Map(),   // sentence -> Promise<blob url | null>
    pending: new Map(), // request id -> resolve
    nextId: 1,
    state: 2,           // 0 waiting for audio, 1 speaking, 2 finished/idle
    heard: false,       // did the latest line actually produce sound
    token: 0,
    audio: null, ctx: null, analyser: null, buf: null, finishPlay: null,
    volume: 1,
  };

  function sentences(text) {
    var parts = text.match(/[^.!?]+[.!?]+["')\]]*|[^.!?]+$/g) || [text];
    return parts.map(function (s) { return s.trim(); }).filter(function (s) { return s.length > 0; });
  }

  function hasBrowserVoice() {
    return !!(window.speechSynthesis && window.speechSynthesis.getVoices().some(function (v) { return /^en/i.test(v.lang); }));
  }

  // Phones and tablets have good built-in voices, and the 63 MB Piper model is heavy for them:
  // use the built-in voice there when it exists. Desktops (Linux Chromium often has none) get Piper.
  function init(mobile) {
    if (mobile !== undefined) S.mobile = !!mobile;
    if (S.mobile && hasBrowserVoice()) { S.status = -1; return; }
    if (S.worker || S.status < 0) return;
    try {
      if (!navigator.storage || !navigator.storage.getDirectory) throw new Error("no OPFS");
      S.worker = new Worker("kf-tts-worker.js", { type: "module" });
      S.worker.onmessage = onMessage;
      S.worker.onerror = function (e) { console.warn("kfTts worker failed", e.message); fail(); };
    } catch (e) {
      console.warn("kfTts: piper unavailable", e);
      fail();
    }
  }

  function fail() {
    S.status = -1;
    S.pending.forEach(function (resolve) { resolve(null); });
    S.pending.clear();
  }

  function onMessage(e) {
    var m = e.data;
    if (m.type === "ready") { S.status = 1; console.log("kfTts: grandmaster voice ready"); }
    else if (m.type === "failed") { console.warn("kfTts: voice download failed", m.error); fail(); }
    else if (m.type === "audio" || m.type === "error") {
      var resolve = S.pending.get(m.id);
      if (!resolve) return;
      S.pending.delete(m.id);
      if (m.type === "error") console.warn("kfTts: synthesis failed", m.error);
      resolve(m.type === "audio" ? URL.createObjectURL(m.wav) : null);
    }
  }

  function synth(sentence) {
    if (S.cache.has(sentence)) return S.cache.get(sentence);
    var p = new Promise(function (resolve) {
      if (!S.worker || S.status < 0) { resolve(null); return; }
      var id = S.nextId++;
      S.pending.set(id, resolve);
      S.worker.postMessage({ id: id, text: sentence });
    });
    S.cache.set(sentence, p);
    p.then(function (url) { if (!url) S.cache.delete(sentence); });
    return p;
  }

  function ensureAudio() {
    if (S.audio) return;
    S.audio = new Audio();
    try {
      S.ctx = new (window.AudioContext || window.webkitAudioContext)();
      var src = S.ctx.createMediaElementSource(S.audio);
      S.analyser = S.ctx.createAnalyser();
      S.analyser.fftSize = 512;
      S.buf = new Uint8Array(S.analyser.fftSize);
      src.connect(S.analyser);
      S.analyser.connect(S.ctx.destination);
    } catch (e) { S.analyser = null; }
  }

  function playUrl(url, token) {
    ensureAudio();
    return new Promise(function (resolve) {
      if (token !== S.token) { resolve(); return; }
      var a = S.audio;
      var done = function () { a.onended = a.onerror = null; S.finishPlay = null; resolve(); };
      S.finishPlay = done;
      a.onended = done;
      a.onerror = done;
      a.src = url;
      a.volume = Math.max(0, Math.min(1, S.volume));
      if (S.ctx && S.ctx.state === "suspended") S.ctx.resume();
      var p = a.play();
      if (p && p.catch) p.catch(function () { done(); });
      S.state = 1;
      S.heard = true;
    });
  }

  function speakBrowser(text, token) {
    var synthApi = window.speechSynthesis;
    var voices = synthApi ? synthApi.getVoices() : [];
    if (!synthApi || voices.length === 0) { S.state = 2; return; }
    var u = new SpeechSynthesisUtterance(text);
    var gb = voices.filter(function (v) { return /^en[-_]GB/i.test(v.lang); });
    var en = voices.filter(function (v) { return /^en/i.test(v.lang); });
    var pick = gb.find(function (v) { return /Male|Daniel|George|Arthur|Ryan|Oliver/i.test(v.name); }) || gb[0] || en[0];
    if (pick) { u.voice = pick; u.lang = pick.lang; }
    u.rate = 0.95; u.pitch = 0.8; u.volume = S.volume;
    u.onstart = function () { if (token === S.token) { S.state = 1; S.heard = true; } };
    u.onend = u.onerror = function () { if (token === S.token) S.state = 2; };
    synthApi.speak(u);
    setTimeout(function () { if (token === S.token && S.state === 0) S.state = 2; }, 3000);
  }

  async function speak(text, volume) {
    stop();
    var token = ++S.token;
    S.volume = volume;
    S.state = 0;
    S.heard = false;
    init();
    if (S.status < 0) { speakBrowser(text, token); return; }
    var parts = sentences(text).map(synth);
    var played = 0;
    for (var i = 0; i < parts.length; i++) {
      var url = await parts[i];
      if (token !== S.token) return;
      if (!url) continue;
      await playUrl(url, token);
      played++;
      if (token !== S.token) return;
    }
    if (token !== S.token) return;
    if (played === 0 && S.status < 0) { speakBrowser(text, token); return; }
    S.state = 2;
  }

  function stop() {
    S.token++;
    S.state = 2;
    if (S.audio) S.audio.pause();
    if (S.finishPlay) S.finishPlay();
    if (window.speechSynthesis) window.speechSynthesis.cancel();
  }

  function level() {
    if (S.state !== 1) return 0;
    if (!S.analyser) return 0.5;
    S.analyser.getByteTimeDomainData(S.buf);
    var sum = 0;
    for (var i = 0; i < S.buf.length; i++) { var v = (S.buf[i] - 128) / 128; sum += v * v; }
    return Math.min(1, Math.sqrt(sum / S.buf.length) * 6);
  }

  // iOS only lets a page make sound after a tap: unlock the audio element and the speech engine on the first one
  function unlock() {
    ensureAudio();
    if (S.ctx && S.ctx.state === "suspended") S.ctx.resume();
    try {
      S.audio.src = "data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=";
      var p = S.audio.play(); if (p && p.catch) p.catch(function () {});
    } catch (e) {}
    if (window.speechSynthesis) {
      var u = new SpeechSynthesisUtterance(" ");
      u.volume = 0;
      window.speechSynthesis.speak(u);
    }
  }
  window.addEventListener("touchend", unlock, { once: true });
  window.addEventListener("pointerup", function first() { window.removeEventListener("pointerup", first); unlock(); });

  window.kfTts = {
    init: init,
    prepare: function (text) { init(); if (S.status >= 0) sentences(text).forEach(synth); },
    speak: function (text, volume) { speak(text, volume); return 1; },
    state: function () { return S.state; },
    status: function () { return S.status; },
    heard: function () { return S.heard; },
    level: level,
    stop: stop,
  };
})();
