FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish backend/src/IntegraOps.Api/IntegraOps.Api.csproj -c Release -o /out/api
RUN dotnet publish backend/src/IntegraOps.Worker/IntegraOps.Worker.csproj -c Release -o /out/worker
RUN dotnet publish samples/OrderSource/OrderSource.csproj -c Release -o /out/source

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /out/api .
RUN mkdir -p /var/lib/integraops/keys && chown -R app:app /var/lib/integraops
ENV Protection__KeyPath=/var/lib/integraops/keys
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "IntegraOps.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker
WORKDIR /app
COPY --from=build /out/worker .
RUN mkdir -p /var/lib/integraops/keys && chown -R app:app /var/lib/integraops
ENV Protection__KeyPath=/var/lib/integraops/keys
USER app
ENTRYPOINT ["dotnet", "IntegraOps.Worker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS source
WORKDIR /app
COPY --from=build /out/source .
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderSource.dll"]
