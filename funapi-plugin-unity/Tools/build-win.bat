@echo off

set OUTPUT_ROOT=..\Assets

set CSC_EXE=%SYSTEMROOT%\Microsoft.Net\Framework\v2.0.50727\csc.exe
set PROTOC_EXE=protobuf-net\ProtoGen\protoc.exe
set PROTOGEN_EXE=protobuf-net\ProtoGen\ProtoGen.exe
set PRECOMPILE_EXE=protobuf-net\Precompile\precompile.exe


setlocal enabledelayedexpansion
set INCLUDE_PATH=proto-files

for %%x in (%*) do (
   if exist %%x (
     for %%F in (%%x) do set dirname=%%~dpF
     set INCLUDE_PATH=!INCLUDE_PATH!:"%dirname:~0,-1%"
   ) else (
      echo File %%x does not exist
      exit /b 1
   )
)

set FINAL_INC_PATH=!INCLUDE_PATH!
echo %FINAL_INC_PATH%

echo Generating Protocol C# files
%PROTOC_EXE% -I proto-files ^
    --include_imports ^
    -o messages.bin ^
    proto-files\funapi\network\fun_message.proto ^
    proto-files\funapi\network\maintenance.proto ^
    proto-files\funapi\service\multicast_message.proto ^
    %*

%PROTOGEN_EXE% -i:messages.bin -o:messages.cs -p:detectMissing

echo Building protocol dll
%CSC_EXE% /target:library /unsafe /out:%OUTPUT_ROOT%\messages.dll ^
    /r:protobuf-net/unity/protobuf-net.dll messages.cs

echo Generating Serializer dll
%PRECOMPILE_EXE% %OUTPUT_ROOT%\messages.dll ^
    -probe:%OUTPUT_ROOT% ^
    -o:%OUTPUT_ROOT%\FunMessageSerializer.dll ^
    -t:FunMessageSerializer

endlocal
