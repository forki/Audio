# TBN

This projects aims to provide music entertainment for toddlers.

The idea is that children will get a WiFi connected box and some objects (e.g. PlayMobil figures) that are equipped with NFC stickers. Whenever the child is putting such an object on the box, the box will start to play audio.
The NFC to audio URL translation can be configured on a server in the Azure cloud.
So a PlayMobil knight might make the box play a story about knights, or a picture with grandpa may play his voice telling a small story.

There are couple of commercial solutions for this, but this project aims to create an open platform.

## Components

### Raspberry Pi

The Raspberry Pi will be connected to an NFC/RFID-Card reader and a speaker module.

[TODO: Explaing wiring]

### Server

The Server component will be run on Azure and provides a service that resolves NFC tags to download/streaming URLs.

### WebClient

The web client will allow users to configure their Raspberry Pi and add NFC tags or upload audio files.

## Additional features

### Local storage

Users may want to keep audio files in their local network. The web client will allow to configure such links to local NAS.

### Business times

Nobody wants their children to stay awake all night to play with this box. The website will allow to configure times where the system is inactive.

## Installation

### Preparing the SD-Card

#### Flash operating system on SD-Card

* Download latest [Raspbian image with Desktop](https://www.raspberrypi.org/downloads/raspbian/)
* Use [Etcher](https://etcher.io/) to flash the image onto your SD-Card

#### Activate WiFi

* Create a file called `/boot/wpa_supplicant.conf` on the SD-Card and put the follwoing content in:

```ini
ctrl_interface=DIR=/var/run/wpa_supplicant GROUP=netdev
update_config=1
country=<<Your_ISO-3166-1_two-letter_country_code>>

network={
    ssid="<<Your_SSID>>"
    psk="<<Your_PSK>>"
    key_mgmt=WPA-PSK
}
```

* Replace <<Your_ISO-3166-1_two-letter_country_code>> with your ISO Country Code (such as DE for Germany), <<Your_SSID>> with your wireless access point name and <<Your_PSK>> with your wifi password.

#### Activate SSH

* Create a file called `ssh` on the SD-Card. It can be left empty.