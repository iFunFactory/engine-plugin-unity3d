#!/bin/bash -e

OUTPUT=csharp-files
FUNPATH=proto-files/funapi/network
SRVPATH=proto-files/funapi/service
PROTOPATH=proto-files
PROTOGEN_EXE=protobuf-net/ProtoGen/protogen.exe

RUNTIME=v2.0.50727

echo 'Generating .bin files'
mkdir -p "${OUTPUT}/bin"
protoc --include_imports \
    -o "${OUTPUT}/bin/messages.bin" \
    -I "${PROTOPATH}" \
    "${FUNPATH}/fun_message.proto" \
    "${FUNPATH}/maintenance.proto" \
    "${SRVPATH}/multicast_message.proto" \
    "${PROTOPATH}/pbuf_echo.proto" \
    "${PROTOPATH}/pbuf_multicast.proto"

echo 'Generating .cs files'
mono --runtime=${RUNTIME} ${PROTOGEN_EXE} \
    -i:${OUTPUT}/bin/messages.bin \
    -o:${OUTPUT}/messages.cs \
    -p:detectMissing

echo 'Deleting .bin files'
rm -rf "${OUTPUT}/bin"
