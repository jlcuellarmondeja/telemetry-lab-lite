@echo off
rem Builds the images used by the Kubernetes manifests (k8s\).
rem Usage: scripts\build-images.cmd [version]   (default 1.0.0 = tag used in k8s\deployment.yaml)
setlocal

set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=1.0.0"

rem Always run from the repo root: the Dockerfiles use it as build context.
pushd "%~dp0.."

where docker >nul 2>&1 || (echo docker not found in PATH & goto :error)

call :build telemetryworker   src\Telemetry.Worker\Dockerfile   || goto :error
call :build telemetryproducer src\Telemetry.Producer\Dockerfile || goto :error

echo ^>^> Done:
docker image ls --filter "reference=telemetry*:%VERSION%"
popd
exit /b 0

:build
echo ^>^> Building %1:%VERSION%
docker build -f %2 -t %1:%VERSION% .
exit /b %errorlevel%

:error
popd
echo ^>^> Build FAILED
exit /b 1