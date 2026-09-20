// Bridge between YouTubePlayables.cs and the YouTube Playables SDK (window.ytgame).
// The SDK script is loaded by index.html BEFORE the game (certification requirement), and the
// same page awaits ytgame.game.loadData() and stores the result in window.gwYtInitialSave
// before creating the Unity instance, so C# can read the save synchronously at startup.
//
// Every call is guarded: opened outside YouTube (IN_PLAYABLES_ENV false or no ytgame at all),
// the build still runs and these functions simply do nothing.

var YouTubePlayablesLib = {

  $gwYt: {
    inEnv: function () {
      return typeof ytgame !== 'undefined' && ytgame.IN_PLAYABLES_ENV === true;
    },
    warn: function (what, err) {
      console.warn('[YouTubePlayables] ' + what + ' failed', err);
    }
  },

  YT_InPlayablesEnv__deps: ['$gwYt'],
  YT_InPlayablesEnv: function () {
    return gwYt.inEnv() ? 1 : 0;
  },

  YT_GameReady__deps: ['$gwYt'],
  YT_GameReady: function () {
    if (!gwYt.inEnv()) return;
    try { ytgame.game.gameReady(); } catch (e) { gwYt.warn('gameReady', e); }
  },

  YT_GetInitialSaveData: function () {
    var data = (typeof window.gwYtInitialSave === 'string') ? window.gwYtInitialSave : '';
    var size = lengthBytesUTF8(data) + 1;
    var buffer = _malloc(size);
    stringToUTF8(data, buffer, size);
    return buffer;
  },

  YT_SaveData__deps: ['$gwYt'],
  YT_SaveData: function (dataPtr) {
    var data = UTF8ToString(dataPtr);
    if (!gwYt.inEnv()) {
      try { window.localStorage.setItem('gw_yt_save', data); } catch (e) { /* private mode */ }
      return;
    }
    try {
      ytgame.game.saveData(data).catch(function (e) { gwYt.warn('saveData', e); });
    } catch (e) { gwYt.warn('saveData', e); }
  },

  YT_IsAudioEnabled__deps: ['$gwYt'],
  YT_IsAudioEnabled: function () {
    if (!gwYt.inEnv()) return 1;
    try { return ytgame.system.isAudioEnabled() ? 1 : 0; } catch (e) { return 1; }
  },

  YT_RegisterCallbacks__deps: ['$gwYt'],
  YT_RegisterCallbacks: function (onPausePtr, onAudioPtr) {
    if (!gwYt.inEnv()) return;

    function audioContext() {
      return (typeof WEBAudio !== 'undefined' && WEBAudio.audioContext) ? WEBAudio.audioContext : null;
    }

    function mainLoop() {
      return (typeof Browser !== 'undefined' && Browser.mainLoop) ? Browser.mainLoop : null;
    }

    try {
      ytgame.system.onPause(function () {
        // Tell C# first (it records the state and freezes time), then stop everything:
        // the audio graph and Unity's frame loop. "MUST pause all execution".
        {{{ makeDynCall('vi', 'onPausePtr') }}}(1);
        var ctx = audioContext();
        if (ctx && ctx.state === 'running') ctx.suspend();
        var loop = mainLoop();
        if (loop) loop.pause();
      });

      ytgame.system.onResume(function () {
        var loop = mainLoop();
        if (loop) loop.resume();
        var ctx = audioContext();
        if (ctx && ctx.state === 'suspended') ctx.resume();
        {{{ makeDynCall('vi', 'onPausePtr') }}}(0);
      });

      ytgame.system.onAudioEnabledChange(function (enabled) {
        {{{ makeDynCall('vi', 'onAudioPtr') }}}(enabled ? 1 : 0);
      });
    } catch (e) {
      gwYt.warn('register callbacks', e);
    }
  },

  YT_LogError__deps: ['$gwYt'],
  YT_LogError: function (messagePtr) {
    var message = UTF8ToString(messagePtr);
    console.error('[GalacticWarrior] ' + message);
    if (!gwYt.inEnv()) return;
    try { ytgame.health.logError(); } catch (e) { /* best effort */ }
  }
};

autoAddDeps(YouTubePlayablesLib, '$gwYt');
mergeInto(LibraryManager.library, YouTubePlayablesLib);
