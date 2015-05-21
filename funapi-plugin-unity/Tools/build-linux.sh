#!/bin/bash

if [[ $# < 1 ]]; then
  echo "Usage: $0 {path to .proto file} [{another .proto file} ...]"
  exit 1
fi


INCLUDE_DIR=proto-files
for proto_file in $*; do
  if [[ ! -e $proto_file ]]; then
    echo ".proto file \"$proto_file\" does not exist"
    exit 2
  fi

  echo $(dirname "$proto_file") $(basename "$proto_file")
  INCLUDE_DIR+=":$(dirname $proto_file)"
done

command -v gmcs
if [ "$?" -ne "0" ]; then
  echo "gmcs command not found; You may install mono-mcs, "
  echo "libmono-corlib2.0-cli, libmono-system2.0-cli"
  exit 1;
fi

set -ex
OUTPUT_ROOT=../Assets
RUNTIME=v2.0.50727

echo "Generating Protocol C# files"
protoc --include_imports \
    -o messages.bin \
    -I "${INCLUDE_DIR}" \
    proto-files/funapi/network/fun_message.proto \
    proto-files/funapi/network/maintenance.proto \
    proto-files/funapi/service/multicast_message.proto \
    $*

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
