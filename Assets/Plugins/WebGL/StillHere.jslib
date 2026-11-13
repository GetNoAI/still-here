// Still Here: bridges from the game to its web page (Assets/WebGLTemplates/StillHere/index.html).
mergeInto(LibraryManager.library, {
  // Gentle haptic feedback; the page decides how (vibrate on Android, a best-effort tick on iPhone).
  SH_Haptic: function (kind) {
    try { if (window.shHaptic) window.shHaptic(kind); } catch (e) {}
  },
  // 1 when the page has switched the browser's 3D audio to HRTF.
  SH_BrowserHrtf: function () {
    return window.__shHrtf ? 1 : 0;
  }
});
