// Copyright (C) 2012 Nexon Korea Corporation All Rights Reserved.
//
// This work is confidential and proprietary to Nexon Korea Corporation and
// must not be used, disclosed, copied, or distributed without the prior
// consent of Nexon Korea Corporation.

#ifndef SRC_UNITY_SERVER_TYPES_H_
#define SRC_UNITY_SERVER_TYPES_H_

#include <funapi/common/types.h>
#include "unity_server.h"


namespace google { namespace protobuf {} }


namespace unity_server {

using fun::string;
using fun::shared_ptr;
using fun::Uuid;
using fun::UnityServer;
using fun::UnityServerPtr;
using fun::UnityServerPtrVector;
using fun::UnityServerPtrMap;

namespace protobuf = google::protobuf;
}  // namespace unity_server

#endif  // SRC_UNITY_SERVER_TYPES_H_
