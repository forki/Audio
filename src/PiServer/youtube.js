"use strict";

exports.download = function (callback, url) {
    const fs = require('fs');
    const ytdl = require('ytdl-core');

    var fileName = '/home/pi/youtube.flv';
    ytdl(url)
      .pipe(fs.createWriteStream(fileName));

    callback(null, fileName);
}
