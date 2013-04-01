#!/bin/bash
# Copyright (C) 2012 Nexon Korea Corporation All Rights Reserved.
#
# This work is confidential and proprietary to Nexon Korea Corporation and
# must not be used, disclosed, copied, or distributed without the prior
# consent of Nexon Korea Corporation.

# Bootstrap configure system from .ac/.am files
autoreconf -Wno-portability --install -I `pwd`/m4 --force

