Funapi plugin
========================

Funapi plugin의 업데이트 내용입니다.

## Release Note

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