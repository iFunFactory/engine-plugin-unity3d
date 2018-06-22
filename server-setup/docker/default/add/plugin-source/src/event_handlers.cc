// PLEASE ADD YOUR EVENT HANDLER DECLARATIONS HERE.

#include "event_handlers.h"

#include <funapi.h>
#include <gflags/gflags.h>
#include <glog/logging.h>

#include "plugin_loggers.h"
#include "plugin_messages.pb.h"


// You can differentiate game server flavors.
DECLARE_string(app_flavor);


namespace plugin {

////////////////////////////////////////////////////////////////////////////////
// Session open/close handlers
////////////////////////////////////////////////////////////////////////////////

void OnSessionOpened(const Ptr<Session> &session) {
  logger::SessionOpened(to_string(session->id()), WallClock::Now());
}


void OnSessionClosed(const Ptr<Session> &session, SessionCloseReason reason) {
  logger::SessionClosed(to_string(session->id()), WallClock::Now());

  if (reason == kClosedForServerDid) {
    // Server has called session->Close().
  } else if (reason == kClosedForIdle) {
    // The session has been idle for long time.
  } else if (reason == kClosedForUnknownSessionId) {
    // The session was invalid.
  }
}



////////////////////////////////////////////////////////////////////////////////
// Client message handlers.
//
// (Just for your reference. Please replace with your own.)
////////////////////////////////////////////////////////////////////////////////
void OnAuthenticated(
    const FacebookAuthenticationRequest &request,
    const FacebookAuthenticationResponse &response,
    bool error,
    const Ptr<Session> &session) {
  if (error) {
    // system error
    LOG(ERROR) << "authentication system error";
    return;
  }

  if (response.success) {
    // login success
    LOG(INFO) << "login success";

    // You can have the Engine manage logged-in accounts through "AccountManager".
    AccountManager::CheckAndSetLoggedIn(response.client_id, session);

    // We also leave a player activity log.
    // This log can be used when you need to do customer services.
    // To customize an activity log, please refer to the reference manual.
    logger::PlayerLoggedIn(to_string(session->id()), response.client_id,
                           WallClock::Now());
  } else {
    // login failure
    LOG(INFO) << "login failure";
  }
}


void OnAccountLogin(const Ptr<Session> &session, const Json &message) {
  // Thank to a JSON schema we specified registering a handler,
  // we are guaranteed that the passsed "message" is in the following form.
  //
  //   {"facebook_access_token":"xxx"}
  //
  // So no more validation is necessary.
  string fb_access_token = message["facebook_access_token"].GetString();

  // Below shows how to initiate Facebook authentication.
  FacebookAuthenticationRequest request(fb_access_token);

  Authenticate(request, bind(&OnAuthenticated, _1, _2, _3, session));
}


void OnEchoMessage(const Ptr<Session> &session, const Json &message) {
  // Since we have not specified a JSON schema for "echo" below,
  // the passed "message" may have malformed data.
  // We should manually validate it before use.
  //
  // Assuming "echo" is in the form below:
  //
  //   {"message":"xxx"}

  static const char *kMessage = "message";

  if (not message.HasAttribute(kMessage)) {
    LOG(ERROR) << "Invalid message";
    return;
  }

  if (not message[kMessage].IsString()) {
    LOG(ERROR) << "Invalid message";
    return;
  }

  // OK. The message looks good.
  string message_from_client = message["message"].GetString();

  // We sends the content back to the client.
  Json response;
  response[kMessage] = message_from_client;
  session->SendMessage("echo", response);

  LOG(INFO) << "message: " << message_from_client;
}


void OnPbufEchoMessage(const Ptr<Session> &session, const Ptr<FunMessage> &message) {
  // This is a Google protocol buffer example.
  // Engine invokes the handler only when the "message" is legitimate.
  // So, we do not have to check "required" fields.
  // But it's your responsibility to make sure required "optional" fields exist.


  // Every client-server protobuf message must extend "FunMessage".
  // Please see plugin_messages.proto.
  //
  // In this example,
  //   message PbufEchoMessage {
  //     required string msg = 1;
  //   }
  //
  //   extend FunMessage {
  //     potional PbufEchoMessage pbuf_echo = 16;
  //   }
  if (not message->HasExtension(pbuf_echo)) {
    LOG(ERROR) << "Invalid message";
    return;
  }
  const PbufEchoMessage &pbuf_message = message->GetExtension(pbuf_echo);
  const string &msg = pbuf_message.msg();
  LOG(INFO) << "pbuf message: " << msg;

  // Constructs a response message.
  Ptr<FunMessage> response(new FunMessage);
  PbufEchoMessage *echo_response = response->MutableExtension(pbuf_echo);
  echo_response->set_msg(msg);

  // Wires the message.
  session->SendMessage("pbuf_echo", response);
}


void OnPbufEchoMessage2(const Ptr<Session> &session, const Ptr<FunMessage> &message) {

  if (not message->HasExtension(pbuf_echo)) {
    LOG(ERROR) << "Invalid message";
    return;
  }
  const PbufEchoMessage &pbuf_message = message->GetExtension(pbuf_echo);
  const string &msg = pbuf_message.msg();
  LOG(INFO) << "pbuf message: " << msg;

  // Constructs a response message.
  Ptr<FunMessage> response(new FunMessage);
  PbufEchoMessage *echo_response = response->MutableExtension(pbuf_echo);
  echo_response->set_msg(msg);

  // Wires the message.
  session->SendMessage(pbuf_echo, response);
}


////////////////////////////////////////////////////////////////////////////////
// Timer handler.
//
// (Just for your reference. Please replace with your own.)
////////////////////////////////////////////////////////////////////////////////

void OnTick(const Timer::Id &timer_id, const WallClock::Value &clock) {
  // PLACE HERE YOUR TICK HANDLER CODE.
}




////////////////////////////////////////////////////////////////////////////////
// Extend the function below with your handlers.
////////////////////////////////////////////////////////////////////////////////

void RegisterEventHandlers() {
  /*
   * Registers handlers for session close/open events.
   */
  {
    HandlerRegistry::Install2(OnSessionOpened, OnSessionClosed);
  }


  /*
   * Registers handlers for messages from the client.
   *
   * Handlers below are just for you reference.
   * Feel free to delete them and replace with your own.
   */
  {
    // 1. Registering a JSON message named "login" with its JSON schema.
    //    With json schema, Engine validates input messages in JSON.
    //    before entering a handler.
    //    You can specify a JSON schema like below, or you can also use
    //    auxiliary files in src/json_protocols directory.
    JsonSchema login_msg(JsonSchema::kObject,
        JsonSchema("facebook_uid", JsonSchema::kString, true),
        JsonSchema("facebook_access_token", JsonSchema::kString, true));

    HandlerRegistry::Register("login", OnAccountLogin, login_msg);

    // 2. Another JSON message example.
    //    In this time, we skipped a JSON schema.
    //    So no validation will be performed.
    HandlerRegistry::Register("echo", OnEchoMessage);


    // 3. Registering a Google Protobuf message handler.
    //    Protobuf itself provides a validation using the "required" keyword.
    HandlerRegistry::Register2("pbuf_echo", OnPbufEchoMessage);

    HandlerRegistry::Register2(pbuf_echo, OnPbufEchoMessage2);


    /////////////////////////////////////////////
    // PLACE YOUR CLIENT MESSAGE HANDLER HERE. //
    /////////////////////////////////////////////

  }


  /*
   * Registers a timer.
   *
   * Below demonstrates a repeating timer. One-shot timer is also available.
   * Please see the Timer class.
   */
  {
    Timer::ExpireRepeatedly(boost::posix_time::seconds(1), plugin::OnTick);
  }
}

}  // namespace plugin
