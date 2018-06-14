#!/bin/sh
/home/test/plugin-build/debug/plugin.zstd-local -session_message_logging_level=2 &
sleep 1
/home/test/plugin-build/debug/plugin.deflate-local -session_message_logging_level=2
