"use strict";

function decimalToHex(d) {
    var padding = 2;
    var hex = Number(d).toString(16);
    padding = typeof (padding) === "undefined" || padding === null ? padding = 2 : padding;

    while (hex.length < padding) {
        hex = "0" + hex;
    }

    return hex;
}

exports.read = function (callback, lastTag) {
        callback(null, "");
}
