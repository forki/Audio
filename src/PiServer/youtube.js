"use strict";
const ytdl = require('ytdl-core');
const fs = require('fs');

exports.download = function (callback, url) {
    var fileName = 'youtube.mp3';
    ytdl(url,{ filter: (format) => format.container === 'mp3' })
      .pipe(fs.createWriteStream(fileName));
    callback(null, fileName);
}
