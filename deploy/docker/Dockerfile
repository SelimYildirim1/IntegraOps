FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish backend/src/IntegraOps.Api/IntegraOps.Api.csproj -c Release -o /out/api
RUN dotnet publish backend/src/IntegraOps.Worker/IntegraOps.Worker.csproj -c Release -o /out/worker
RUN dotnet publish samples/OrderSource/OrderSource.csproj -c Release -o /out/source

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
ARG APP_VERSION=dev
ARG APP_REVISION=unknown
# The running version and commit are reported by /health/version and on every metric.
ENV APP_VERSION=$APP_VERSION APP_REVISION=$APP_REVISION
LABEL org.opencontainers.image.source="https://github.com/SelimYildirim1/IntegraOps" \
      org.opencontainers.image.version="$APP_VERSION" \
      org.opencontainers.image.revision="$APP_REVISION" \
      org.opencontainers.image.title="integraops-api"
WORKDIR /app
COPY --from=build /out/api .
# Exports are written by the API and served from the same volume; the directory must exist with app ownership before the volume is created.
RUN mkdir -p /var/lib/integraops/keys /var/lib/integraops/exports && chown -R app:app /var/lib/integraops
ENV Protection__KeyPath=/var/lib/integraops/keys Exports__Path=/var/lib/integraops/exports
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "IntegraOps.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker
ARG APP_VERSION=dev
ARG APP_REVISION=unknown
# The running version and commit are reported by /health/version and on every metric.
ENV APP_VERSION=$APP_VERSION APP_REVISION=$APP_REVISION
LABEL org.opencontainers.image.source="https://github.com/SelimYildirim1/IntegraOps" \
      org.opencontainers.image.version="$APP_VERSION" \
      org.opencontainers.image.revision="$APP_REVISION" \
      org.opencontainers.image.title="integraops-worker"
WORKDIR /app
COPY --from=build /out/worker .
RUN mkdir -p /var/lib/integraops/keys && chown -R app:app /var/lib/integraops
ENV Protection__KeyPath=/var/lib/integraops/keys
USER app
ENTRYPOINT ["dotnet", "IntegraOps.Worker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS source
ARG APP_VERSION=dev
ARG APP_REVISION=unknown
# The running version and commit are reported by /health/version and on every metric.
ENV APP_VERSION=$APP_VERSION APP_REVISION=$APP_REVISION
LABEL org.opencontainers.image.source="https://github.com/SelimYildirim1/IntegraOps" \
      org.opencontainers.image.version="$APP_VERSION" \
      org.opencontainers.image.revision="$APP_REVISION" \
      org.opencontainers.image.title="integraops-source"
WORKDIR /app
COPY --from=build /out/source .
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderSource.dll"]
