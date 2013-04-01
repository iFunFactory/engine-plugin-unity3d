// Copyright (C) 2012 Nexon Korea Corporation All Rights Reserved.
//
// This work is confidential and proprietary to Nexon Korea Corporation and
// must not be used, disclosed, copied, or distributed without the prior
// consent of Nexon Korea Corporation.

#include <boost/bind.hpp>
#include <funapi/account/account_event_handler_registry.h>
#include <funapi/account/object_creator_registry.h>
#include <funapi/api/tick/world_event_handler_registry.h>
#include <funapi/framework/installer.h>

#include <utility>

#include "event_handlers.h"


namespace {

class UnityServerServerInstaller : public fun::framework::Installer {
 public:
  virtual bool Install(
      const fun::framework::Installer::ArgumentMap &/*arguments*/) {
    fun::ObjectCreatorRegistry::Install(
        boost::bind(unity_server::CreateObject,
                    unity_server::kAccountObjectModelName),
        unity_server::DeserializeObject);

    fun::WorldEventHandlerRegistry::Install(
        unity_server::OnWorldReady,
        std::make_pair(unity_server::OnWorldTick,
                       unity_server::kWorldTickMicrosecond));

    fun::AccountEventHandlerRegistry::Install(
        unity_server::OnAccountLogin,
        unity_server::OnAccountLogout,
        unity_server::OnAccountTimeout,
        unity_server::OnAccountMessage);

    return true;
  }

  virtual bool Uninstall() {
    return true;
  }
};

}  // unnamed namespace


REGISTER_INSTALLER(UnityServerServer, UnityServerServerInstaller)
