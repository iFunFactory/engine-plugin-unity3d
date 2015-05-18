#!/bin/bash -e

OUTPUT=csharp-files
FUNPATH=proto-files/funapi/network
SRVPATH=proto-files/funapi/service
PROTOPATH=proto-files
PROTOGEN_EXE=protobuf-net/ProtoGen/protogen.exe

echo 'Generating .bin files'
mkdir -p "${OUTPUT}/bin"
protoc -I"${PROTOPATH}/" "${FUNPATH}/fun_message.proto" -o"${OUTPUT}/bin/fun_message.bin"
protoc -I"${PROTOPATH}/" "${FUNPATH}/maintenance.proto" -o"${OUTPUT}/bin/maintenance.bin"
protoc -I"${PROTOPATH}/" "${SRVPATH}/multicast_message.proto" -o"${OUTPUT}/bin/multicast_message.bin"
protoc -I"${PROTOPATH}/" "${PROTOPATH}/pbuf_echo.proto" -o"${OUTPUT}/bin/pbuf_echo.bin"
protoc -I"${PROTOPATH}/" "${PROTOPATH}/pbuf_multicast.proto" -o"${OUTPUT}/bin/pbuf_multicast.bin"

echo 'Generating .cs files'
mono "${PROTOGEN_EXE}" -i:"${OUTPUT}/bin/fun_message.bin" -o:"${OUTPUT}/fun_message.cs" -p:detectMissing
mono "${PROTOGEN_EXE}" -i:"${OUTPUT}/bin/maintenance.bin" -o:"${OUTPUT}/maintenance.cs"
mono "${PROTOGEN_EXE}" -i:"${OUTPUT}/bin/multicast_message.bin" -o:"${OUTPUT}/multicast_message.cs"
mono "${PROTOGEN_EXE}" -i:"${OUTPUT}/bin/pbuf_echo.bin" -o:"${OUTPUT}/pbuf_echo.cs"
mono "${PROTOGEN_EXE}" -i:"${OUTPUT}/bin/pbuf_multicast.bin" -o:"${OUTPUT}/pbuf_multicast.cs"

echo 'Deleting .bin files'
rm -rf "${OUTPUT}/bin"
