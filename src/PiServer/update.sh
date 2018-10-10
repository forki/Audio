#!/bin/sh

. /lib/lsb/init-functions

DOTNETREQ=2.1.5
SCRIPTROOT=`dirname "$0"`
DEFAULTS=/etc/default/PiServer
SOURCEPATH=$SCRIPTROOT/bin

if [ ! -e $DEFAULTS -a $SCRIPTROOT/PiServer.defaults ]; then
    cp $SCRIPTROOT/PiServer.defaults $DEFAULTS
fi

if [ -e $DEFAULTS ]; then
    log_action_begin_msg "Loading PiServer settings"
    . /etc/default/PiServer
    log_action_end_msg $?

    if [ ! -e $DAEMON ]; then
        log_failure_msg "PiServer not installed! Update "
        exit 1
    fi

    if [ -e $SOURCEPATH/$NAME ]; then
        log_action_begin_msg "Stop service"
        service PiServer stop
        retval=$?
        log_action_end_msg $retval
        if [ $retval -ne 0 ]; then
            exit 2
        fi

        log_action_begin_msg "Updating"
        log_action_cont_msg "Init script"
        cp $SCRIPTROOT/PiServer /etc/init.d/
        chmod +x /etc/init.d/PiServer
        log_action_cont_msg "Reloading system.d"
        systemctl daemon-reload
        log_action_end_msg $?

        log_action_begin_msg "Updating $DAEMONHOME"
        rm -R $DAEMONHOME && cp -r $SOURCEPATH/. $DAEMONHOME
        chmod +x $DAEMONHOME/$NAME
        log_action_end_msg $?

        if [ -e $DAEMON ]; then
            log_action_begin_msg "Starting service"
            service PiServer start
            log_action_end_msg $?
        fi
    else
        log_failure_msg "Update package corrupt?"
    fi
else
    log_failure_msg "Unable to load defaults"
fi

