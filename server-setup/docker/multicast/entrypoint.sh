#!/bin/sh
service zookeeper start
sleep 1
/home/test-server/plugin-multicast/build/plugin-local -session_message_logging_level=2
