# Built from the repository root: docker build -f src/PubInvest.HouseConfig.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, so a source-only change does not re-download every package.
COPY Directory.Packages.props ./
COPY src/PubInvest.HouseConfig.Domain/*.csproj src/PubInvest.HouseConfig.Domain/
COPY src/PubInvest.HouseConfig.Data/*.csproj src/PubInvest.HouseConfig.Data/
COPY src/PubInvest.HouseConfig.Api/*.csproj src/PubInvest.HouseConfig.Api/
RUN dotnet restore src/PubInvest.HouseConfig.Api/PubInvest.HouseConfig.Api.csproj

COPY src/ src/
COPY seed/ seed/
RUN dotnet publish src/PubInvest.HouseConfig.Api/PubInvest.HouseConfig.Api.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./

# Runs as a non-root user: nothing here needs to write to the image.
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "PubInvest.HouseConfig.Api.dll"]
