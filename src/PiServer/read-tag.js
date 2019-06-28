"use strict";
const Mfrc522 = require("mfrc522-rpi");
const SoftSPI = require("rpi-softspi");

const softSPI = new SoftSPI({
    clock: 23, // pin number of SCLK
    mosi: 19, // pin number of MOSI
    miso: 21, // pin number of MISO
    client: 24 // pin number of CS
  });
  
// GPIO 24 can be used for buzzer bin (PIN 18), Reset pin is (PIN 22).
const mfrc522 = new Mfrc522(softSPI).setResetPin(22).setBuzzerPin(18);

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
