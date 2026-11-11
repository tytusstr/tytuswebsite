// Lets the game tell the web page it's up and running, so the page removes its loading cover.
mergeInto(LibraryManager.library, {
  WebPage_Ready: function () {
    if (typeof window.hideLoading === "function") window.hideLoading();
  }
});
