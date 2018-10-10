#!/bin/sh

DOTNETREQ=2.1.5
SCRIPTROOT=`dirname "$0"`
SOURCEPATH=$SCRIPTROOT/bin
DEFAULTDAEMONHOME=/home/pi/PiServer/publish
DEFAULTS=/etc/default/PiServer

DAEMONHOME=$1
if [ "x$DAEMONHOME" = "x" ]; then
    if [ -e  $SCRIPTROOT/PiServer.defaults ]; then
        . $SCRIPTROOT/PiServer.defaults
    else
        DAEMONHOME=$DEFAULTDAEMONHOME
    fi
fi

sudo apt-get update
sudo apt-get install curl libunwind8 gettext apt-transport-https omxplayer pulseaudio-module-bluetooth

curl -sSL -o dotnet.tar.gz https://download.visualstudio.microsoft.com/download/pr/4d555219-1f04-47c6-90e5-8b3ff8989b9c/0798763e6e4b98a62846116f997d046e/dotnet-runtime-2.1.5-linux-arm.tar.gz
sudo mkdir -p /opt/dotnet && sudo tar zxf dotnet.tar.gz -C /opt/dotnet
rm dotnet.tar.gz
sudo ln -s /opt/dotnet/dotnet /usr/local/bin

pulseaudio -k
pulseaudio --start

if [ -e  $SCRIPTROOT/PiServer ]; then
    if [ ! -e $DEFAULTS -a $SCRIPTROOT/PiServer.defaults ]; then
        cat $SCRIPTROOT/PiServer.defaults | grep -v "DAEMONHOME=" > $DEFAULTS
        echo DAEMONHOME=$DAEMONHOME >> $DEFAULTS
    fi

    if [ ! -d $DAEMONHOME ]; then
        mkdir -p $DAEMONHOME
    fi

    cp -r $SOURCEPATH/. $DAEMONHOME
    cp  $SCRIPTROOT/PiServer /etc/init.d/
    chmod +x /etc/init.d/PiServer
    chmod +x $DAEMONHOME/$NAME
    systemctl daemon-reload
    systemctl enable PiServer
    service PiServer start
    service PiServer status
fi
