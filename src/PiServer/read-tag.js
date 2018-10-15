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
        callback(null, "");
        return;
    }

    const uid = response.data;
    var data = uid.map(x => x.toString(16)).join('');
    if (uid.length < 7){
        data = uid[0].toString(16) + uid[1].toString(16) + uid[2].toString(16) + uid[3].toString(16);
    }
    callback(null, data);
}
