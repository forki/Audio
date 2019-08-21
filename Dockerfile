FROM mcr.microsoft.com/dotnet/core/runtime:latest
COPY /deploy .
WORKDIR .
EXPOSE 8085
ENTRYPOINT ["dotnet", "Server.dll"]
