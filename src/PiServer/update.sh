#!/bin/sh

. /lib/lsb/init-functions

DOTNETREQ=2.1.5
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

        log_action_begin_msg "Updating"
        log_action_cont_msg "Init script"
        cp $SCRIPTROOT/$PROJECTNAME /etc/init.d/

        chmod +x /etc/init.d/$PROJECTNAME
        log_action_cont_msg "Reloading system.d"
        systemctl daemon-reload
        log_action_end_msg $?

        log_action_begin_msg "Updating $DAEMONHOME"
        rm -R $DAEMONHOME && cp -r $SOURCEPATH/. $DAEMONHOME

        (cd $DAEMONHOME && npm install)
        chmod +x $DAEMONHOME/$NAME
        log_action_end_msg $?

        if [ -e $DAEMON ]; then
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

