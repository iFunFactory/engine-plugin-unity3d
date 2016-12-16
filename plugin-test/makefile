# Copyright (C) 2013-2016 iFunFactory Inc. All Rights Reserved.
#
# This work is confidential and proprietary to iFunFactory Inc and
# must not be used, disclosed, copied, or distributed without the prior
# consent of iFunFactory Inc.

PLUGIN_ROOT=../funapi-plugin-unity/Assets
SRC_ROOT=src

tester.exe: ${SRC_ROOT}/main.cs \
		${SRC_ROOT}/client.cs \
		FunMessageSerializer.dll messages.dll protobuf-net.dll \
		${PLUGIN_ROOT}/Funapi/ConnectList.cs \
		${PLUGIN_ROOT}/Funapi/Crypto.cs \
		${PLUGIN_ROOT}/Funapi/DebugUtils.cs \
		${PLUGIN_ROOT}/Funapi/FunapiAnnouncement.cs \
		${PLUGIN_ROOT}/Funapi/FunapiChat.cs \
		${PLUGIN_ROOT}/Funapi/FunapiDownloader.cs \
		${PLUGIN_ROOT}/Funapi/FunapiEncryption.cs \
		${PLUGIN_ROOT}/Funapi/FunapiMulticasting.cs \
		${PLUGIN_ROOT}/Funapi/FunapiMessage.cs \
		${PLUGIN_ROOT}/Funapi/FunapiNetwork.cs \
		${PLUGIN_ROOT}/Funapi/FunapiSession.cs \
		${PLUGIN_ROOT}/Funapi/FunapiSession.Transport.cs \
		${PLUGIN_ROOT}/Funapi/FunapiTransport.cs \
		${PLUGIN_ROOT}/Funapi/FunapiUtils.cs \
		${PLUGIN_ROOT}/Funapi/JsonAccessor.cs \
		${PLUGIN_ROOT}/Funapi/MD5Async.cs \
		${PLUGIN_ROOT}/Funapi/MozRoots.cs \
		${PLUGIN_ROOT}/Funapi/SessionId.cs \
		${PLUGIN_ROOT}/plugins/MiniJSON.cs

	mcs /debug -define:NO_UNITY \
		-target:exe -out:$@ \
		${SRC_ROOT}/main.cs \
		${SRC_ROOT}/client.cs \
		${PLUGIN_ROOT}/Funapi/ConnectList.cs \
		${PLUGIN_ROOT}/Funapi/Crypto.cs \
		${PLUGIN_ROOT}/Funapi/DebugUtils.cs \
		${PLUGIN_ROOT}/Funapi/FunapiAnnouncement.cs \
		${PLUGIN_ROOT}/Funapi/FunapiChat.cs \
		${PLUGIN_ROOT}/Funapi/FunapiDownloader.cs \
		${PLUGIN_ROOT}/Funapi/FunapiEncryption.cs \
		${PLUGIN_ROOT}/Funapi/FunapiMulticasting.cs \
		${PLUGIN_ROOT}/Funapi/FunapiMessage.cs \
		${PLUGIN_ROOT}/Funapi/FunapiNetwork.cs \
		${PLUGIN_ROOT}/Funapi/FunapiSession.cs \
		${PLUGIN_ROOT}/Funapi/FunapiSession.Transport.cs \
		${PLUGIN_ROOT}/Funapi/FunapiTransport.cs \
		${PLUGIN_ROOT}/Funapi/FunapiUtils.cs \
		${PLUGIN_ROOT}/Funapi/JsonAccessor.cs \
		${PLUGIN_ROOT}/Funapi/MD5Async.cs \
		${PLUGIN_ROOT}/Funapi/MozRoots.cs \
		${PLUGIN_ROOT}/Funapi/SessionId.cs \
		${PLUGIN_ROOT}/plugins/MiniJSON.cs \
		/r:FunMessageSerializer.dll \
		/r:messages.dll \
		/r:protobuf-net.dll


FunMessageSerializer.dll: ${PLUGIN_ROOT}/FunMessageSerializer.dll
	cp $^ .

messages.dll: ${PLUGIN_ROOT}/messages.dll
	cp $^ .

protobuf-net.dll: ${PLUGIN_ROOT}/protobuf-net.dll
	cp $^ .
