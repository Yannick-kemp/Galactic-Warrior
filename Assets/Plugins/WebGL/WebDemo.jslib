// Web demo helpers (polymart.be build). See WebDemo.cs.
mergeInto(LibraryManager.library, {

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
