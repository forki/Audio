"use strict";
const mfrc522 = require("mfrc522-rpi");
//# Init WiringPi with SPI Channel 0
mfrc522.initWiringPi(0);

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
        console.log("UID Scan Error");
        return;
    }

    const uid = response.data;
    var data = uid.map(x => x.toString(16)).join('');
    callback(null, data);
}
