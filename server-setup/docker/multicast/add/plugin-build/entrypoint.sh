#!/bin/sh
service zookeeper start
sleep 1
/home/test/plugin-build/debug/plugin.json-local -session_message_logging_level=2 &
sleep 1
/home/test/plugin-build/debug/plugin.protobuf-local -session_message_logging_level=2
