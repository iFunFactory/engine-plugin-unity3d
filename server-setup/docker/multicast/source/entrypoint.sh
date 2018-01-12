#!/bin/sh
service zookeeper start
sleep 1
/home/test-server/plugin-multicast/build/plugin.json-local -session_message_logging_level=2 &
sleep 1
/home/test-server/plugin-multicast/build/plugin.protobuf-local -session_message_logging_level=2
