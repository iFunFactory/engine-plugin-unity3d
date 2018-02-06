#!/bin/sh
/home/test/plugin-build/debug/plugin-local -session_message_logging_level=2 &
sleep 1
/home/test/plugin-build/debug/plugin.encryption-local -session_message_logging_level=2
