// Browser text-to-speech for the grandmaster (the desktop build uses Piper instead).
mergeInto(LibraryManager.library, {
  WebSpeechAvailable: function () {
    return (typeof window !== "undefined" && "speechSynthesis" in window) ? 1 : 0;
  },

  WebSpeak: function (textPtr) {
    if (!("speechSynthesis" in window)) return 0;
    var text = UTF8ToString(textPtr);
    var synth = window.speechSynthesis;
    synth.cancel();
    var u = new SpeechSynthesisUtterance(text);
    var voices = synth.getVoices();
    // an older British man suits a wizard; fall back to any British, then any English voice
    var prefer = [/UK English Male/i, /Daniel/i, /George/i, /Arthur/i, /Ryan/i, /Oliver/i, /Male/i];
    var gb = voices.filter(function (v) { return /^en[-_]GB/i.test(v.lang); });
    var en = voices.filter(function (v) { return /^en/i.test(v.lang); });
    var pick = null;
    for (var i = 0; i < prefer.length && !pick; i++)
      pick = gb.find(function (v) { return prefer[i].test(v.name); }) || null;
    pick = pick || gb[0] || en.find(function (v) { return /Male|David|Daniel|Mark|Guy/i.test(v.name); }) || en[0] || null;
    if (pick) { u.voice = pick; u.lang = pick.lang; } else u.lang = "en-GB";
    u.rate = 0.95;
    u.pitch = 0.8;
    window.__kfSpeech = { started: false, ended: false, boundary: 0 };
    u.onstart = function () { window.__kfSpeech.started = true; };
    u.onend = function () { window.__kfSpeech.ended = true; };
    u.onerror = function () { window.__kfSpeech.ended = true; };
    u.onboundary = function () { window.__kfSpeech.boundary = performance.now(); };
    synth.speak(u);
    return 1;
  },

  // 0 = not started yet, 1 = speaking, 2 = finished (or failed)
  WebSpeechState: function () {
    var s = window.__kfSpeech;
    if (!s) return 2;
    if (s.ended) return 2;
    return s.started ? 1 : 0;
  },

  // milliseconds since the last word boundary, for a simple mouth/gesture animation
  WebSpeechSinceWord: function () {
    var s = window.__kfSpeech;
    return s && s.boundary ? performance.now() - s.boundary : 100000;
  },

  WebSpeechStop: function () {
    if ("speechSynthesis" in window) window.speechSynthesis.cancel();
    if (window.__kfSpeech) window.__kfSpeech.ended = true;
  },
});
