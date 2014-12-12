#!/bin/bash -e

OUTPUT_ROOT=../Assets
UNITY_MONO=/Applications/Unity/Unity.app/Contents/Frameworks/Mono
export MONO_PATH=${UNITY_MONO}/lib/mono/2.0


echo Generating Protocol DLL
${UNITY_MONO}/bin/gmcs -target:library -unsafe+ \
    -out:${OUTPUT_ROOT}/messages.dll \
    /r:protobuf-net/unity/protobuf-net.dll \
    fun_message.cs

echo Generating Serializer DLL
${UNITY_MONO}/bin/mono protobuf-net/Precompile/precompile.exe \
    ${OUTPUT_ROOT}/messages.dll \
    -o:${OUTPUT_ROOT}/FunMessageSerializer.dll \
    -t:FunMessageSerializer
