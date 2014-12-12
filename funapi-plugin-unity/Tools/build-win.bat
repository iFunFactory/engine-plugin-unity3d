@echo off

set OUTPUT_ROOT=..\Assets

if exist "C:\Program Files\Unity\Editor\Data\Mono\bin\gmcs.bat" ^
set UNITY_MONO=C:\Program Files\Unity\Editor\Data

if exist "C:\Program Files (x86)\Unity\Editor\Data\Mono\bin\gmcs.bat" ^
set UNITY_MONO=C:\Program Files (x86)\Unity\Editor\Data

set PATH=%PATH%;"%UNITY_MONO%\Mono\bin"

REM echo Generating C# protocol files
REM protobuf-net\ProtoGen\protogen -i:funapi\network\fun_message.proto -o:fun_message.cs -p:detectMissing
REM protobuf-net\ProtoGen\protogen -i:foo_messages.proto -o:foo_messages.cs -p:detectMissing

echo Generating Protocol DLL
call gmcs -target:library -unsafe+ ^
	-out:%OUTPUT_ROOT%\DLL\messages.dll ^
	/r:%OUTPUT_ROOT%\DLL\protobuf-net.dll ^
	fun_message.cs

echo Generating serializer DLL
protobuf-net\Precompile\precompile %OUTPUT_ROOT%\messages.dll ^
	-o:%OUTPUT_ROOT%\DLL\FunMessageSerializer.dll ^
	-t:FunMessageSerializer

