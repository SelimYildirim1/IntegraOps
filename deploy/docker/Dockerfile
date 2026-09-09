FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish backend/src/IntegraOps.Api/IntegraOps.Api.csproj -c Release -o /out/api
RUN dotnet publish backend/src/IntegraOps.Worker/IntegraOps.Worker.csproj -c Release -o /out/worker

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /out/api .
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "IntegraOps.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker
WORKDIR /app
COPY --from=build /out/worker .
USER app
ENTRYPOINT ["dotnet", "IntegraOps.Worker.dll"]
