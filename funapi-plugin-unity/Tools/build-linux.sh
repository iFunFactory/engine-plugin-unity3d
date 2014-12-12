#!/bin/bash -e

set +e
command -v gmcs
if [ "$?" -ne "0" ]; then
  echo "gmcs command not found; You may install mono-mcs, "
  echo "libmono-corlib2.0-cli, libmono-system2.0-cli"
  exit 1;
fi

set -e
OUTPUT_ROOT=../Assets
RUNTIME=v2.0.50727

if [ -f /usr/include/funapi/network/fun_message.proto ]; then
  echo Generating Protocol C# files
  protoc -I/usr/include /usr/include/funapi/network/fun_message.proto \
    -o fun_message.bin
  mono --runtime=${RUNTIME} \
    protobuf-net/ProtoGen/protogen.exe \
    -i:fun_message.bin -o:fun_message.cs -p:detectMissing
fi

echo Generating Protocol DLL
gmcs -target:library -unsafe+ \
    -sdk:2 \
    -out:${OUTPUT_ROOT}/messages.dll \
    /r:protobuf-net/unity/protobuf-net.dll \
    fun_message.cs

echo Generating Serializer DLL
mono --runtime=${RUNTIME} \
    protobuf-net/Precompile/precompile.exe \
    ${OUTPUT_ROOT}/messages.dll \
    -o:${OUTPUT_ROOT}/FunMessageSerializer.dll \
    -t:FunMessageSerializer

