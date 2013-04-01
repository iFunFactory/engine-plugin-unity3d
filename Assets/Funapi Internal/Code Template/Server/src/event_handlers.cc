// Copyright (C) 2012 Nexon Korea Corporation All Rights Reserved.
//
// This work is confidential and proprietary to Nexon Korea Corporation and
// must not be used, disclosed, copied, or distributed without the prior
// consent of Nexon Korea Corporation.

#include "event_handlers.h"

#include <boost/foreach.hpp>
#include <boost/lexical_cast.hpp>
#include <boost/uuid/uuid_io.hpp>
#include <funapi/account/account.h>
#include <funapi/account/multicaster.h>
#include <funapi/api/clock.h>
#include <funapi/common/boost_util.h>
#include <funapi/common/serialization/bson_archive.h>
#include <funapi/object/object.h>
#include <glog/logging.h>

#include <algorithm>

#include "unity_server_types.h"
#include "unity_server.h"


namespace unity_server {

const char *kWorldObjectModelName = "UnityServer";
const char *kAccountObjectModelName = "Player";


fun::Object::Ptr CreateObject(const string &model) {
  return unity_server::UnityServer::CreateNew(model);
}


fun::Object::Ptr DeserializeObject(const string &serial) {
  fun::BsonArchive::Ptr archive_ptr =
      fun::BsonArchive::CreateFromSerialized(serial);
  return unity_server::UnityServer::CreateFromSerialized(*archive_ptr);
}


const int64_t kWorldTickMicrosecond = 1000000;  // 1 second.


void OnWorldReady(int64_t /*now_microsec*/) {
  the_world = UnityServer::CreateNew(kWorldObjectModelName);
  the_world->EnterChannel(kRoomChannelName, kRoomChannelSubId);
}


void OnWorldTick(int64_t /*now_microsec*/) {
}


void OnAccountLogin(const fun::Account::Ptr &account) {
  UnityServerPtr player = UnityServer::Cast(account->object());
  const string &player_name = account->account_id().local_account();
  player->set_name(player_name);
  fun::Multicaster::Get().EnterChannel(kRoomChannelName, kRoomChannelSubId,
                                       account);

  InsertPlayer(player);
  LOG(INFO) << "account login[" << account->account_id()
            << "] player name[" << player_name
            << "]";
}


void OnAccountLogout(const fun::Account::Ptr &account) {
  UnityServerPtr player = UnityServer::Cast(account->object());
  const string &player_name = player->name();
  ErasePlayer(player_name);
  fun::Multicaster::Get().LeaveChannel(kRoomChannelName, kRoomChannelSubId,
                                       account);

  LOG(INFO) << "account logout[" << account->account_id()
            << "] player name[" << player_name
            << "]";
}


void OnAccountTimeout(const fun::Account::Ptr &account) {
  OnAccountLogout(account);
}


void OnAccountMessage(const fun::Account::Ptr &account,
                      const ::ClientAppMessage &msg) {
  UnityServerPtr player = UnityServer::Cast(account->object());

  ::ClientAppMessageType::Type msg_type = msg.GetExtension(client_message_type);
  switch (msg_type) {
    case ::ClientAppMessageType::kPlayerPosition: {
      OnPlayerPosition(player, msg.GetExtension(player_position));
      break;
    }
    default: {
      LOG(ERROR) << "Unknown client message type: " << (int64_t) msg_type;
      break;
    }
  }
}


const char *kRoomChannelName = "room";
const char *kRoomChannelSubId = "1";


UnityServerPtr the_world;


void InsertPlayer(const UnityServerPtr &player) {
  UnityServerPtrMap players = the_world->players();
  players.Insert(player->name(), player);
  the_world->set_players(players);
}


void ErasePlayer(const string &player_name) {
  UnityServerPtrMap players = the_world->players();
  players.erase(player_name);
  the_world->set_players(players);
}


void OnPlayerPosition(const UnityServerPtr &player,
                      const ::PlayerPosition &msg) {
  std::string pos_x = boost::lexical_cast<std::string>(msg.pos_x());
  player->set_pos_x(pos_x);
  std::string pos_y = boost::lexical_cast<std::string>(msg.pos_y());
  player->set_pos_y(pos_y);
  std::string pos_z = boost::lexical_cast<std::string>(msg.pos_z());
  player->set_pos_z(pos_z);
}

}  // namespace unity_server
