// Web demo helpers (polymart.be build). See WebDemo.cs.
mergeInto(LibraryManager.library, {

  // Reports a finished demo run to the page, which counts it. stats.js reports the start by
  // itself, but only the game knows when the demo chapter is cleared. The guard covers a page
  // copied without stats.js: the demo must keep working, it just stops being counted.
  GW_ReportGameCompleted: function () {
    if (window.PolymartStats && window.PolymartStats.gameCompleted)
      window.PolymartStats.gameCompleted();
  },

  // Opens a URL in a new tab; if the browser refuses the popup (no user gesture in progress,
  // e.g. the press came from a gamepad), opens it in the current tab instead.
  GW_OpenUrl: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var win = null;
    // No 'noopener' feature: with it window.open always returns null, which would look like a
    // refused popup and open the page a second time. The opener link is cut by hand instead.
    try { win = window.open(url, '_blank'); } catch (e) { win = null; }
    if (win) { try { win.opener = null; } catch (e) { /* cross-origin already */ } }
    else window.location.href = url;
  }
});
