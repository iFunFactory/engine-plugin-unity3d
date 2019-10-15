# Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
#
# This work is confidential and proprietary to iFunFactory Inc and
# must not be used, disclosed, copied, or distributed without the prior
# consent of iFunFactory Inc.

PLUGIN_ROOT=../funapi-plugin-unity/Assets
SRC_ROOT=src

tester.exe: ${SRC_ROOT}/*.cs \
		${PLUGIN_ROOT}/Funapi/*.cs \
		${PLUGIN_ROOT}/plugins/MiniJSON.cs \
		FunMessageSerializer.dll \
		messages.dll \
		protobuf-net.dll \
		websocket-sharp.dll \
		Newtonsoft.Json.dll

	mcs /debug -define:NO_UNITY \
		-target:exe -out:$@ \
		${SRC_ROOT}/*.cs \
		${PLUGIN_ROOT}/Funapi/*.cs \
		${PLUGIN_ROOT}/plugins/MiniJSON.cs \
		/r:FunMessageSerializer.dll \
		/r:messages.dll \
		/r:protobuf-net.dll \
		/r:websocket-sharp.dll \
		/r:Newtonsoft.Json.dll


FunMessageSerializer.dll: ${PLUGIN_ROOT}/FunMessageSerializer.dll
	cp $^ .

messages.dll: ${PLUGIN_ROOT}/messages.dll
	cp $^ .

protobuf-net.dll: ${PLUGIN_ROOT}/protobuf-net.dll
	cp $^ .

websocket-sharp.dll: ${PLUGIN_ROOT}/websocket-sharp.dll
	cp $^ .

Newtonsoft.Json.dll: ${PLUGIN_ROOT}/JsonDotNet/Assemblies/Standalone/Newtonsoft.Json.dll
	cp $^ .
