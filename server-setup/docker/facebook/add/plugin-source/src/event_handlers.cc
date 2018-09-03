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

void SendFacebookAuthenticationMessage(const Ptr<Session> &session,
    string result, EncodingScheme encoding)
{
  if (encoding == kJsonEncoding) {
    Json message;
    message["result"] = result;
    session->SendMessage("fb_authentication", message);
  } else {
    Ptr<FunMessage> response(new FunMessage);
    PbufAnotherMessage *login_result = response->MutableExtension(pbuf_another);
    login_result->set_msg(result);
    session->SendMessage("fb_authentication", response);
  }
}


void OnAuthenticated(
    const string &fb_uid,
    const FacebookAuthenticationRequest &request,
    const FacebookAuthenticationResponse &response,
    bool error,
    const Ptr<Session> &session,
    EncodingScheme encoding) {

  if (error) {
    // system error
    LOG(ERROR) << "authentication system error";
    SendFacebookAuthenticationMessage(session, "system error", encoding);
    return;
  }

  if (not response.success) {
    LOG(INFO) << "wrong access token";
    SendFacebookAuthenticationMessage(session, "wrong access token", encoding);
    return;
  }

  if (fb_uid != response.client_id) {
    LOG(INFO) << "authentication fail";
    SendFacebookAuthenticationMessage(session, "authentication fail", encoding);
    return;
  }

  // login success
  LOG(INFO) << "login success";
  SendFacebookAuthenticationMessage(session, "ok", encoding);
}


void OnAccountLogin(const Ptr<Session> &session, const Json &message) {
  // Thank to a JSON schema we specified registering a handler,
  // we are guaranteed that the passsed "message" is in the following form.
  //
  //   {"facebook_access_token":"xxx"}
  //
  // So no more validation is necessary.
  string fb_uid = message["facebook_uid"].GetString();
  string fb_access_token = message["facebook_access_token"].GetString();

  LOG(INFO) << "attempt facebook login - id : " << fb_uid;

  if (fb_uid.empty()) {
    LOG(INFO) << "facebook id is empty";
    SendFacebookAuthenticationMessage(session, "empty id", kJsonEncoding);
    return;
  }

  if (fb_access_token.empty()) {
    LOG(INFO) << "facebook token is empty";
    SendFacebookAuthenticationMessage(session, "empty token", kJsonEncoding);
    return;
  }

  // Below shows how to initiate Facebook authentication.
  FacebookAuthenticationRequest request(fb_access_token);

  Authenticate(request, bind(&OnAuthenticated,
               fb_uid, _1, _2, _3, session, kJsonEncoding));
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


void OnAccountLogin2(const Ptr<Session> &session,
    const Ptr<FunMessage> &message) {
  if (not message->HasExtension(facebook_login)) {
    LOG(ERROR) << "Invalid message";
    SendFacebookAuthenticationMessage(
        session, "invalid message", kProtobufEncoding);
    return;
  }
  const FacebookLoginMessage &login_message =
      message->GetExtension(facebook_login);
  const string &fb_uid = login_message.facebook_uid();
  const string &fb_access_token = login_message.facebook_access_token();

  LOG(INFO) << "attempt facebook login - id : " << fb_uid;

  if (fb_uid.empty()) {
    LOG(INFO) << "facebook id is empty";
    SendFacebookAuthenticationMessage(session, "empty id", kProtobufEncoding);
    return;
  }

  if (fb_access_token.empty()) {
    LOG(INFO) << "facebook token is empty";
    SendFacebookAuthenticationMessage(
        session, "empty token", kProtobufEncoding);
    return;
  }

  // Below shows how to initiate Facebook authentication.
  FacebookAuthenticationRequest request(fb_access_token);

  Authenticate(request, bind(&OnAuthenticated,
               fb_uid, _1, _2, _3, session, kProtobufEncoding));
}


void OnPbufEchoMessage(const Ptr<Session> &session,
    const Ptr<FunMessage> &message) {
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


void OnPbufEchoMessage2(const Ptr<Session> &session,
    const Ptr<FunMessage> &message) {

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
    JsonSchema login_msg(JsonSchema::kObject,
        JsonSchema("facebook_uid", JsonSchema::kString, true),
        JsonSchema("facebook_access_token", JsonSchema::kString, true));

    HandlerRegistry::Register("login", OnAccountLogin, login_msg);

    HandlerRegistry::Register2("facebook_login", OnAccountLogin2);
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
