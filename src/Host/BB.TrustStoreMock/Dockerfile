# Multi-stage build for BB.TrustStoreMock. The build context must be the
# project folder (Render sets rootDir: src/BB.TrustStoreMock) so COPY . .
# brings in the csproj + source tree.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore BB.TrustStoreMock.csproj \
 && dotnet publish BB.TrustStoreMock.csproj -c Release -o /app /p:UseAppHost=false

# Runtime: ASP.NET shared-base image. The mock exposes port 10000 to match
# Render's expected port; the platform reads ASPNETCORE_URLS to override.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Force the mock into Staging so the IsProduction() guard in Program.cs does
# not refuse to start. Free-tier Render Blueprint services have proven
# unreliable about preserving per-service env vars across auto-deploys; this
# ENV ships baked into the image and cannot be silently dropped. The Neon
# connection string is intentionally NOT here — that one MUST come from the
# Render env var at runtime (a DATABASE PASSWORD never belongs in a Docker
# image that may be inspected or pushed to a registry).
ENV ASPNETCORE_ENVIRONMENT=Staging

EXPOSE 10000
ENTRYPOINT ["dotnet", "BB.TrustStoreMock.dll"]
