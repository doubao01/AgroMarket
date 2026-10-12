# 多阶段构建 .NET 10 Web API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY AgroMarket.Api/AgroMarket.Api.csproj AgroMarket.Api/
RUN dotnet restore AgroMarket.Api/AgroMarket.Api.csproj
COPY AgroMarket.Api/ AgroMarket.Api/
RUN dotnet publish AgroMarket.Api/AgroMarket.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
RUN mkdir -p /app/uploads
ENTRYPOINT ["dotnet", "AgroMarket.Api.dll"]