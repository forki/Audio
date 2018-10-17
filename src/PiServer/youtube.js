"use strict";
const ytdl = require('ytdl-core');
const fs = require('fs');

exports.download = function (callback, url) {
    var fileName = 'youtube.mpe';
    ytdl(url,{ filter: (format) => format.container === 'mp4' })
      .pipe(fs.createWriteStream(fileName));
    callback(null, fileName);
}
