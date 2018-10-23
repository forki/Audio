#!/bin/sh

# volume 1%, XXX=0.01
# volume 10%, XXX=0.1
# volume 50%, XXX=0.5
# volume 100%, XXX=1
# volume 150%, XXX=1.5

export DBUS_SESSION_BUS_ADDRESS=$(cat /tmp/omxplayerdbus.${USER:-root})
dbus-send --print-reply --session --reply-timeout=500 \
           --dest=org.mpris.MediaPlayer2.omxplayer \
           /org/mpris/MediaPlayer2 org.freedesktop.DBus.Properties.Set \
           string:"org.mpris.MediaPlayer2.Player" \
           string:"Volume" double:0.5   # <-- XXX=0.5 (50% sound volume)