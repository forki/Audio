#!/bin/sh

. /lib/lsb/init-functions

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
log_action_cont_msg " apt upgrade "
apt-get -qq upgrade
log_action_cont_msg " apt install "
sudo apt-get install -y curl libunwind8 gettext apt-transport-https omxplayer npm unzip
log_action_end_msg $?

log_action_begin_msg "Download dotnet core "
curl -sSL -o dotnet.tar.gz https://download.visualstudio.microsoft.com/download/pr/b12c61f5-7ba4-47f1-93f0-d2280fa4bf3c/8e1ae5ac780c61e0339d0247e7d9a8d8/dotnet-runtime-2.2.3-linux-arm.tar.gz
sudo mkdir -p /opt/dotnet && sudo tar zxf dotnet.tar.gz -C /opt/dotnet
rm dotnet.tar.gz
sudo ln -s /opt/dotnet/dotnet /usr/local/bin
log_action_end_msg $?

if [ -e  $SCRIPTROOT/$PROJECTNAME ]; then
    if [ ! -e $DEFAULTS -a $SCRIPTROOT/$PROJECTNAME.defaults ]; then
        echo DAEMONHOME=$DAEMONHOME > $DEFAULTS
        cat $SCRIPTROOT/$PROJECTNAME.defaults | grep -v "DAEMONHOME=" >> $DEFAULTS
    fi

    if [ ! -d $DAEMONHOME ]; then
        mkdir -p $DAEMONHOME
    fi

    log_action_begin_msg "Install application into $DAEMONHOME "
    cp -a $SOURCEPATH/. $DAEMONHOME
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
