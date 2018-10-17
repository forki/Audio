"use strict";
const ytdl = require('ytdl-core');
const fs = require('fs');

exports.download = function (callback, url) {
    var fileName = 'youtube.flv';
    ytdl(url)
      .pipe(fs.createWriteStream(fileName));

    callback(null, fileName);
}
