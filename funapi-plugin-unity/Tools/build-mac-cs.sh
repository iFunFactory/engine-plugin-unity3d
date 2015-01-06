#!/bin/bash -e

OUTPUT=csharp-files
FUNPATH=proto-files/funapi/network
PROTOPATH=proto-files
PROTOGEN_EXE=protobuf-net/ProtoGen/protogen.exe

echo 'Generating .bin files'
mkdir -p "${OUTPUT}/bin"
protoc -I"${PROTOPATH}/" "${FUNPATH}/fun_message.proto" -o"${OUTPUT}/bin/fun_message.bin"
protoc -I"${PROTOPATH}/" "${FUNPATH}/maintenance.proto" -o"${OUTPUT}/bin/maintenance.bin"
protoc -I"${PROTOPATH}/" "${PROTOPATH}/pbuf_echo.proto" -o"${OUTPUT}/bin/pbuf_echo.bin"

echo 'Generating .cs files'
mono "${PROTOGEN_EXE}" -i:"${OUTPUT}/bin/fun_message.bin" -o:"${OUTPUT}/fun_message.cs" -p:detectMissing
mono "${PROTOGEN_EXE}" -i:"${OUTPUT}/bin/maintenance.bin" -o:"${OUTPUT}/maintenance.cs"
mono "${PROTOGEN_EXE}" -i:"${OUTPUT}/bin/pbuf_echo.bin" -o:"${OUTPUT}/pbuf_echo.cs"

echo 'Deleting .bin files'
rm -rf "${OUTPUT}/bin"
