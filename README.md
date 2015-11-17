Funapi plugin
========================

Funapi plugin의 업데이트 내용입니다.

## Release Note

### 11/18/2015 (ver.118)
- Time.timeScale 값이 0이어도 플러그인 타이머는 돌아가도록 하기 위해 플러그인 자체 deltaTime 값을 갖도록 수정
- 연결이 종료되는 도중에 재연결을 시도할 수 없도록 Start() 함수가 다음 업데이트에 처리되도록 수정

### 11/17/2015 (ver.117)
- Expected reply 응답 콜백에서 Expected reply가 포함된 패킷을 보내면 오류가 발생하는 버그 수정

### 11/11/2015 (ver.116)
- Http header 파싱 버그 수정

### 11/09/2015 (ver.115)
- 다운로드 실패시 파일당 3회까지 재시도
- 파일을 임시 파일로 다운로드 후 다운로드가 완료되면 원래 파일명으로 변경
- 다운로드 실패로 중지되었을 때 중지된 파일부터 재시도 하는 ContinueDownload 함수 추가

### 11/06/2015 (ver.114)
- 서버와 연결 후 응답이 지정된 시간 이상 없을 경우 연결을 끊음
- 문자열에 값을 더하는 방식의 로그를 String format 방식으로 변경

### 10/29/2015 (ver.113)
- 핑 관련 시간 설정 인터페이스 추가
- 핑 대기시간 초과시 재연결을 시도하는 코드 삭제 (사용자가 원하는 시점에 재연결을 할 수 있도록 하기 위해)

### 10/08/2015 (ver.112)
- 서버에서 세션 연결을 끊으면 플러그인도 연결 종료 처리

### 10/05/2015 (ver.111)
- 로그에 시간 정보 출력, 로그 함수에 String.Format 파라미터 추가

### 10/02/2015 (ver.110)
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
- 기존의 HttpWebRequest가 Unity Editor Windows 버전에서 Blocking 되는 경우가 있어 UnityEngine.WWW를 사용해서 메시지를 보내는 옵션을 추가
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

  기존 인터페이스는 그대로 유지하고 설정파일을 사용하는 인터페이스가 추가되었습니다. 설정파일을 Load한 뒤에도 사용자가 선택해서 설정파일을 사용해서 객체를 생성하거나 기존 방식대로 객체를 생성할 수 있습니다. 사용방법은 샘플코드를 참고해 주세요.

  ```csharp
  // 초기화, Config.json 파일이 있는 경로를 파라미터로 전달
  FunapiConfig.Load("Config.json");
  ```

### 06/16/2015 (ver.80)
- MsgType -> Encoding 으로 변경

  인코딩 타입으로 사용하는 MsgType과 메시지 타입으로 사용하는 msgtype의 이름을 동일하게 사용하고 있어 인코딩 관련 변수들의 이름이 Encoding으로 변경되었습니다. 기존 타입인 MsgType을 그대로 사용하고 싶다면 해당 파일 상단에 아래와 같은 코드를 추가하면 됩니다. 아래와 같은 방법으로 기존 타입을 그대로 사용하는 것이 가능하지만 될 수 있으면 Encoding 타입을 사용하는시는 것을 권장합니다.

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
- Transport 생성할 때 Encoding 타입을 지정하도록 변경. Transport 별로 Encoding 타입을 다르게 지정해서 사용 가능 (FunapiNetwork 생성자에서 지정하던 기능은 삭제 예정)
- Transport를 새로 만들지 않고 다른 서버로 재접속하는 기능 추가 (기존 세션을 유지하고 다른 서버로 접속하는 기능은 해당 기능이 서버 릴리즈에 추가된 이후부터 사용하실 수 있습니다)

### 06/01/2015 (ver.76)
- Expected reply 메시지 중복 등록 허용
- SendMessage 파라미터에 기본값 적용해서 함수 개수 줄임
- 콜백 함수가 메인 쓰레드(유니티의 Update)에서만 호출되도록 수정
- 오류가 발생했을 때 Transport에 등록된 실패 콜백 호출
- StartedEventHandler, StoppedEventHandler 등의 핸들러가 TransportEventHandler 등의 공통 핸들러로 이름이 변경됨
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

## Documentation

http://www.ifunfactory.com/engine/documents/reference/ko/client-plugin.html
