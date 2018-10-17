"use strict";

exports.download = function (callback, url) {
    const fs = require('fs');
    const ytdl = require('ytdl-core');

    var fileName = 'youtube.flv';
    console.log("youtube url:" + url);
    ytdl(url)
      .pipe(fs.createWriteStream(fileName));

    callback(null, fileName);
}
