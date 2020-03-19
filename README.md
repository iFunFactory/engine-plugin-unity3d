Funapi plugin unity
========================

이 플러그인은 iFun Engine 게임 서버를 사용하는 Unity3d 사용자를 위한 클라이언트 플러그인입니다.

## 기능

* TCP, UDP, HTTP, Websocket 프로토콜 사용 가능
* JSON, Protobuf-net 형식의 메시지 타입 지원
* ChaCha20, AES-128을 포함한 4종류의 암호화 타입 지원
* 멀티캐스트, 채팅, 게임내 리소스 다운로드 등 다양한 기능 지원
* 페이스북, 트위터의 로그인, 친구 정보 요청 등의 기능 지원

## 유니티 3D 지원 버전에 대해서

Funapi Plugin 은 다음 버전의 유니티 3D 를 지원합니다.

| 유니티 3D 버전 | Windows | macOS | Android | iOS |
|:-------------:|:-------:|:-----:|:-------:|:---:|
| 5             | o       | o     | o       | o   |
| 2017          | o       | o     | o       | o   |
| 2018          | o       | o     | o       | o   |
| 2019          | o       | o     | o       | o   |

## 서버

* 서버 버전 3686 이상은 플러그인 버전 253 이상이 필요합니다.
* FunMulticastMessage를 확장하여 사용하는 경우 **필드 번호를 16부터 사용** 해야 합니다.


## 사용방법

### 다운로드
**git clone** 으로 다운 받거나 **zip 파일** 을 다운 받아 압축을 풀면 아래와 같은 폴더 목록을
확인할 수 있습니다.

```text
|- additional-plugins         # 페이스북, 트위터 플러그인
|- csharp-samples             # C# Runtime 샘플 코드
|- funapi-plugin-unity        # 유니티 Funapi 플러그인
|- dedicated-server-plugin    # 데디케이티드 서버 RPC 플러그인
```

클라이언트 플러그인 코드는 ``funapi-plugin-unity`` 폴더에 있습니다.
``dedicated-server-plugin`` 폴더에는 데디케이티드 서버와 게임 서버 간의 통신을 할 수 있게 해주는
RPC 플러그인이 있습니다.
``additional-plugins`` 에는 페이스북과 트위터용 플러그인이 있으며
``csharp-samples`` 폴더에는 MacOS/Linux 와 Windows 용 C# Runtime 샘플 코드가 있습니다.

### 테스트 프로젝트
유니티를 실행해서 ``funapi-plugin-unity`` 폴더의 프로젝트를 열면 프로젝트 폴더 중에 ``Sample``
폴더가 있습니다. 여기에 테스트용 Scene과 샘플 코드들이 있습니다.

**Test** Scene을 열면 테스트 페이지가 나옵니다. 서버 주소가 로컬로 되어 있으니 서버가 로컬에 있지
않다면 **Server** 항목을 수정해 주세요.

서버를 띄우고 실행을 하면 여러가지 기능들을 테스트해 볼 수 있습니다.
서버를 설치하고 아무것도 변경하지 않았다면 기본적으로 TCP, HTTP의 JSON 포트만 열려 있습니다.
다른 프로토콜과 메시지 타입을 사용하려면 서버와 클라이언트의 프로토콜과 포트 번호를 동일하게 변경하고
테스트하면 됩니다.


### 내 프로젝트에 적용
다운받은 폴더 중에 ``funapi-plugin-unity/Assets`` 폴더로 들어가면 아래와 같은 목록이 나타납니다.

```text
|- Editor
|- Funapi
|- JsonDotNet
|- Plugins
|- Resources
|- Sample
|- FunMessageSerializer.dll
|- messages.dll
|- protobuf-net.dll
|- ...
```

위의 폴더 중 ``Funapi``, ``Plugins``, ``JsonDotNet`` 폴더를 플러그인을 사용할 프로젝트의
``Assets`` 폴더로 복사하면 됩니다.

암호화 기능이나 압축 기능을 사용하려면 ``Resources`` 폴더도 함께 복사해 주세요.
**HTTPS** 를 사용하려면 ``Resources`` 폴더와 함께 ``Editor`` 폴더도 복사해 주세요.

웹소켓을 사용하고 싶다면 ``websocket-sharp.dll`` 파일과 ``WebSocket.jslib`` 파일을 프로젝트의
``Assets`` 폴더로 복사해야 됩니다.

아래 세 개의 DLL은 Protobuf 메시지를 사용하기 위한 DLL인데 포함된 DLL들은 샘플용 메시지이므로
Protobuf를 사용할 경우 서버 프로젝트에서 메시지를 정의하고 만든 DLL을 사용해야 합니다. Protobuf
메시지를 사용하지 않더라도 플러그인 코드 내에 Protobuf를 사용하는 코드가 포함되어 있으므로 플러그인을
사용하는 프로젝트 내에 기본으로 제공되는 DLL도 들어 있어야 합니다.

자세한 사용방법은 ``Sample`` 폴더의 파일과 도움말을 참고해 주세요.


## 도움말

클라이언트 플러그인의 도움말은
<http://www.ifunfactory.com/engine/documents/reference/ko/client-plugin.html> 를
참고해 주세요.

플러그인에 대한 궁금한 점은 <http://answers.ifunfactory.com> 에 질문을 올려주세요.
가능한 빠르게 답변해 드립니다.

그 외에 플러그인에 대한 문의 사항이나 버그 신고는 <funapi-support@ifunfactory.com> 으로 메일을
보내주세요.


## 버전별 주요 이슈

아래 설명의 버전보다 낮은 버전의 플러그인을 사용하고 있다면 플러그인 업데이트시 아래 내용을 참고해 주세요.

### v312
기존에는 로그로 프로토콜 버퍼 메시지의 길이만 출력했지만 메시지의 내용도 함께 출력하도록 변경했습니다.
이 변경을 적용하기 위해서는 4269 이상 버전의 서버에서 다시 빌드한 protobuf dll 을 사용해야 합니다.

### v276
로그 심볼이 변경되었습니다. 기존 LOG_LEVEL 은 삭제되고 ENABLE_LOG, ENABLE_DEBUG 로 변경되었습니다.
ENABLE_LOG 는 기본적인 로그를 출력하고 ENABLE_DEBUG 는 디버깅에 도움이 되는 로그를 출력합니다.
ENABLE_DEBUG 는 ENABLE_LOG 가 선언되어 있어야 사용 가능합니다.

### v249
FunapiSession의 사용 방식 변경
- 기존에는 Session마다 MonoBehaviour 객체를 생성/삭제했었는데 플러그인 종료처리가 완료되기 전에
MonoBehaviour 객체가 삭제되어 이후 처리가 안되는 경우가 있어 플러그인용 MonoBehaviour 객체를
하나만 두고 공용으로 사용하는 방식으로 변경하였습니다.
- 사용이 끝난 FunapiSession 객체는 FunapiSession.Destroy() 함수를 호출해 명시적으로
객체를 해제해 주어야 더 이상 업데이트가 이루어지지 않습니다. 앱 전체에서 시작해서 끝날
때까지 FunapiSession 객체를 하나만 사용할 경우에는 굳이 호출하지 않아도 괜찮습니다.
OnApplicationQuit 이 호출되면 자동으로 해제됩니다.


AutoReconnect 기능 변경
- AutoReconnect 옵션은 기존에는 첫 연결시에만 연결 실패시 20초 동안 4-5회 가량 재접속을 시도하는
기능이었으나 효용성이 없어 해당 옵션이 true일 경우 연결이 종료된 것을 감지하면 항상 재연결을
시도하는 것으로 변경 되었습니다.
- 재접속을 시작할 때 Transport 이벤트인 kReconnecting 이벤트가 발생 하며 기존에 연결 종료시
발생하던 다양한 타입의 Transport 이벤트는 kStopped 하나로 통일 되었습니다.
- 재접속 시도를 멈추는 방법은 ConnectionTimeout으로 기다리는 시간을 지정하거나 kReconnecting
이벤트 발생 후 일정 시간을 기다린 후 직접 Stop() 을 호출하는 방법이 있습니다. 재접속 시도 후
ConnectionTimeout으로 지정한 시간을 초과했을 경우에는 재접속 시도가 중단되고 kStopped 이벤트가
발생합니다.

### v247
AutoReconnect 옵션은 기존에는 첫 연결시에만 연결 실패시 재접속을 시도하는 기능이었으나 연결이 종료되면
항상 재시도를 하는 것으로 변경되었습니다. ConnectionTimeout으로 재시도 허용 시간을 지정할 수 있습니다.

Session 관련 MonoBehaviour의 효율적인 사용을 위해 하나의 MonoBehaviour 객체를 여러 Session이
공유해서 사용하는 방식으로 변경되었습니다. 사용이 끝난 FunapiSession 객체는 FunapiSession.Destroy()
함수를 호출해 주어야 더 이상 업데이트가 이루어지지 않습니다.

### v226
하나의 머신에서 테스트를 위해 클라이언트를 여러 개 실행할 경우 UDP Transport 를 사용한다면
UDP 소켓의 로컬 포트가 겹치는 문제가 발생할 수 있습니다. (로컬 포트가 같을 경우 서버에서 하나의
클라이언트로 인식) 가장 좋은 해결책은 서버에서 연결 종료 후 세션을 명시적으로 Close 하는 것입니다.
(기본적으로 서버는 Close가 호출되지 않으면 세션 타임아웃이 발생할 때까지 세션을 닫지 않습니다)
서버에서 세션을 닫는 것이 어려울 경우 Define Symbols에 FIXED_UDP_LOCAL_PORT 를 추가하면
클라이언트에서 로컬 포트를 순차적으로 할당하게 할 수 있습니다. 이 방법은 테스트 용도로만 사용하는 것을
권장하며 기본적으로 하나의 머신에서 하나의 클라이언트만 실행된다면 발생하지 않는 문제입니다.

### v213
Protobuf의 Message Type을 Integer로 쓸 수 있는 기능이 추가되면서 기존의
SendMessage (MessageType, ...) 함수로 메시지를 보내면 메시지 타입이 Integer로 전송되게
변경되었습니다. MessageType을 사용하면서 기존의 String 타입으로 메시지를 보내고 싶다면 Unity의
Project Setting 에서 Scripting Define Symbols에 PROTOBUF_ENUM_STRING_LEGACY 를
추가해 주세요.

### v212
멀티캐스트 채널에 token을 지정할 수 있는 기능이 추가되면서 인터페이스가 중복되는 일이 발생해 부득이하게
FunapiChat.JoinChannel (string channel_id, string my_name, OnChatMessage handler)
함수가 삭제되었습니다. 기존에 해당 함수를 사용하던 분들은 아래와 같이 수정해서 사용해주시기 바랍니다.

```
chat.sender = "nickname";
chat.JoinChannel("channel id", handler);
```

### v164
JsonAccessor 클래스의 인터페이스가 추가되었습니다. 이전의 JsonAccessor 클래스를 상속받아
JsonHelper를 구현하셨다면 플러그인 업데이트시 추가된 인터페이스에 대해 구현해야 합니다.

### v158
Session 클래스인 FunapiNetwork를 대체할 새로운 클래스로 FunapiSession 클래스가 추가되었습니다.
이후로는 특별한 이슈가 없는 한 FunapiNetwork의 업데이트는 없을 예정이므로 가능하면 FunapiSession
클래스를 사용하시기 바랍니다.

### v152
HTTPs를 사용하기 위해서는 루트 인증서가 필요한데 Mono에는 기본 루트 인증서가 포함되어 있지 않습니다.
이를 해결하기 위해 Mozilla에서 제공하는 루트 인증서를 사용할 수 있도록 인증서 다운로드 기능이
추가되었습니다.

#### MozRoots 인증서 다운받기
*Assets/Editor/iFunPlugin.cs* 파일을 프로젝트에 포함하면 유니티 에디터의 메뉴에
[iFun Plugin] 항목이 추가됩니다. [iFun Plugin][Download MozRoots] 항목을 실행하면
``Assets/Resources/Funapi/`` 폴더에 인증서가 저장됩니다. (*MozRoots.bytes* 파일)

### v150
Json과 Protobuf의 인/디코딩 함수의 위치가 변경되었습니다.
기존에 JsonHelper는 FunapiTransport에 Protobuf 관련 함수는 FunapiNetwork에 있었으나
이제 FunapiMessage 클래스로 통합되었습니다.

### v141
플러그인에서 필요한 MonoBehaviour를 자체적으로 생성해서 사용하므로
FunapiNetwork와 FunapiDownloader의 Update나 Stop 함수를 호출할 필요가 없습니다.
