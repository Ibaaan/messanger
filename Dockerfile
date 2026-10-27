FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Messanger/Messanger.csproj Messanger/
COPY src/Messanger.Domain/Messanger.Domain.csproj src/Messanger.Domain/
COPY src/Messanger.Application/Messanger.Application.csproj src/Messanger.Application/
COPY src/Messanger.Infrastructure/Messanger.Infrastructure.csproj src/Messanger.Infrastructure/
RUN dotnet restore Messanger/Messanger.csproj
COPY . .
RUN dotnet publish Messanger/Messanger.csproj -c Release --no-restore -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS app
WORKDIR /app
COPY --from=build /publish .
USER 1654:1654
EXPOSE 8443
ENTRYPOINT ["dotnet", "Messanger.dll"]
