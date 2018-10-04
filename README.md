# TBN

This projects aims to provide music entertainment for toddlers. 
The idea is that children will get a WiFi connected box and some objects (e.g. PlayMobil figures) that are equipped with NFC stickers. Whenever the child is putting such an object on the box, the box will start to play audio.
The NFC to audio URL translation can be configured on a server in the Azure cloud. 
So a PlayMobil knight might make the box play a story about knights, or a picture with grandpa may play his voice telling a small story.

## Components

### Raspberry Pi

The Raspberry Pi will be connected to an NFC/RFID-Card reader and a speaker module.

### Server

The Server component will be run on Azure and provides a service that resolves NFC tags to download/streaming URLs.
