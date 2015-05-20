@echo off

set OUTPUT_ROOT=..\Assets

if exist "C:\Program Files\Unity\Editor\Data\Mono\bin\gmcs.bat" ^
set UNITY_MONO=C:\Program Files\Unity\Editor\Data

if exist "C:\Program Files (x86)\Unity\Editor\Data\Mono\bin\gmcs.bat" ^
set UNITY_MONO=C:\Program Files (x86)\Unity\Editor\Data

set PATH=%PATH%;"%UNITY_MONO%\Mono\bin"

REM To generate .cs file, remove REM in following 3 lines.
REM echo Generating C# protocol files
REM protobuf-net\ProtoGen\protoc.exe -I proto-files --include_imports -o messages.bin proto-files\funapi\network\fun_message.proto proto-files\funapi\network\maintenance.proto proto-files\funapi\service\multicast_message.proto proto-files\pbuf-echo.proto proto-files\pbuf-multicast.proto
REM protobuf-net\ProtoGen\protogen.exe -i:messages.bin -o:messages.cs -p:detectMissing

echo Generating Protocol DLL
call gmcs -target:library -unsafe+ ^
    -out:%OUTPUT_ROOT%\DLL\messages.dll ^
    /r:%OUTPUT_ROOT%\DLL\protobuf-net.dll ^
    messages.cs

echo Generating serializer DLL
protobuf-net\Precompile\precompile %OUTPUT_ROOT%\messages.dll ^
    -probe:%OUTPUT_ROOT%\DLL\ ^
    -o:%OUTPUT_ROOT%\DLL\FunMessageSerializer.dll ^
    -t:FunMessageSerializer
