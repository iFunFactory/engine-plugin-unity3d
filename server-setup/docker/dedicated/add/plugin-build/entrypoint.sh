#!/bin/sh
service redis-server start
sleep 1
service zookeeper start
sleep 1
/home/test/plugin-build/debug/plugin-local -session_message_logging_level=2
