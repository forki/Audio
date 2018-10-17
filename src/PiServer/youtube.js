"use strict";
const ytdl = require('ytdl-core');
const fs = require('fs');

exports.download = function (callback, url) {
    url = "https://www.youtube.com/watch?v=v_FhYVU_6Ws";
    var fileName = 'video.flv';
    ytdl(url).pipe(fs.createWriteStream(fileName));
    callback(null, fileName);
}
