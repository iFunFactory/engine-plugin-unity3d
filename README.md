Funapi plugin
========================

플러그인 버전 v164 이상을 사용하려면 서버 버전이 v1695 이상이어야 합니다.

## Documentation

[클라이언트 플러그인 문서](http://www.ifunfactory.com/engine/documents/reference/ko/client-plugin.html)

## Release Note

Funapi plugin의 업데이트 내용입니다.

```
플러그인 버전 141부터는 플러그인에서 필요한 MonoBehaviour를 자체적으로 생성해서 사용하므로
FunapiNetwork와 FunapiDownloader의 Update나 Stop 함수를 호출할 필요가 없습니다.
```
```
플러그인 버전 150 이상부터 Json과 Protobuf의 인/디코딩 함수의 위치가 변경되었습니다.
기존에 JsonHelper는 FunapiTransport에 Protobuf 관련 함수는 FunapiNetwork에 있었으나
이제 FunapiMessage 클래스를 통해 해당 기능을 사용할 수 있습니다. 사용방법은 샘플 코드를 참고해주세요.
```

### 10/05/2016 (ver.178)
- unitypackage 파일 생성 메뉴 추가

### 09/22/2016 (ver.175)
- Redirect 관련 protobuf 메시지 비활성화

### 09/21/2016 (ver.174)
- C# Runtime 테스트 관련 수정
- Json 메시지를 보낼 때 body가 null일 경우 예외 처리 추가

### 09/19/2016 (ver.173)
- FunapiNetwork 사용하는 곳 모두 FunapiSession으로 대체
  - FunapiMulticastClient, FunapiChatClient, DebugLog 예제

### 09/13/2016 (ver.172)
- Aes128 / ChaCha20 암호화 기능 추가

### 09/08/2016 (ver.170)
- FunapiSession의 SendMessage에 EncryptionType 파라미터 추가

### 09/07/2016 (ver.169)
- ENABLE_DEBUG 가 활성화 되면 로그에 object id 표시

### 09/01/2016 (ver.168)
- Session Close 할 때 Message Queue 비움
- Session Id 변경시 Id 교체하고 콜백 호출만 하도록 변경

### 08/30/2016 (ver.165)
- FunapiSession에 Event Log 추가
- 핑 메시지에 이전 Session id가 전송되는 버그 수정

### 08/26/2016 (ver.164)
```
플러그인 버전 164 이상을 사용하려면 서버 버전 1695 이상이 필요하며 protobuf dll을 새로 빌드해야 합니다.
```
- FunapiSession에 Redirect 기능 추가
- FunapiNetwork에 Message Handler 삭제 함수 추가
- Json 메시지는 모두 JsonHelper를 사용하도록 수정
```
이 업데이트로 JsonAccessor 클래스의 인터페이스에 변경이 있습니다.
이전의 JsonAccessor 클래스를 상속받아 JsonHelper를 구현하셨다면 플러그인 업데이트시 추가된
인터페이스에 대해 구현을 해주셔야 합니다. 가능하면 인터페이스에 대한 변경 없이 업데이트를 진행하고 있지만
플러그인 내부에서 처리하는 메시지에서 JsonHelper를 사용해야 해서 불가피하게 인터페이스의 변경이 있었습니다.
```

### 08/12/2016 (ver.162)
- 핑 메시지에 Session Id가 빈 값으로 전송되는 버그 수정

### 08/10/2016 (ver.161)
- TcpTransportOption에 핑 로그 표시 옵션 추가, SetPing 함수 추가
- FunapiMessage의 Reply 관련 멤버를 FunapiNetwork로 이동
- 재접속시 미전송 메시지 보낼 때 Reply 설정 누락되는 버그 수정

### 08/04/2016 (ver.159)
- 코드 정리 및 불필요한 private 키워드 삭제
- private, protected 멤버 이름 소문자로 시작하도록 수정
- Encryptor.encryption 이름을 Encryptor.type으로 변경
- http content type 변경 (x-www-form-urlencoded -> octet-stream)
- C# Runtime 테스트 코드 버그 수정

### 07/29/2016 (ver.158)
- FunapiSession 클래스 및 예제 추가
- C# Runtime 테스트 코드 FunapiSession으로 변경

### 07/22/2016 (ver.156)
- 예제 UI 폰트 변경 (DroidSansMono -> Arial)
- Failure 콜백이 호출되기 전에 Stop이 먼저 처리되도록 수정

### 07/08/2016 (ver.155)
- 재연결시 메시지 전송 관련 버그 수정
- UnityVS 플러그인 관련 파일을 무시하도록 설정

### 07/08/2016 (ver.153)
- HTTP Transport에서 Receive buffer를 재사용하도록 수정
- Session closed 메시지가 전달되지 않는 버그 수정

### 06/28/2016 (ver.152)
- event 콜백 등록시 new 삭제
- Http Request 종료시 Response object 도 Close 호출
- FunapiDownloader 관련 수정 사항
  - 파일 다운로드 시간 체크에 Stopwatch 클래스 사용
  - 일시정지 상태일 경우 콜백 함수 호출시 PAUSED 상태 전달
  - 일시정지시 다운받던 임시파일 삭제
- HTTPs MozRoots 인증서 다운로드 기능 추가
```
HTTPs를 사용하기 위해서는 루트 인증서가 필요한데 Mono에는 기본 루트 인증서가 포함되어 있지 않습니다.
이를 해결하기 위해 Mozilla에서 제공하는 루트 인증서를 사용할 수 있도록 인증서 다운로드 기능이 추가되었습니다.
Assets/Editor/iFunPlugin.cs 파일을 프로젝트에 포함하면 유니티 에디터의 메뉴에 [iFun Plugin]이 추가됩니다.
[iFun Plugin][Download MozRoots] 항목을 실행하면 Assets/Resources/Funapi/ 폴더에 인증서가 저장됩니다.
HTTPs를 사용하실 경우 인증서 파일(MozRoots.bytes)을 프로젝트의 동일한 위치에 포함시켜 주시면 됩니다.
```

### 06/07/2016 (ver.151)
- 주석 설명 수정

### 06/01/2016 (ver.150)
- Ping 응답 속도 개선

### 05/24/2016 (ver.149)
- FunapiNetwork.Update() Obsolete 함수 삭제
- 샘플 코드 UI를 GUI에서 UnityEngine UI로 변경
- C# 런타임에서 MozRoots 관련 컴파일 오류 수정

### 05/17/2016 (ver.148)
- TCP, UDP 에서 IPv4와 IPv6 구분해서 처리하도록 수정
- ENABLE_DEBUG 가 활성화되면 ENABLE_LOG 도 활성화
- 로그를 문자열, 파일로 저장하는 기능 추가
- C# 런타임 로그에도 시간 표시 추가
- 샘플코드 수정

### 05/11/2016 (ver.147)
- HTTPs 인증서 검증 기능 추가
- Http transport의 address를 hostname을 그대로 사용하도록 수정
- DebugUtils -> FunDebug 이름 변경
- 유니티 에디터에서 실행할 때만 로그가 표시되도록 변경
- 플랫폼별 데이터 파일 저장 경로 정리 및 수정 (안드로이드 환경은 변경사항 없음)

### 05/03/2016 (ver.146)
```
플러그인 버전 146 이상을 사용하려면 서버 버전 1551 이상이 필요하며 protobuf dll을 새로 빌드해야 합니다.
```
- 멀티캐스트 채널 목록을 요청하는 함수 추가
- 멀티캐스트 서버에 의해 강제로 채널에서 퇴장되었을 때 처리 추가

### 04/27/2016 (ver.145)
- Tester Scene 수정, 불필요한 파일 삭제
- Delta time limit 값 추가, 로그 정리
- Reconnect 실패시 콜백 함수를 ConnectFailedCallback 함수로 변경

### 04/05/2016 (ver.144)
- TCP, HTTP 메시지에 sequence number 옵션 추가 (FunapiNetwork.SequenceNumberValidation)
- protobuf.net 으로 enum 타입의 메시지를 보낼 때 int32 타입으로 변환해서 전송
- 테스트 항목을 구분하여 테스트 Scene 과 코드 분리
- FunapiConfig 관련 코드 및 파일 삭제
- 멀티캐스트 에러코드 처리 및 LeaveAllChannels 함수 추가

### 03/10/2016 (ver.143)
- Ping 이벤트가 중복해서 등록되는 버그 수정
- 서버 메시지에 대한 ack를 이미 보낸 경우 ack를 다시 보내지 않도록 수정
- FunapiDownloader의 Callback 함수가 Main Update 시점에 호출되도록 수정
- Http Transport 에 Request timeout 시간을 설정할 수 있는 인터페이스 추가
- Http Transport 의 CancelRequest 함수를 public 으로 변경.
  - 현재 요청 중인 Request 를 취소 (**마지막으로 보낸 메시지가 취소되는 것이 아님에 주의!**)

### 02/29/2016 (ver.142)
- 재접속시 이전 메시지의 응답 메시지 처리에 관한 버그 수정
  - session reliability 옵션을 사용하는 tcp 연결에서만 발생

### 02/25/2016 (ver.141)
- 유니티 에디터 종료시 자동으로 Stop 호출
  - FunapiNetwork, FunapiDownloader 에 적용
- C# Runtime Unit Test Code
- ThreadSafeEventList 업데이트 버그 수정
- 로그 정리

### 02/15/2016 (ver.136)
- 매 프레임마다 업데이트가 필요한 객체들이 스스로 업데이트할 수 있도록 변경
  - FunapiUpdater 클래스 추가 (MonoBehaviour 오브젝트를 갖고 있음)
  - FunapiNetwork, FunapiDownloader 에 적용
- FunapiNetwork.Update를 Obsolete 처리 함 (May 2016 에 삭제 예정)
- Thread에 안전한 이벤트 콜백 함수를 관리하기 위해 ThreadSafeEventList 추가
- FunapiManager, FunapiTimer 삭제

### 01/27/2016 (ver.135)
- Multicast 채널에서 누군가 퇴장해서 Leave 메시지를 받으면 무조건 채널 메시지 핸들러가 삭제되는 버그 수정

### 01/12/2016 (ver.125)
- 리소스 업데이트가 중단되었을 경우 이어서 시작할 수 있도록 Continue 기능 추가
- Connect 도중에 Stop이 호출될 경우 연결이 완료된 후에 종료 (IL2CPP 관련 버그)
- FunapiNetwork나 Transport의 Start() 함수가 중복 호출될 경우 로그를 출력하고 요청은 무시
- 연결 도중 Stop이 호출된 후에도 ConnectionTimeout 콜백이 호출되는 버그 수정
- 재시도 타이머가 FunapiTransport.Stop 호출로 삭제되어 재시도가 멈추는 버그 수정

### 01/04/2016 (ver.123)
- AutoReconnect 옵션이 적용 안되는 버그 수정

### 12/21/2015 (ver.122)
- 소셜 플러그인 개선 사항
  - 프로필 사진 다운로드 알림 콜백 추가
  - 콜백 함수 이름 변경, get method 이름 소문자로 변경
  - 트위터 기능 추가 (프로필 사진, 친구 정보 가져오기)

### 12/08/2015 (ver.121)
- Facebook SDK 7.2 적용
- 친구 목록 및 사진 요청 API 분리
- 프로필 사진 다운로드 수동/자동 옵션 추가
- additioal plugins 패키지의 원본 파일 추가

### 12/02/2015 (ver.120)
- 맥 실행파일 창모드에서 배경색 초기화가 안 되어서 깜빡이는 문제 수정
- Multicasting 채널 입/퇴장 콜백 추가 및 오류 처리
- Multicasting 메시지에 기본 정보로 sender 추가

멀티캐스팅 메시지 콜백 함수의 원형이 아래와 같이 변경되었습니다. 기존의 파라미터에서 sender 값이
추가되었습니다.
```csharp
public delegate void ChannelMessage(string channel_id, string sender, object body);
```

멀티캐스팅 관련 인터페이스가 변경되어 메뉴얼의 설명이 업데이트 되었습니다. 자세한 내용은 [멀티캐스팅 설명](http://www.ifunfactory.com/engine/documents/reference/ko/client-plugin.html#client-plugin-multicasting)
에서 확인해주세요.

### 11/18/2015 (ver.118)
- Time.timeScale 값이 0이어도 플러그인 타이머는 돌아가도록 하기 위해 플러그인 자체 deltaTime
값을 갖도록 수정
- 연결이 종료되는 도중에 재연결을 시도할 수 없도록 Start() 함수가 다음 업데이트에 처리되도록 수정

### 11/17/2015 (ver.117)
- Expected reply 응답 콜백에서 Expected reply가 포함된 패킷을 보내면 오류가 발생하는 버그 수정

### 11/11/2015 (ver.116)
- Http header 파싱 버그 수정

### 11/10/2015 (ver.115)
- 다운로드 실패시 파일당 3회까지 재시도
- 파일을 임시 파일로 다운로드 후 다운로드가 완료되면 원래 파일명으로 변경
- 다운로드 실패로 중지되었을 때 중지된 파일부터 재시도 하는 ContinueDownload 함수 추가
- 서버와 연결 후 응답이 지정된 시간 이상 없을 경우 연결을 끊음
- 문자열에 값을 더하는 방식의 로그를 String format 방식으로 변경
- 핑 관련 시간 설정 인터페이스 추가
- 핑 대기시간 초과시 재연결을 시도하는 코드 삭제 (사용자가 원하는 시점에 재연결을 할 수 있도록 하기 위해)
- 서버에서 세션 연결을 끊으면 플러그인도 연결 종료 처리
- 로그에 시간 정보 출력, 로그 함수에 String.Format 파라미터 추가
- Transport 종료시 더 이상의 재시도를 하지 않을 경우 재시도 카운트 초기화
- Transport 별로 타이머 생성해서 사용하도록 변경 (기존: 하나의 타이머 공유)
- Transport 연결 종료시 타이머 목록 초기화
- 타이머 추가시 timer_list_와 pending_list_ 검사 조건 분리
- 타이머 콜백 호출 전에 삭제된 타이머인지 한번 더 검사

### 09/25/2015 (ver.109)
- 공지사항 목록 개수 지정, 이미지 경로 얻는 함수 추가
- Multicasting에서 사용하는 FunapiNetwork 객체를 지정할 수 있도록 변경
- 멀티캐스팅 메시지로 Json 메시지도 사용할 수 있도록 수정
- Send 실패시 오류 처리 추가 (Tcp, Udp)
- **인터페이스가 변경되어 더 이상 사용할 수 없는 Obsolete 함수 제거**
- 공지사항 이미지 다운로드 관련 버그 수정
- HTTP - Cookie 값 저장, 보내기
- 리소스 파일 검증 속도 향상

### 09/08/2015 (ver.102)
- 업데이트 List 관련 크래쉬 버그 수정
- 리소스 다운로드시 파일 삭제 시점 변경

### 08/25/2015 (ver.100)
- C# Runtime Compile 지원 작업
- 메시지 관련 버그 수정

### 08/24/2015 (ver.99)
- HTTP Transport 에서 특정 상황에서 버퍼 컴팩션 잘못하는 버그 수정
- WWW로 메시지 전송에 실패했을 때 에러 처리
- 타이머의 등록, 삭제 관련 버그 수정

### 08/14/2015 (ver.97)
- 리소스 다운로드 방식 변경, 파일 크기 정보 추가

  - StartDownload 함수를 GetDownloadList 와 StartDownload 함수 두 개로 나눔
    (GetDownloadList 함수를 호출하면 다운로드 목록 확인 후 ReadyCallback 함수가 호출됨)
  - CurDownloadFileSize, TotalDownloadFileSize 함수 추가
  - MD5 계산을 동시에 할 수 있도록 변경 (기존엔 순차 처리)

### 08/04/2015 (ver.96)
- 유니티에서 FileStream을 비동기로 사용이 불가능하여 코루틴 사용으로 변경
- 파일 유효성을 체크하기 위해 MD5 계산하는 기능을 옵션으로 선택할 수 있도록 변경 (생성자 파라미터)
- 파일 목록 문자열이 192kb를 넘으면 유니티 로그로 출력시 에러가 발생하여 파일 목록 로그 주석 처리
- 비동기 함수에서 코루틴을 호출할 때 사용하기 위해 FunapiManager에 Action 이벤트큐 추가

### 07/23/2015 (ver.95)
- Connect 할 때 기존의 Transport가 갖고 있던 주소들은 모두 날리고 새로운 주소로 리셋

### 07/22/2015 (ver.94)
- CurrentDownloadFileCount, TotalDownloadFileCount 함수 추가
- Windows에서 파일 경로 구분이 '\'여서 경로를 찾지 못하는 버그 수정

### 07/15/2015 (ver.93)
- Http response timeout 체크를 Update에서 하던 것을 Timer 사용으로 변경
- region으로 묶는 형식에서 관련 있는 함수끼리 모아놓는 형식으로 변경
- FunapiTransport의 encoding과 protocol 이름을 Encoding, Protocol로 변경

### 07/15/2015 (ver.92)
- FunapiNetwork에 EnablePing 옵션 추가 (아무때나 켜고 끌 수 있는 옵션)
- 마지막 핑 값을 가져오는 PingTime 함수 추가

### 07/13/2015 (ver.91)
- 기존의 HttpWebRequest가 Unity Editor Windows 버전에서 Blocking 되는 경우가 있어
UnityEngine.WWW를 사용해서 메시지를 보내는 옵션을 추가
- Coroutine을 사용하기 위해 FunapiManager Singleton class 추가
- Tcp의 경우 재접속을 무한 시도하는 버그가 수정

### 07/13/2015 (ver.90)
- EnablePing 값이 true이면 핑 사용 (Tcp만 사용 가능)

### 07/13/2015 (ver.89)
- Connect, Reconnect 방식 변경

  - 기존의 연결, 재연결 룰을 아래와 같은 방식으로 변경

    - 클라이언트가 처음 서버에 연결할 때
      DNS 주소로 여러개가 나오나 보고, 여러 개가 나오면 순차적으로 접근 (하나면 그것만 시도)
      재시도할 때는 exponential back-off (최대 3회)
      재시도에 실패하면 IP-list가 주어진 경우 하나씩 시도
      모두 실패하면 ConnectFailureCallback 호출

    - 클라이언트가 `Redirect` 로 다른 서버로 이동할 때
      연결 시도한 서버만 계속 재접속 시도 (exponential back-off, 최대 3회)
      실패하면 ConnectFailureCallback 호출

    - 클라이언트가 연결이 끊겼을 때
      원래 붙어 있던 서버에 재접속 시도 (exponetial-backoff, 최대 3회)
      실패하면 DisconnectedCallback 호출

- Ping 관련 수정사항

  - Udp가 기본 프로토콜일 경우 Ping 사용 못하도록 막음
  - Ping Timeout 체크 기준 값을 횟수에서 시간으로 변경
  - 이전 연결에서 보낸 Ping 값을 재연결됐을 때 받으면 무시함

- Transport가 Stop될 때 Default protocol 변경하던 것 삭제
  - 연결이 잠시 끊겼다가 재연결될 때 기본 프로토콜이 바뀌면 안되므로 변경 안함

### 06/22/2015 (ver.87)
- Sequence number 관련 버그 수정
- Ping timeout 시간을 정할 수 있도록 Config.json 파일에 추가
- 연결이 끊겼을 때 재접속을 시도하는 옵션 추가
- Connect 실패 및 Disconnect에 대한 오류 처리
- Client - Server 간의 Ping time을 얻는 기능 추가

### 06/16/2015 (ver.82)
- Multicasting 관련 메시지의 숫자를 MessageType으로 변경

### 06/16/2015 (ver.81)
- 서버 접속 정보와 옵션 등을 설정하는 Config.json 파일 추가

  기존 인터페이스는 그대로 유지하고 설정파일을 사용하는 인터페이스가 추가되었습니다. 설정파일을 Load한
  뒤에도 사용자가 선택해서 설정파일을 사용해서 객체를 생성하거나 기존 방식대로 객체를 생성할 수 있습니다.
  사용방법은 샘플코드를 참고해 주세요.

  ```csharp
  // 초기화, Config.json 파일이 있는 경로를 파라미터로 전달
  FunapiConfig.Load("Config.json");
  ```

### 06/16/2015 (ver.80)
- MsgType -> Encoding 으로 변경

  인코딩 타입으로 사용하는 MsgType과 메시지 타입으로 사용하는 msgtype의 이름을 동일하게 사용하고
  있어 인코딩 관련 변수들의 이름이 Encoding으로 변경되었습니다. 기존 타입인 MsgType을 그대로
  사용하고 싶다면 해당 파일 상단에 아래와 같은 코드를 추가하면 됩니다. 아래와 같은 방법으로 기존 타입을
  그대로 사용하는 것이 가능하지만 될 수 있으면 Encoding 타입을 사용하는시는 것을 권장합니다.

  ```csharp
  using FunMsgType = Fun.FunEncoding;
  ```

- '+' 연산자로 되어 있는 문자열들을 String.Format을 사용하도록 변경
- iOS, Android 환경에 맞는 저장 경로를 구하도록 변경

### 06/16/2015 (ver.79)
- FunapiNetwork 파일 분리 및 불필요한 파일 삭제
- Facebook, Twitter 패키지 파일 업데이트 (연관된 파일들을 묶어서 패키징을 새로함)

### 06/12/2015 (ver.78)
- 리소스 다운로드 기능 업데이트
- Multicasting, Chat 관련 Close 함수 추가

### 06/04/2015 (ver.77)
- Transport 생성할 때 Encoding 타입을 지정하도록 변경. Transport 별로 Encoding 타입을 다르게
지정해서 사용 가능 (FunapiNetwork 생성자에서 지정하던 기능은 삭제 예정)
- Transport를 새로 만들지 않고 다른 서버로 재접속하는 기능 추가 (기존 세션을 유지하고 다른 서버로
  접속하는 기능은 해당 기능이 서버 릴리즈에 추가된 이후부터 사용하실 수 있습니다)

### 06/01/2015 (ver.76)
- Expected reply 메시지 중복 등록 허용
- SendMessage 파라미터에 기본값 적용해서 함수 개수 줄임
- 콜백 함수가 메인 쓰레드(유니티의 Update)에서만 호출되도록 수정
- 오류가 발생했을 때 Transport에 등록된 실패 콜백 호출
- StartedEventHandler, StoppedEventHandler 등의 핸들러가 TransportEventHandler 등의
공통 핸들러로 이름이 변경됨
- protobuf 메시지를 보내고 받을 때 symbolic name 으로 할 수 있게 수정

다음과 같은 현재의 코드를

```csharp
// 보내기
FunMessage msg = network.CreateFunMessage(..., 16);
SendMessage("pbuf_echo", msg);


// 받기
FunMessage msg = ...;
object obj = network_.GetMessage(msg, typeof(PbufEchoMessage), 16);
PbufEchoMessage echo = obj as PbufEchoMessage;
```

아래처럼 쓰게 변경

```csharp
// 보내기
FunMessage msg = network.CreateFunMessage(..., MessageType.pbuf_echo);
SendMessage(MessageType.pbuf_echo, msg);

// 받기
FunMessage msg = ...;
object obj = network_.GetMessage(msg, MessageType.pbuf_echo);
PbufEchoMessage echo = obj as PbufEchoMessage;
```

기존 인터페이스는 남겨두었으나 Obsolete 처리 (2015년 9월 삭제 예정)

Tools 이하의 파일들은 더 이상 사용되지 않습니다. protobuf-net의
클라이언트용 dll은 서버 환경에서 빌드해서 사용하셔야 합니다.


### 2015년 6월 이전 업데이트 생략
