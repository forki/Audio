"use strict";

exports.download = function (callback, url) {
    const fs = require('fs');
    const ytdl = require('ytdl-core');

    var fileName = 'youtube.flv';
    var download = ytdl(url, { filter: 'audioonly' });
    download.pipe(fs.createWriteStream(fileName));

    download.on('end', function () {
        callback(null, fileName);
    });
}
