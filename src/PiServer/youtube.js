"use strict";

exports.download = function (callback, url) {
    const fs = require('fs');
    const ytdl = require('ytdl-core');

    var fileName = 'youtube.flv';
    ytdl(url, { filter: 'audioonly' })
      .pipe(fs.createWriteStream(fileName));

    callback(null, fileName);
}
