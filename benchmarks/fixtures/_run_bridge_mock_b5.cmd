@echo off
setlocal
for %%D in ("%~dp0..\..") do set "ROOT=%%~fD"
set "TESTDIR=%~dp0"
set "OUT=%ROOT%\dist-b5\bridge-mock"
set "VCVARS=%ProgramFiles%\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat"
if not exist "%VCVARS%" (
 echo Missing MSVC developer environment: "%VCVARS%"
 exit /b 2
)
if not exist "%OUT%" mkdir "%OUT%"
call "%VCVARS%" x86 >nul
if errorlevel 1 exit /b 3
cl /nologo /EHsc /O2 /LD "%TESTDIR%mock_ats_b5.cpp" /Fo:"%OUT%\mock_ats.obj" /link /OUT:"%OUT%\mock_ats.dll" /DEF:"%TESTDIR%mock_ats_b5.def" >"%OUT%\compile-ats.log" 2>&1
if errorlevel 1 (
 type "%OUT%\compile-ats.log"
 exit /b 4
)
call "%VCVARS%" x64 >nul
if errorlevel 1 exit /b 5
cl /nologo /EHsc /O2 /std:c++17 "%TESTDIR%bridge_harness_b5.cpp" /Fo:"%OUT%\bridge_harness.obj" /link /OUT:"%OUT%\bridge_harness.exe" >"%OUT%\compile-harness.log" 2>&1
if errorlevel 1 (
 type "%OUT%\compile-harness.log"
 exit /b 6
)
echo MOCK_BUILD_OK
