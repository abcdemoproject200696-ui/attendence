# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore (copy csproj + nuget.config first for layer caching)
COPY nuget.config ./
COPY Attendance.Domain/Attendance.Domain.csproj Attendance.Domain/
COPY Attendance.Infrastructure/Attendance.Infrastructure.csproj Attendance.Infrastructure/
COPY Attendance.Api/Attendance.Api.csproj Attendance.Api/
RUN dotnet restore Attendance.Api/Attendance.Api.csproj

# Build + publish the API
COPY . .
RUN dotnet publish Attendance.Api/Attendance.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Npgsql needs the Kerberos/GSSAPI shared lib (libgssapi_krb5.so.2) during the
# PostgreSQL auth handshake; the slim runtime image doesn't ship it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

# Render injects PORT; Program.cs binds to http://0.0.0.0:$PORT when set.
# ASPNETCORE_URLS is a fallback for plain `docker run` without PORT.
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Attendance.Api.dll"]
