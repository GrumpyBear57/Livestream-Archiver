FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env
WORKDIR /app
COPY . ./
RUN dotnet restore
RUN dotnet publish -c Release --os linux -a x64 -o out

FROM mcr.microsoft.com/dotnet/runtime:6.0
WORKDIR /app
COPY --from=build-env /app/out .
COPY third-party-lib/yt-dlp_linux .
COPY third-party-lib/ffmpeg .
ENTRYPOINT ["./TwitchLiveArchiver"]