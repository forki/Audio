FROM microsoft/dotnet:2.2.0-runtime
COPY /deploy .
ADD https://yt-dl.org/downloads/latest/youtube-dl /usr/local/bin/youtube-dl
RUN  chmod a+rx /usr/local/bin/youtube-dl
WORKDIR .
EXPOSE 8085
ENTRYPOINT ["dotnet", "Server.dll"]
