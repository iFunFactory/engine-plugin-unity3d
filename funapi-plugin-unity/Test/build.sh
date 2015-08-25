PLUGIN_ROOT=../Assets

mcs /debug -define:NO_UNITY \
    -target:exe -out:tester.exe main.cs \
    ${PLUGIN_ROOT}/Funapi/ConnectList.cs \
    ${PLUGIN_ROOT}/Funapi/FunapiTransport.cs \
    ${PLUGIN_ROOT}/Funapi/FunapiNetwork.cs \
    ${PLUGIN_ROOT}/Funapi/FunapiMulticasting.cs \
    ${PLUGIN_ROOT}/Funapi/FunapiEncryption.cs \
    ${PLUGIN_ROOT}/Funapi/FunapiChat.cs \
    ${PLUGIN_ROOT}/Funapi/FunapiUtils.cs \
    ${PLUGIN_ROOT}/Funapi/DebugUtils.cs \
    ${PLUGIN_ROOT}/Funapi/JsonAccessor.cs \
    ${PLUGIN_ROOT}/plugins/MiniJSON.cs \
    /r:FunMessageSerializer.dll \
    /r:messages.dll \
    /r:protobuf-net.dll