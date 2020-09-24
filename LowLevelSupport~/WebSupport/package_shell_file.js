const fs = require("fs");
const path = require("path");

var outputPath = process.argv[2];
var html = fs.readFileSync(process.argv[3], "utf8");
var js = fs.readFileSync(process.argv[4], "utf8");
var assetPaths = process.argv[5] ? fs.readFileSync(process.argv[5], "utf8").split(/\r?\n/).filter(Boolean) : [];

var SINGLE_FILE_ASSETS = {};
assetPaths.forEach(function (assetPath) {
  SINGLE_FILE_ASSETS["Data/" + path.basename(assetPath)] = fs.readFileSync(assetPath, "base64");
});

fs.writeFileSync(outputPath, html.replace(/\{\s*\{\s*\{\s*TINY_SHELL\s*\}\s*\}\s*\}/, js.replace(/(?<=\WSINGLE_FILE_ASSETS\s*=\s*)\{\s*\}/, JSON.stringify(SINGLE_FILE_ASSETS, null, 1))));
