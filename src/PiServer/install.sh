#!/bin/sh

. /lib/lsb/init-functions

DOTNETREQ=2.1.5
SCRIPTROOT=`dirname "$0"`
SOURCEPATH=$SCRIPTROOT/bin
PROJECTNAME=PiServer
DEFAULTDAEMONHOME=/home/pi/$PROJECTNAME/publish
DEFAULTS=/etc/default/$PROJECTNAME

DAEMONHOME=$1

log_action_begin_msg "Loading $PROJECTNAME settings"
if [ "x$DAEMONHOME" = "x" ]; then
    if [ -e  $SCRIPTROOT/$PROJECTNAME.defaults ]; then
        . $SCRIPTROOT/$PROJECTNAME.defaults
    else
        DAEMONHOME=$DEFAULTDAEMONHOME
    fi
fi
log_action_end_msg $?

log_action_begin_msg "Updating packages "
log_action_cont_msg " apt update "
apt-get -qq update
log_action_cont_msg " apt install "
sudo apt-get install -y curl libunwind8 gettext apt-transport-https omxplayer pulseaudio-module-bluetooth npm unzip
log_action_end_msg $?

curl -sSL -o dotnet.tar.gz https://download.visualstudio.microsoft.com/download/pr/4d555219-1f04-47c6-90e5-8b3ff8989b9c/0798763e6e4b98a62846116f997d046e/dotnet-runtime-2.1.5-linux-arm.tar.gz
sudo mkdir -p /opt/dotnet && sudo tar zxf dotnet.tar.gz -C /opt/dotnet
rm dotnet.tar.gz
sudo ln -s /opt/dotnet/dotnet /usr/local/bin

pulseaudio -k
pulseaudio --start

if [ -e  $SCRIPTROOT/$PROJECTNAME ]; then
    if [ ! -e $DEFAULTS -a $SCRIPTROOT/$PROJECTNAME.defaults ]; then
        echo DAEMONHOME=$DAEMONHOME > $DEFAULTS
        cat $SCRIPTROOT/$PROJECTNAME.defaults | grep -v "DAEMONHOME=" >> $DEFAULTS
    fi

    if [ ! -d $DAEMONHOME ]; then
        mkdir -p $DAEMONHOME
    fi

    log_action_begin_msg "Install application into $DAEMONHOME "
    cp -r $SOURCEPATH/. $DAEMONHOME
    cp  $SCRIPTROOT/$PROJECTNAME /etc/init.d/
    log_action_end_msg $?

    log_action_begin_msg "Updating node_modules"
    cd $DAEMONHOME && npm --silent install > /dev/null 2>&1
    log_action_end_msg $?

    log_action_begin_msg "Enabling daemon"
    chmod +x /etc/init.d/$PROJECTNAME
    chmod +x $DAEMONHOME/$NAME
    systemctl daemon-reload
    systemctl enable $PROJECTNAME
    log_action_end_msg $?

    log_action_begin_msg "Starting service"
    service $PROJECTNAME start
    log_action_end_msg $?
fi
