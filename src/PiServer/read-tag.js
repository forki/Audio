"use strict";
const mfrc522 = require("mfrc522-rpi");
//# Init WiringPi with SPI Channel 0
mfrc522.initWiringPi(0);

//# This loop keeps checking for chips. If one is near it will get the UID and authenticate
console.log("scanning...");
console.log("Please put chip or keycard in the antenna inductive zone!");
console.log("Press Ctrl-C to stop.");

setInterval(function(){
    //# reset card
    mfrc522.reset();

    //# Scan for cards
    let response = mfrc522.findCard();
    if (!response.status) {
        console.log("No Card");
        return;
    }
    console.log("Card detected, CardType: " + response.bitSize);

    //# Get the UID of the card
    response = mfrc522.getUid();
    if (!response.status) {
        console.log("UID Scan Error");
        return;
    }
    //# If we have the UID, continue
    const uid = response.data;
    var data = uid.map(x => x.toString(16)).join();
    console.log("Card read UID: %s", data);

    //# Stop
    mfrc522.stopCrypto();
}, 500);


exports.play = function (callback, fileName) {
	callback(null, "" + fileName + " finished");
}