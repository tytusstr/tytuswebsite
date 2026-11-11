// Lets the game talk to the web page it runs in.
mergeInto(LibraryManager.library, {
  // The game is running: the page swaps its loading bar for the Enter button.
  WebPage_Ready: function () {
    if (typeof window.hideLoading === "function") window.hideLoading();
  },

  // 1 while the page's loading / Enter screen still covers the game.
  WebPage_Covered: function () {
    return window.gameCovered ? 1 : 0;
  }
});
