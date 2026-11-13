// Lets the game talk to the web page it runs in.
mergeInto(LibraryManager.library, {
  // The game is running: the page swaps its loading bar for the Enter button.
  WebPage_Ready: function () {
    if (typeof window.hideLoading === "function") window.hideLoading();
  },

  // What the game wants from the mouse pointer: captured for looking in computer mode, unless
  // the menu is open (the page captures it inside clicks; see index.html).
  WebPage_SetPointer: function (computer, menuOpen) {
    window.gamePointer = { computer: !!computer, menuOpen: !!menuOpen };
  },

  // 1 while the browser really has the pointer captured.
  WebPage_PointerLocked: function () {
    return document.pointerLockElement ? 1 : 0;
  },

  // 1 once the visitor has pressed Enter (the game then joins the room, and reveals itself).
  WebPage_Entered: function () {
    return window.gameEntered ? 1 : 0;
  },

  // The room is joined: the Enter screen fades away and the game starts.
  WebPage_Reveal: function () {
    if (typeof window.revealGame === "function") window.revealGame();
  },

  // The name typed on the Enter screen.
  WebPage_PlayerName: function () {
    var name = String(window.playerName || "");
    var size = lengthBytesUTF8(name) + 1;
    var buffer = _malloc(size);
    stringToUTF8(name, buffer, size);
    return buffer;
  },

  // 1 while the page's loading / Enter screen still covers the game.
  WebPage_Covered: function () {
    return window.gameCovered ? 1 : 0;
  }
});
