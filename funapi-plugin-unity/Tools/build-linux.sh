#!/bin/bash -e

set +x
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

echo "Generating Protocol C# files"
protoc --include_imports \
    -o messages.bin \
    -I proto-files \
    proto-files/funapi/network/fun_message.proto \
    proto-files/funapi/network/maintenance.proto \
    proto-files/funapi/service/multicast_message.proto \
    proto-files/pbuf_echo.proto \
    proto-files/pbuf_multicast.proto

mkdir -p csharp-files
mono --runtime=${RUNTIME} "protobuf-net/ProtoGen/protogen.exe" \
    -i:"messages.bin" \
    -o:"csharp-files/messages.cs" \
    -p:detectMissing

echo 'Generating Protocol DLL'
gmcs -target:library -unsafe+ \
    -sdk:2 \
    -out:"${OUTPUT_ROOT}/messages.dll" \
    /r:"protobuf-net/unity/protobuf-net.dll" \
    csharp-files/messages.cs

echo 'Generating Serializer DLL'
mono --runtime=${RUNTIME} \
    "protobuf-net/Precompile/precompile.exe" \
    "${OUTPUT_ROOT}/messages.dll" \
    -probe:"${OUTPUT_ROOT}" \
    -o:"${OUTPUT_ROOT}/FunMessageSerializer.dll" \
    -t:"FunMessageSerializer"
