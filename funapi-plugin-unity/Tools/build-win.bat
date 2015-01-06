@echo off

set OUTPUT_ROOT=..\Assets

if exist "C:\Program Files\Unity\Editor\Data\Mono\bin\gmcs.bat" ^
set UNITY_MONO=C:\Program Files\Unity\Editor\Data

if exist "C:\Program Files (x86)\Unity\Editor\Data\Mono\bin\gmcs.bat" ^
set UNITY_MONO=C:\Program Files (x86)\Unity\Editor\Data

set PATH=%PATH%;"%UNITY_MONO%\Mono\bin"

REM echo Generating C# protocol files
REM protobuf-net\ProtoGen\protogen -i:proto-files\funapi\network\fun_message.proto -o:csharp-files\fun_message.cs -p:detectMissing
REM protobuf-net\ProtoGen\protogen -i:proto-files\pbuf_echo.proto -o:csharp-files\pbuf_echo.cs -p:detectMissing

echo Generating Protocol DLL
call gmcs -target:library -unsafe+ ^
    -out:%OUTPUT_ROOT%\DLL\messages.dll ^
    /r:%OUTPUT_ROOT%\DLL\protobuf-net.dll ^
    csharp-files\*.cs

echo Generating serializer DLL
protobuf-net\Precompile\precompile %OUTPUT_ROOT%\messages.dll ^
    -o:%OUTPUT_ROOT%\DLL\FunMessageSerializer.dll ^
    -t:FunMessageSerializer
