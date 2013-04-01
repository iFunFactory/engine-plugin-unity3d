// Copyright (C) 2012 Nexon Korea Corporation All Rights Reserved.
//
// This work is confidential and proprietary to Nexon Korea Corporation and
// must not be used, disclosed, copied, or distributed without the prior
// consent of Nexon Korea Corporation.

#ifndef SRC_EVENT_HANDLERS_H_
#define SRC_EVENT_HANDLERS_H_

#include <funapi/account/account_event_handler_registry.h>
#include <funapi/account/account.h>
#include <funapi/account/object_creator_registry.h>
#include <funapi/common/types.h>
#include <funapi/object/object.h>

#include "app_messages.pb.h"
#include "unity_server_client_messages.pb.h"
#include "unity_server_server_messages.pb.h"
#include "unity_server_types.h"


namespace unity_server {

///////////////////////////////////////////////////////////
// object creators.

// world object 용 model 의 이름.
extern const char *kWorldObjectModelName;

// account object 용 model 의 이름.
extern const char *kAccountObjectModelName;

fun::Object::Ptr CreateObject(const string &model);
fun::Object::Ptr DeserializeObject(const string &serial);


///////////////////////////////////////////////////////////
// world event handlers.

// server 의 timer event 발생 주기.
extern const int64_t kWorldTickMicrosecond;

// server 가 최초 뜰 때만 불린다.
void OnWorldReady(int64_t now_microsec);

// server 의 timer 를 통해 지정된 주기(kWorldTickMicrosecond)로 불린다.
void OnWorldTick(int64_t now_microsec);


///////////////////////////////////////////////////////////
// account event handlers.

void OnAccountLogin(const fun::Account::Ptr &account);
void OnAccountLogout(const fun::Account::Ptr &account);
void OnAccountTimeout(const fun::Account::Ptr &account);
void OnAccountMessage(const fun::Account::Ptr &account,
                      const ::ClientAppMessage &msg);


///////////////////////////////////////////////////////////
// app contents.

// room 용 채널의 이름과 서브 아이디.
extern const char *kRoomChannelName;
extern const char *kRoomChannelSubId;

// the one and only world object.
extern UnityServerPtr the_world;

// players methods.
void InsertPlayer(const UnityServerPtr &player);
void ErasePlayer(const string &player_name);

// account message handlers.
void OnPlayerPosition(const UnityServerPtr &player,
                      const ::PlayerPosition &msg);

}  // namespace unity_server

#endif  // SRC_EVENT_HANDLERS_H_
