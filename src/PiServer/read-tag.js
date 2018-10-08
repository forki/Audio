"use strict";
const mfrc522 = require("mfrc522-rpi");
//# Init WiringPi with SPI Channel 0
mfrc522.initWiringPi(0);

//# This loop keeps checking for chips. If one is near it will get the UID and authenticate
console.log("scanning...");
console.log("Please put chip or keycard in the antenna inductive zone!");
console.log("Press Ctrl-C to stop.");


exports.read = function (callback, fileName) {
    var lastTag = "";
    var readInterval = setInterval(function(){
        mfrc522.reset();
    
        let response = mfrc522.findCard();
        if (!response.status) {
            return;
        }

        clearInterval(readInterval);

        //# Get the UID of the card
        response = mfrc522.getUid();
        if (!response.status) {
            console.log("UID Scan Error");
            return;
        }
        //# If we have the UID, continue
        const uid = response.data;
        var data = uid.map(x => x.toString(16)).join('');
        if(lastTag != data) {
            lastTag = data;
            console.log("Card read UID: %s", data);
            callback(null, data);
        }
    }, 500);    
}

exports.removed = function (callback, lastTag) {
    var readInterval = setInterval(function(){
        mfrc522.reset();
    
        let response = mfrc522.findCard();
        if (!response.status) {
            clearInterval(readInterval);
            callback(null, "");
            return;
        }
    }, 500);    
}