FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Northstar.slnx ./
COPY Directory.Build.props ./
COPY src/Northstar.Api/Northstar.Api.csproj src/Northstar.Api/
COPY src/Northstar.Application/Northstar.Application.csproj src/Northstar.Application/
COPY src/Northstar.Domain/Northstar.Domain.csproj src/Northstar.Domain/
COPY src/Northstar.Infrastructure/Northstar.Infrastructure.csproj src/Northstar.Infrastructure/
RUN dotnet restore src/Northstar.Api/Northstar.Api.csproj

COPY src/ src/
RUN dotnet publish src/Northstar.Api/Northstar.Api.csproj --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Northstar.Api.dll"]
