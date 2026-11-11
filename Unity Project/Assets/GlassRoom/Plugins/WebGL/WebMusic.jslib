// Plays the speaker's music in a web browser by streaming the song files with the browser's own
// audio player, instead of Unity unpacking whole songs into memory. Volume goes through a Web
// Audio gain node, since iPhones ignore an audio element's own volume. Browsers only allow sound
// after the player touches, clicks or presses a key, so playback (re)starts on the first of those.
mergeInto(LibraryManager.library, {
  WebMusic_Play: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var m = window.__webMusic;
    if (!m) {
      m = window.__webMusic = { el: new Audio(), ctx: null, gain: null, wantPlay: false, ended: false, volume: 1 };
      m.el.preload = "auto";
      m.el.setAttribute("playsinline", "");
      m.el.addEventListener("ended", function () { m.ended = true; });
      m.graph = function () {
        if (m.ctx) return;
        try {
          var AC = window.AudioContext || window.webkitAudioContext;
          m.ctx = new AC();
          var source = m.ctx.createMediaElementSource(m.el);
          m.gain = m.ctx.createGain();
          m.gain.gain.value = m.volume;
          source.connect(m.gain);
          m.gain.connect(m.ctx.destination);
        } catch (e) { m.ctx = null; m.gain = null; }
      };
      var unlock = function () {
        m.graph();
        if (!m.wantPlay) return;
        if (m.ctx && m.ctx.state !== "running") m.ctx.resume();
        if (m.wantPlay && m.el.paused && !m.ended) { var p = m.el.play(); if (p && p.catch) p.catch(function () {}); }
      };
      ["pointerdown", "touchend", "mousedown", "keydown"].forEach(function (t) { window.addEventListener(t, unlock, true); });
      m.graph();
    }
    m.ended = false;
    m.wantPlay = true;
    if (m.gain) m.gain.gain.value = m.volume;
    if (m.ctx && m.ctx.state !== "running") m.ctx.resume();
    m.el.src = url;
    var p = m.el.play();
    if (p && p.catch) p.catch(function () {});
  },

  WebMusic_Pause: function () {
    var m = window.__webMusic;
    if (!m) return;
    m.wantPlay = false;
    // iPhones can keep repeating the last moment of sound after a pause, so silence the
    // output and stop the mixer too, not just the player.
    if (m.gain) m.gain.gain.value = 0;
    m.el.pause();
    if (m.ctx && m.ctx.state === "running" && m.ctx.suspend) m.ctx.suspend();
  },

  WebMusic_Resume: function () {
    var m = window.__webMusic;
    if (!m) return;
    m.wantPlay = true;
    if (m.gain) m.gain.gain.value = m.volume;
    if (m.ctx && m.ctx.state !== "running") m.ctx.resume();
    var p = m.el.play();
    if (p && p.catch) p.catch(function () {});
  },

  WebMusic_SetVolume: function (volume) {
    var m = window.__webMusic;
    if (!m) return;
    m.volume = volume;
    if (m.gain) { if (m.wantPlay) m.gain.gain.value = volume; }
    else m.el.volume = Math.max(0, Math.min(1, volume));
  },

  WebMusic_Ended: function () {
    var m = window.__webMusic;
    return m && m.ended ? 1 : 0;
  }
});
