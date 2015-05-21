#!/bin/bash -e

OUTPUT=csharp-files
FUNPATH=proto-files/funapi/network
SRVPATH=proto-files/funapi/service
PROTOPATH=proto-files
PROTOGEN_EXE=protobuf-net/ProtoGen/protogen.exe

RUNTIME=v2.0.50727
OUTPUT_ROOT=${OUTPUT_ROOT:-../Assets}


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


echo 'Generating .bin files'
mkdir -p "${OUTPUT}/bin"
protoc --include_imports \
    -o "${OUTPUT}/bin/messages.bin" \
    -I "${INCLUDE_DIR}" \
    "${FUNPATH}/fun_message.proto" \
    "${FUNPATH}/maintenance.proto" \
    "${SRVPATH}/multicast_message.proto" \
    $*

echo 'Generating .cs files'
mono --runtime=${RUNTIME} ${PROTOGEN_EXE} \
    -i:${OUTPUT}/bin/messages.bin \
    -o:${OUTPUT}/messages.cs \
    -p:detectMissing

echo 'Deleting .bin files'
rm -rf "${OUTPUT}/bin"

UNITY_MONO=${UNITY_MONO:-/Applications/Unity/Unity.app/Contents/Frameworks/Mono}
export MONO_PATH=${UNITY_MONO}/lib/mono/2.0
echo 'Generating Protocol DLL'
"${UNITY_MONO}/bin/gmcs" -target:library -unsafe+ \
    -out:"${OUTPUT_ROOT}/messages.dll" \
    /r:"protobuf-net/unity/protobuf-net.dll" \
    "csharp-files/messages.cs"

echo 'Generating Serializer DLL'
"${UNITY_MONO}/bin/mono" "protobuf-net/Precompile/precompile.exe" \
    "${OUTPUT_ROOT}/messages.dll" \
    -o:"${OUTPUT_ROOT}/FunMessageSerializer.dll" \
    -t:"FunMessageSerializer"
