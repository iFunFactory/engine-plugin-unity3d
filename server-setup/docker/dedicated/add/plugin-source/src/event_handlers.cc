// PLEASE ADD YOUR EVENT HANDLER DECLARATIONS HERE.

#include "event_handlers.h"

#include <vector>

#include <funapi.h>
#include <funapi/service/account_manager.h>
#include <funapi/service/dedicated_server_manager.h>
#include <funapi/utility/random_generator.h>
#include <funapi/management/api_service.h>

#include <glog/logging.h>
#include <boost/unordered_set.hpp>

#include "plugin_loggers.h"
#include "plugin_messages.pb.h"


// You can differentiate game server flavors.
DECLARE_string(app_flavor);


namespace plugin {

using std::vector;
using std::string;

////////////////////////////////////////////////////////////////////////////////
// Session open/close handlers
////////////////////////////////////////////////////////////////////////////////


boost::mutex the_lock;
boost::unordered_set<std::string> the_users;


static void OnSessionOpened(const Ptr<Session> &session) {
  logger::SessionOpened(to_string(session->id()), WallClock::Now());
}


static void OnSessionClosed(const Ptr<Session> &session, SessionCloseReason reason) {
  logger::SessionClosed(to_string(session->id()), WallClock::Now());

  std::string name = AccountManager::FindLocalAccount(session);
  if (not name.empty()) {
    boost::mutex::scoped_lock lock {the_lock};
    auto ui = the_users.find(name);
    if (ui != the_users.cend()) {
      the_users.erase(ui);
    }
    AccountManager::SetLoggedOut(name);
  }
}


static void OnMatchResultReceived(const fun::Uuid &match_id,
                                  const fun::Json &data,
                                  bool success) {
  if (not success) {
    LOG(ERROR) << "Match(" << match_id << "): finished abnormally";
    return;
  }

  LOG(INFO) << "Match(" << match_id << "): finished with result: "
      << data.ToString(true);

}

fun::Uuid the_match_id;


static void OnLogin(const Ptr<Session> &session, const Json &msg) {
  /*
   {"name": "blahblah"}
   */

  if (not msg.HasAttribute("name") or not msg["name"].IsString()) {
    LOG(ERROR) << "OnLogin: name is not set";
    return;
  }

  const std::string name = msg["name"].GetString();
  if (not AccountManager::CheckAndSetLoggedIn(name, session)) {
    LOG(ERROR) << "OnLogin: " << name << ": cannot login";
    return;
  }

  LOG(INFO) << "User(" << name << "): logged in";

  if (the_match_id.is_nil()) {
    fun::Json data;
    data.SetObject();
    data["foo"] = "bar";

    fun::Json x;
    x.SetObject();
    x.AddAttribute("x", "y");

    std::vector<string> users {name,};

    LOG(INFO) << "DedicatedServerManager::SendUser(" << users[0];
    std::vector<string> args;
    the_match_id = fun::RandomGenerator::GenerateUuid();
    DedicatedServerManager::Spawn(
        the_match_id,
        data,
        args,
        users,
        std::vector<fun::Json>({x,}),
        [] (const fun::Uuid &match_id, const vector<string> &users, bool success) {
          DLOG(INFO) << "DediServer callback: Match(" << match_id << "): " << success;
        });
  } else {
    fun::Json x;
    x.SetObject();
    x.AddAttribute("x", "y");
    DedicatedServerManager::SendUsers(
        the_match_id,
        fun::Json(),
        std::vector<string>({name,}),
        std::vector<fun::Json>({x,}),
        [] (const fun::Uuid &match_id, const vector<string> &users, bool success) {
          DLOG(INFO) << "DediServer callback 2: Match(" << match_id << "): " << success;
        });
  }
}


void RegisterEventHandlers() {
  HandlerRegistry::Install2(OnSessionOpened, OnSessionClosed);

  HandlerRegistry::Register("login", OnLogin);

  DedicatedServerManager::RegisterMatchResultCallback(OnMatchResultReceived);
}

}  // namespace plugin
