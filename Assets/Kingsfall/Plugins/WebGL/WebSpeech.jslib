// Browser text-to-speech for the grandmaster. The work is done by kf-tts.js in the page
// (Piper in a web worker, falling back to the browser's own voice); the desktop build runs Piper directly.
mergeInto(LibraryManager.library, {
  WebSpeechAvailable: function () {
    return (typeof window !== "undefined" && window.kfTts) ? 1 : 0;
  },
  WebSpeechPrepare: function (textPtr) {
    if (window.kfTts) window.kfTts.prepare(UTF8ToString(textPtr));
  },
  WebSpeak: function (textPtr, volume) {
    if (!window.kfTts) return 0;
    return window.kfTts.speak(UTF8ToString(textPtr), volume);
  },
  // 0 = waiting for audio, 1 = speaking, 2 = finished (or nothing to say)
  WebSpeechState: function () {
    return window.kfTts ? window.kfTts.state() : 2;
  },
  WebSpeechHeard: function () {
    return window.kfTts && window.kfTts.heard() ? 1 : 0;
  },
  WebSpeechLevel: function () {
    return window.kfTts ? window.kfTts.level() : 0;
  },
  WebSpeechStop: function () {
    if (window.kfTts) window.kfTts.stop();
  },
});
