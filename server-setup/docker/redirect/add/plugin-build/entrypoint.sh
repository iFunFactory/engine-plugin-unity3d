#!/bin/sh
service zookeeper start
sleep 1
/home/test/plugin-build/debug/plugin.alpha-local -session_message_logging_level=2 &
sleep 1
/home/test/plugin-build/debug/plugin.beta-local -session_message_logging_level=2
