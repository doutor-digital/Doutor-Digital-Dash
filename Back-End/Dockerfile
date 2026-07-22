# syntax=docker/dockerfile:1

# ─── build ───────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore numa camada própria: só refaz quando o .csproj muda, não a cada commit.
COPY LeadAnalytics.Api/*.csproj LeadAnalytics.Api/
RUN dotnet restore LeadAnalytics.Api/LeadAnalytics.Api.csproj

COPY LeadAnalytics.Api/ LeadAnalytics.Api/
RUN dotnet publish LeadAnalytics.Api/LeadAnalytics.Api.csproj \
      -c Release -o /app --no-restore

# ─── runtime ─────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl      → healthcheck do Swarm (sem ele o start-first não sabe quando o
#             container novo ficou pronto, e o deploy deixa de ser sem downtime).
# fontconfig→ QuestPDF/SkiaSharp quebra na geração de PDF sem as libs de fonte.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl libfontconfig1 \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# Usuário não-root já existente nas imagens .NET.
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1

EXPOSE 8080

ENTRYPOINT ["dotnet", "LeadAnalytics.Api.dll"]
