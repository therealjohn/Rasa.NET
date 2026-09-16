FROM mcr.microsoft.com/dotnet/sdk:10.0.401

WORKDIR /app

COPY src /app/src
COPY Rasa.NET.sln /app
COPY Rasa.NET.sln.DotSettings /app
COPY global.json /app
COPY .config /app/.config

RUN dotnet restore
RUN dotnet build --no-restore --configuration Release
