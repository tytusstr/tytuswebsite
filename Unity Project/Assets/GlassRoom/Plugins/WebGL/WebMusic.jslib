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
      // Smoothly moves the volume to a value over a few hundredths of a second, so starting
      // and stopping never clicks or glitches.
      m.fadeTo = function (value, seconds) {
        if (!m.gain || !m.ctx) return;
        var g = m.gain.gain, now = m.ctx.currentTime;
        g.cancelScheduledValues(now);
        g.setValueAtTime(g.value, now);
        g.linearRampToValueAtTime(value, now + seconds);
      };
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
        if (m.wantPlay && m.el.paused && !m.ended) {
          var p = m.el.play();
          if (p && p.then) p.then(function () { m.fadeTo(m.volume, 0.08); }, function () {});
        } else if (m.wantPlay && m.gain && m.gain.gain.value < m.volume) m.fadeTo(m.volume, 0.08);
      };
      ["pointerdown", "touchend", "mousedown", "keydown"].forEach(function (t) { window.addEventListener(t, unlock, true); });
      m.graph();
    }
    m.ended = false;
    m.wantPlay = true;
    m.pauseId = (m.pauseId || 0) + 1;
    // A new song starts silent and fades in once it's actually playing.
    if (m.gain) {
      m.gain.gain.cancelScheduledValues(0);
      m.gain.gain.value = 0;
    }
    if (m.ctx && m.ctx.state !== "running") m.ctx.resume();
    m.pendingSeek = null;
    m.el.src = url;
    var p = m.el.play();
    var fadeIn = function () { if (m.wantPlay) m.fadeTo(m.volume, 0.08); };
    if (p && p.then) p.then(fadeIn, function () {}); else fadeIn();
  },

  WebMusic_Pause: function () {
    var m = window.__webMusic;
    if (!m) return;
    m.wantPlay = false;
    m.pauseId = (m.pauseId || 0) + 1;
    var id = m.pauseId;
    // Fade out quickly, then pause the song. The browser's sound system itself keeps running:
    // stopping and restarting it is what made a small hiccup when unpausing.
    m.fadeTo(0, 0.05);
    setTimeout(function () {
      if (m.wantPlay || m.pauseId !== id) return;
      m.el.pause();
    }, 70);
  },

  WebMusic_Resume: function () {
    var m = window.__webMusic;
    if (!m) return;
    m.wantPlay = true;
    m.pauseId = (m.pauseId || 0) + 1;
    var id = m.pauseId;
    if (m.gain) {
      m.gain.gain.cancelScheduledValues(0);
      m.gain.gain.value = 0;
    }
    if (m.ctx && m.ctx.state !== "running") m.ctx.resume();
    // Silent until the song is truly playing again, then a gentle fade up, so the restart
    // itself can never be heard.
    var fadeIn = function () {
      m.el.removeEventListener("playing", fadeIn);
      if (!m.wantPlay || m.pauseId !== id) return;
      setTimeout(function () { if (m.wantPlay && m.pauseId === id) m.fadeTo(m.volume, 0.25); }, 40);
    };
    // Unpaused again before the pause finished: the song never stopped, so just fade back up.
    if (!m.el.paused) { m.fadeTo(m.volume, 0.25); return; }
    m.el.addEventListener("playing", fadeIn);
    var p = m.el.play();
    if (p && p.catch) p.catch(function () {});
  },

  WebMusic_SetVolume: function (volume) {
    var m = window.__webMusic;
    if (!m) return;
    m.volume = volume;
    if (m.gain) { if (m.wantPlay) m.fadeTo(volume, 0.1); }
    else m.el.volume = Math.max(0, Math.min(1, volume));
  },

  // Jumps the current song to a time (seconds), e.g. to match other players.
  // A song that's still loading jumps there as soon as it can.
  WebMusic_Seek: function (seconds) {
    var m = window.__webMusic;
    if (!m) return;
    seconds = Math.max(0, seconds);
    if (m.el.readyState < 1) {
      if (m.pendingSeek == null) {
        m.el.addEventListener("loadedmetadata", function apply() {
          m.el.removeEventListener("loadedmetadata", apply);
          if (m.pendingSeek != null) { try { m.el.currentTime = m.pendingSeek; } catch (e) {} }
          m.pendingSeek = null;
        });
      }
      m.pendingSeek = seconds;
      return;
    }
    m.pendingSeek = null;
    try { m.el.currentTime = seconds; } catch (e) {}
  },

  // How far into the current song it is, in seconds.
  WebMusic_Position: function () {
    var m = window.__webMusic;
    if (!m || !m.el) return 0;
    if (m.pendingSeek != null) return m.pendingSeek;
    return m.el.currentTime || 0;
  },

  WebMusic_Ended: function () {
    var m = window.__webMusic;
    return m && m.ended ? 1 : 0;
  }
});
