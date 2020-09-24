/* This script crafts the final Emscripten MINIMAL_RUNTIME shell file from HTML and JS input fragments, and optionally
   embeds asset files to it if doing a SINGLE_FILE build.

  Usage:
  node package_shell_file.js
    --outputHtml /path/to/nameOfGeneratedOutput.html
    --inputShellHtml inputHtmlFile.html
    --inputShellJs inputJsFile.js
    [--assetRootDirectory /path/to/asset/root/]
    [--assetManifest /path/to/export.manifest]
*/

const fs = require("fs");
const path = require("path");

// In an array of ['--param1', 'value', '--param2', 'value'], removes and returns the value of last argument with given name.
console.error(process.argv.toString());
function getStringArg(name) {
  var args = process.argv.slice(2);
  var pos = args.lastIndexOf(name);
  if (pos != -1 && pos + 1 < args.length && args[pos+1][0] != '-') {
    return args[pos+1];
  }
}

// Reads a UTF-8 input text file.
function read(file) {
  return fs.readFileSync(file, "utf8");
}

// Optionally, if doing a SINGLE_FILE build where all asset files are embedded as base64 to the build,
// include them in the SINGLE_FILE_ASSETS variable:
var SINGLE_FILE_ASSETS = {};
var assetRootDirectory = getStringArg('--assetRootDirectory');
var assetManifest = getStringArg('--assetManifest');
var assetFiles = assetManifest ? read(assetManifest).split(/\r?\n/).filter(Boolean) : [];
assetFiles.forEach(function(assetFile) {
  // Root the files in the runtime SINGLE_FILE_ASSETS path namespace to be relative to
  // assetRootDirectory, and normalize Windows path delimiters to '/'.
  var destinationPath = path.relative(assetRootDirectory, assetFile).replace(/\\/g, '/');
  SINGLE_FILE_ASSETS[destinationPath] = fs.readFileSync(assetFile, "base64");
});

// Expand {{{ SINGLE_FILE_ASSETS }}}.
var js = read(getStringArg('--inputShellJs'));
js = js.replace(/(?<=\WSINGLE_FILE_ASSETS\s*=\s*)\{\s*\}/, JSON.stringify(SINGLE_FILE_ASSETS, null, 1));

// Expand {{{ TINY_SHELL }}}.
var html = read(getStringArg('--inputShellHtml'));
var shellHtml = html.replace(/\{\s*\{\s*\{\s*TINY_SHELL\s*\}\s*\}\s*\}/, js);

// Write final output shell file.
var outputHtml = getStringArg('--outputHtml');
fs.writeFileSync(outputHtml, shellHtml);
