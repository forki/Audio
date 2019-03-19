#!/bin/sh

. /lib/lsb/init-functions

SCRIPTROOT=`dirname "$0"`
PROJECTNAME=PiServer
DEFAULTS=/etc/default/$PROJECTNAME
SOURCEPATH=$SCRIPTROOT/bin

if [ ! -e $DEFAULTS -a $SCRIPTROOT/$PROJECTNAME.defaults ]; then
    cp $SCRIPTROOT/$PROJECTNAME.defaults $DEFAULTS
fi

if [ -e $DEFAULTS ]; then
    log_action_begin_msg "Loading $PROJECTNAME settings"
    . /etc/default/$PROJECTNAME
    log_action_end_msg $?

    if [ ! -e $DAEMON ]; then
        log_failure_msg "$PROJECTNAME not installed! Update "
        exit 1
    fi

    if [ -e $SOURCEPATH/$NAME ]; then
        log_action_begin_msg "Stop service"
        service $PROJECTNAME stop
        retval=$?
        log_action_end_msg $retval
        if [ $retval -ne 0 ]; then
            exit 2
        fi

        log_action_begin_msg "Download dotnet core "
        curl -sSL -o dotnet.tar.gz https://download.visualstudio.microsoft.com/download/pr/b12c61f5-7ba4-47f1-93f0-d2280fa4bf3c/8e1ae5ac780c61e0339d0247e7d9a8d8/dotnet-runtime-2.2.3-linux-arm.tar.gz
        sudo mkdir -p /opt/dotnet && sudo tar zxf dotnet.tar.gz -C /opt/dotnet
        rm dotnet.tar.gz
        sudo ln -s /opt/dotnet/dotnet /usr/local/bin
        log_action_end_msg $?

        log_action_begin_msg "Updating packages "
        log_action_cont_msg " apt update "
        apt-get -qq update
        log_action_cont_msg " apt upgrade "
        apt-get -qq upgrade
        log_action_cont_msg " apt install "
        apt-get -qq install  curl libunwind8 gettext apt-transport-https omxplayer npm unzip
        log_action_end_msg $?

        log_action_begin_msg "Updating"
        log_action_cont_msg " Init script "
        cp $SCRIPTROOT/$PROJECTNAME /etc/init.d/

        chmod +x /etc/init.d/$PROJECTNAME
        log_action_cont_msg "Reloading system.d"
        systemctl daemon-reload
        log_action_end_msg $?

        log_action_begin_msg "Updating $DAEMONHOME "
        log_action_cont_msg " Backup node_modules "
        cp -a $DAEMONHOME/node_modules $SOURCEPATH/

        log_action_cont_msg " installing new version to $DAEMONHOME "
        rm -R $DAEMONHOME && cp -a $SOURCEPATH/. $DAEMONHOME
        log_action_end_msg $?

        log_action_begin_msg "Updating node_modules"
        cd $DAEMONHOME && npm --silent install > /dev/null 2>&1
        log_action_end_msg $?

        if [ -e $DAEMON ]; then
            chmod +x $DAEMONHOME/$NAME

            log_action_begin_msg "Starting service"
            service $PROJECTNAME start
            log_action_end_msg $?
        fi
    else
        log_failure_msg "Update package corrupt?"
    fi
else
    log_failure_msg "Unable to load defaults"
fi
