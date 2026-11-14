FROM mcr.microsoft.com/dotnet/sdk:8.0.428@sha256:1b955f179b02df4f0eb5fadedaa898cbe44d620a64aab52eec72c8e91d1355b5 AS build
WORKDIR /source

COPY global.json ./
COPY TempShare.Api/TempShare.Api.csproj TempShare.Api/
RUN dotnet restore TempShare.Api/TempShare.Api.csproj

COPY TempShare.Api/ TempShare.Api/
RUN dotnet publish TempShare.Api/TempShare.Api.csproj --configuration Release --no-restore --output /app -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0.34@sha256:dccacae9e0e103b5d3ba5fbe442dd8d3316b990cd8f2ec9be0a0ec9db0c0e520 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app ./
RUN mkdir -p /app/storage && chown $APP_UID /app/storage
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TempShare.Api.dll"]