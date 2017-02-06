Funapi plugin unity
========================

이 플러그인은 iFun Engine 게임 서버를 사용하는 Unity3d 사용자를 위한 클라이언트 플러그인입니다.

## 기능

* TCP, UDP, HTTP 프로토콜 사용 가능
* JSON, Protobuf-net 형식의 메시지 타입 지원
* ChaCha20, AES-128을 포함한 4종류의 암호화 타입 지원
* 멀티캐스트, 채팅, 게임내 리소스 다운로드 등 다양한 기능 지원
* 페이스북, 트위터의 글쓰기, 친구 정보 요청 등의 기능 지원


## 서버 버전

플러그인 버전 190 이상은 **서버 버전 1903-experimental 이상이 필요** 하며 Protobuf DLL을 새로 빌드해야 할 수도 있습니다.


## 사용방법

### 다운로드
**git clone** 으로 다운 받거나 **zip 파일** 을 다운 받아 압축을 풀면 아래와 같은 폴더 목록을
확인할 수 있습니다.

```text
|- additional-plugins      # 페이스북, 트위터 플러그인
|- funapi-plugin-unity     # 플러그인 프로젝트
|- plugin-test             # 봇테스트 샘플
```

클라이언트 플러그인 코드는 ``funapi-plugin-unity`` 폴더에 있습니다.
``additional-plugins``와 ``plugin-test`` 폴더는 필요할 경우 사용하면 됩니다.

### 테스트 프로젝트
유니티를 실행해서 ``funapi-plugin-unity`` 폴더의 프로젝트를 열면 프로젝트 폴더 중에 ``Tester``
폴더가 있습니다. 여기에 테스트용 Scene과 샘플 코드들이 있습니다.

**Tester** Scene을 열어 보면 오른쪽 상단에 **OPTION** 버튼이 있는데 옵션창을 통해 테스트에 필요한
값들을 수정할 수 있습니다. 서버 주소가 로컬로 되어 있으니 서버가 로컬에 있지 않다면 **Server** 항목을
수정해 주세요.

서버를 띄우고 실행을 하면 여러가지 기능들을 테스트해 볼 수 있습니다.
서버를 설치하고 아무것도 변경하지 않았다면 기본적으로 TCP, HTTP의 JSON 포트만 열려 있습니다.
다른 프로토콜과 메시지 타입을 사용하려면 서버와 클라이언트의 프로토콜과 포트 번호를 동일하게 변경하고
테스트하면 됩니다.


### 내 프로젝트에 적용
다운받은 폴더 중에 ``funapi-plugin-unity/Assets`` 폴더로 들어가면 아래와 같은 목록이 나타납니다.

```text
|- Editor
|- Funapi
|- Plugins
|- Resources
|- Tester
|- FunMessageSerializer.dll
|- messages.dll
|- protobuf-net.dll
```

위의 폴더 중 ``Funapi``, ``Plugins`` 폴더를 플러그인을 사용할 프로젝트의 ``Assets`` 폴더로
복사하면 됩니다. **HTTPS**  를 사용한다면 ``Editor``와 ``Resources`` 폴더도 함께 복사해 주세요.

아래 세 개의 DLL은 Protobuf 메시지를 사용하기 위한 DLL인데 포함된 DLL들은 샘플용 메시지이므로
Protobuf를 사용할 경우 서버 프로젝트에서 메시지를 정의하고 만든 DLL을 사용해야 합니다.

자세한 사용방법은 ``Tester`` 폴더의 샘플과 도움말을 참고해 주세요.


## 도움말

클라이언트 플러그인의 도움말은
<http://www.ifunfactory.com/engine/documents/reference/ko/client-plugin.html> 를
참고해 주세요.

플러그인에 대한 궁금한 점은 <http://answers.ifunfactory.com> 에 질문을 올려주세요.
가능한 빠르게 답변해 드립니다.

그 외에 플러그인에 대한 문의 사항이나 버그 신고는 <funapi-support@ifunfactory.com> 으로 메일을
보내주세요.


## 버전별 주요 이슈

아래 설명의 버전보다 낮은 버전의 플러그인을 사용하고 있다면 아래 내용을 참고해 주세요.

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
