// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

#define DEBUG

using System;
using System.Diagnostics;

namespace Fun
{
    // Utility class
    public class DebugUtils
    {
        [Conditional("DEBUG")]
        public static void Assert(bool condition)
        {
            if (!condition)
            {
                throw new Exception();
            }
        }
    }
}
