# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first so the package layer is cached until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Appointments.Api/Appointments.Api.csproj src/Appointments.Api/
RUN dotnet restore src/Appointments.Api/Appointments.Api.csproj
COPY src/ src/
RUN dotnet publish src/Appointments.Api/Appointments.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# The .NET images ship a non-root user; ASP.NET Core listens on 8080 by default.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Appointments.Api.dll"]
