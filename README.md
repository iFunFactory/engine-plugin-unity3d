Funapi plugin
========================

Funapi plugin의 업데이트 내용입니다.

## Release Note

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
