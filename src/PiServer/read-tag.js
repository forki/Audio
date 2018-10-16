"use strict";
const mfrc522 = require("mfrc522-rpi");
//# Init WiringPi with SPI Channel 0
mfrc522.initWiringPi(0);

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
    mfrc522.reset();

    let response = mfrc522.findCard();
    if (!response.status) {
        callback(null, "");
        return;
    }

    //# Get the UID of the card
    response = mfrc522.getUid();
    if (!response.status) {
        callback(null, "");
        return;
    }

    const uid = response.data;
    var data = uid.map(x => decimalToHex(x)).join('');
    callback(null, data);
}
