#if !MODULARIZE || MODULARIZE_INSTANCE
  var Module = {
#if USE_PTHREADS
    worker: '{{{ PTHREAD_WORKER_FILE }}}' + location.search
#endif
};
#endif

#if WASM == 2
  var supportsWasm = window.WebAssembly;
#endif

// The action to perform when a fatal error occurs that cannot be recovered from. Override this function
// in your own shell file to take a different action. Default behavior is to show a red error ribbon
// at the top of the page.
function fatal(msg) {
	// Emscripten runtime throws a string "unwind" to unwind out from a nested Wasm callstack to yield execution back to the browser
	if (msg.indexOf("unwind") != -1)
		return;
	fatal.count = (fatal.count|0)+1;
	if (fatal.count < 20) { // Cap the number of errors that are printed to prevent an infinite cascade if there is an error each frame.
		document.querySelector('#error_log').innerHTML += "<div style='background-color:red; border: 2px solid yellow; padding: 5px; font-weight: bold; font-size: 2em '>" + msg + '</div>';
	}
}

// The action to perform when a runtime browser warning occurs. Override this function in your own shell file
// to customize the behavior. Default behavior is to show a yellow warning ribbon at the top of the page
// that disappears after a while.
function warn(msg) {
	warn.count = (warn.count|0)+1;
	if (warn.count < 3) { // Cap the number of simultaneous warnings.
		var div = document.createElement('div');
		div.innerHTML = "<div style='background-color:yellow; border: 2px solid black; padding: 5px; font-weight: bold; font-size: 2em;'>" + msg + ' <span style="cursor: pointer">[X]</span></div>';
		function removeWarning() { if (div.parentNode) div.parentNode.removeChild(div); }
		document.querySelector('#error_log').appendChild(div);
		div.querySelector('span').onclick = removeWarning;
		setTimeout(removeWarning, 20000);
	}
}

// By default, uncaught global errors are routed to fatal error handler. Override these in your own
// shell file to take a different action.
window.onerror = function(message, source, lineno, colno) {
	var msg = source;
	if (lineno) msg += ':' + lineno;
	if (colno) msg += ':' + colno;
	msg += (msg ? ':':'') + message;
	fatal(msg);
}

function doneWaitingForDebugger() {
  document.getElementById("waitForManagedDebugger").style.display = "none";
}

// Register a callback function that will be called when the given event occurs.
// The input system will invoke callbacks for the input events like keydown, keyup.
var TinyEventManager = {
	listeners: {},

	addEventListener: function(type, callback) {
		this.listeners[type] = this.listeners[type] || [];
		if (this.listeners[type].indexOf(callback) == -1) {
			this.listeners[type].push(callback);
		}
	},

	removeEventListener: function(type, callback) {
		if (this.listeners[type]) {
			var index = this.listeners[type].indexOf(callback);
			if (index != -1) {
				this.listeners[type].splice(index, 1);
			}
		}
	},

	dispatchEvent: function(event) {
		if (this.listeners[event.type]) {
			this.listeners[event.type].forEach((callback) => callback(event));
		}
	}
};


// Example action: do not navigate the browser page on space bar key presses.
function keyEvent(ev) {
	if (document.activeElement == document.querySelector('#UT_CANVAS') && ev.key == ' ') {
		ev.preventDefault();
	}
}
TinyEventManager.addEventListener('keydown', keyEvent);
TinyEventManager.addEventListener('keyup', keyEvent);

// Depending on the build flags that one uses, different files need to be downloaded
// to load the compiled page. The right set of files will be expanded to be downloaded
// via the directive below.

{{{ DOWNLOAD_JS_AND_WASM_FILES }}}

#if SINGLE_FILE
  var SINGLE_FILE_ASSETS = {};

#if WASM2JS
  // when building wasm2js, the default base64Decode() function is not embedded by default
  // by Emscripten, so embed it explicitly.
#include "base64Decode.js"
#endif

// If we are doing a SINGLE_FILE=1 build, inlined JS runtime code follows here:
{{{ JS_CONTENTS_IN_SINGLE_FILE_BUILD }}}

#endif
