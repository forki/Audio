"use strict";

exports.download = function (callback, url) {
    const fs = require('fs');
    const ytdl = require('ytdl-core');

    var fileName = '/home/pi/youtube.mp3';
    ytdl(url, { filter: (format) => format.container === 'mp3' })
      .pipe(fs.createWriteStream(fileName));

    callback(null, fileName);
}
