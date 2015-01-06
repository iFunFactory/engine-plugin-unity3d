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
  echo 'Generating Protocol C# files'
  mkdir csharp-files/bin
  protoc -I"/usr/include" "proto-files/funapi/network/fun_message.proto" -o"csharp-files/bin/fun_message.bin"
  protoc -I"proto-files" "proto-files/pbuf_echo.proto" -o"csharp-files/bin/pbuf_echo.bin"

  mono --runtime=${RUNTIME} "protobuf-net/ProtoGen/protogen.exe" \
                            -i:"csharp-files/bin/fun_message.bin" -o:"csharp-files/fun_message.cs" -:detectMissing
  mono --runtime=${RUNTIME} "protobuf-net/ProtoGen/protogen.exe" \
                            -i:"csharp-files/bin/pbuf_echo.bin" -o:"csharp-files/pbuf_echo.cs" -:detectMissing

  rm -rf "csharp-files/bin"
fi

echo 'Generating Protocol DLL'
gmcs -target:library -unsafe+ \
    -sdk:2 \
    -out:"${OUTPUT_ROOT}/messages.dll" \
    /r:"protobuf-net/unity/protobuf-net.dll" \
    "csharp-files/*.cs"

echo 'Generating Serializer DLL'
mono --runtime=${RUNTIME} \
    "protobuf-net/Precompile/precompile.exe" \
    "${OUTPUT_ROOT}/messages.dll" \
    -o:"${OUTPUT_ROOT}/FunMessageSerializer.dll" \
    -t:"FunMessageSerializer"
